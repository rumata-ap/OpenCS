using System.Runtime.ExceptionServices;

namespace CScore.LimitSurface;

/// <summary>Узел границы на колонне (N, ψ): параметр луча m*, точка, плоскость, активное ограничение.</summary>
/// <param name="M">Параметр луча момента от оси колонны до границы, кН·м (при оси M = 0 — модуль полного момента).</param>
/// <param name="Plane">Полная пост-трещинная плоскость (ten = false); у <see cref="ConstraintKind.CrackBody"/> — плоскость тела ПС2.</param>
/// <param name="Converged">Ньютон сошёлся во всех пробах до границы; иначе M — последний сошедшийся (нижняя оценка).</param>
/// <param name="ByAxialForce">Точка трещинообразования — на луче полной нагрузки (N за растянутым полюсом тела ПС2).</param>
internal readonly record struct ColumnPoint(double M, ForcePoint Force, Kurvature Plane, ActiveConstraint Active,
    bool Converged, bool ByAxialForce = false, bool NoTensileRebar = false);

/// <summary>
/// Построитель поверхности ширины раскрытия трещин по колоннам (N, ψ): на колонне — постоянная N и
/// направление полного момента (cos ψ, sin ψ) от оси колонны; граница — наименьший момент за телом ПС2,
/// при котором g(m) = max(acrc,long/ult, acrc,short/ult, σs/Rs,ser, εb/εb,ult) = 1. Mcrc, плоскость
/// трещинообразования и вид границы тела ПС2 — по сетке образования трещин (<see cref="CrackStrainCriterion"/>).
/// <para>
/// Ось колонн — ломаная внутри тела ПС2: M = 0 на отрезке N, где точка (N, 0, 0) внутри тела (там Mcrc — по
/// направлению момента, как у crack_width), за ним — отрезки к вершинам тела с наименьшим и наибольшим N, за
/// растянутой вершиной — продолжение при постоянном моменте. У несимметричного сечения крайние по N точки тела
/// ПС2 лежат при M ≠ 0, и ось M = 0 теряла бы часть тела. Где (N, 0, 0) вне тела ПС2, сечение трещиновато от
/// одной N: точка трещинообразования — по лучу полной нагрузки (как у crack_width), граница ищется от выхода
/// колонны из тела ПС2. Если (N, 0, 0) вне тела ни при какой N (обжатие растрескивает грань), ось проходит
/// через центр тяжести тела ПС2.
/// Сжатый полюс — вершина тела ПС2 с наименьшим N, растянутый — корень g = 1 на продолжении оси
/// (у симметричного сечения — центральное растяжение).
/// Параметр колонны t = π·(Nt − N)/(Nt − Nc) ∈ [0, π]; колонны сшиваются как меридианы
/// (<see cref="MeridianStitcher"/>). При N = 0 на каждой колонне два узла: φ3 = 1,0 (N = 0⁻) и 1,2
/// (N = 0⁺) — по норме ступень, не дефект.
/// </para>
/// </summary>
public sealed class AcrcColumnMesher
{
    const int BisectIters = 60;
    const int ExpandIters = 40;
    const int KinkBisectIters = 10;
    const int MaxKinksPerInterval = 6;
    const double TwoPi = 2 * Math.PI;
    /// <summary>Полуширина ступени N = 0 по параметру колонны.</summary>
    const double StepHalfWidth = 1e-9;

    readonly CrossSection _section;
    readonly AcrcSurfaceCriterion _criterion;
    readonly LimitSurfaceOptions _opt;
    readonly double _mRef;
    readonly ForcePoint _compressionPole;
    readonly int _compressionIndex;
    readonly ForcePoint _tensionTip;
    readonly int _tensionIndex;
    readonly ColumnSolver _main;
    int _notConverged;

    /// <summary>Создаёт построитель; строит тело образования трещин, если оно не передано.</summary>
    public AcrcColumnMesher(CrossSection section, AcrcSurfaceCriterion criterion, LimitSurfaceOptions? options = null,
        LimitSurfaceMesh? crackMesh = null, CancellationToken ct = default)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _criterion = criterion ?? throw new ArgumentNullException(nameof(criterion));
        _opt = options ?? new LimitSurfaceOptions();
        CrackMesh = crackMesh ?? new StrainDomainMesher(section, criterion.CrackCriterion, _opt).Build(ct);
        double mMax = CrackMesh.Vertices.Select(v => Math.Sqrt(v.Mx * v.Mx + v.My * v.My)).DefaultIfEmpty(0).Max();
        _mRef = mMax > 0 ? mMax : 1.0;
        var verts = CrackMesh.Vertices;
        _compressionIndex = Enumerable.Range(0, verts.Count).MinBy(i => verts[i].N);
        _compressionPole = verts[_compressionIndex];
        _tensionIndex = Enumerable.Range(0, verts.Count).MaxBy(i => verts[i].N);
        _tensionTip = verts[_tensionIndex];
        var center = Centroid(CrackMesh);
        if (!(center.N > _compressionPole.N && center.N < _tensionTip.N))
            throw new InvalidOperationException($"Тело «{criterion.CrackCriterion.Name}» вырождено по N.");
        // Отрезок оси M = 0 внутри тела ПС2 (с запасом 0,1 % внутрь — концы ломаной строго внутри тела).
        var c0 = new ForcePoint(center.N, 0, 0);
        double span = _tensionTip.N - _compressionPole.N;
        var up = CrackMesh.Utilization(c0, new ForcePoint(10 * span, 0, 0));
        var down = CrackMesh.Utilization(c0, new ForcePoint(-10 * span, 0, 0));
        if (up.RayOriginInside && up.Capacity is { } cu && down.Capacity is { } cd && cu.N - cd.N > 1e-6 * span)
        {
            double margin = 1e-3 * (cu.N - cd.N);
            _axisLo = new ForcePoint(cd.N + margin, 0, 0);
            _axisHi = new ForcePoint(cu.N - margin, 0, 0);
        }
        else
            _axisLo = _axisHi = center;
        _main = new ColumnSolver(this, section);
    }

    /// <summary>Концы средней части оси колонн: при M = 0 — границы отрезка внутри тела ПС2, иначе — центр тяжести.</summary>
    readonly ForcePoint _axisLo, _axisHi;

    /// <summary>Центр тяжести объёма замкнутой сетки (сумма тетраэдров от начала координат).</summary>
    static ForcePoint Centroid(LimitSurfaceMesh mesh)
    {
        double v = 0, n = 0, mx = 0, my = 0;
        foreach (var (a, b, c) in mesh.Triangles)
        {
            ForcePoint p = mesh.Vertices[a], q = mesh.Vertices[b], r = mesh.Vertices[c];
            double dv = (p.N * (q.Mx * r.My - q.My * r.Mx) - p.Mx * (q.N * r.My - q.My * r.N)
                + p.My * (q.N * r.Mx - q.Mx * r.N)) / 6.0;
            v += dv;
            n += dv * (p.N + q.N + r.N) / 4.0;
            mx += dv * (p.Mx + q.Mx + r.Mx) / 4.0;
            my += dv * (p.My + q.My + r.My) / 4.0;
        }
        return Math.Abs(v) > 0 ? new ForcePoint(n / v, mx / v, my / v) : new ForcePoint(0, 0, 0);
    }

    /// <summary>Тело образования трещин (ПС2), по которому берутся Mcrc и плоскость трещинообразования.</summary>
    public LimitSurfaceMesh CrackMesh { get; }

    /// <summary>Наибольший модуль момента тела ПС2 — масштаб моментов построителя, кН·м.</summary>
    public double MomentScale => _mRef;

    /// <summary>Число узлов последнего построения, где Ньютон не сошёлся (взята нижняя оценка).</summary>
    public int NotConvergedCount => _notConverged;

    /// <summary>
    /// Число узлов последнего построения, где в сечении с трещиной нет растянутой арматуры: ширина раскрытия не
    /// определена, граница — тело ПС2 (предупреждение в статистике результата).
    /// </summary>
    public int NoTensileRebarCount => _noTensileRebar;
    int _noTensileRebar;

    /// <summary>Граница колонны (N, ψ); φ3 — по знаку N, если не задан.</summary>
    internal ColumnPoint Column(double n, double psi, double? phi3 = null) => _main.Column(n, psi, phi3);

    /// <summary>g на колонне при параметре луча m — для проверки монотонности; NaN — расчёт не сошёлся или трещин нет.</summary>
    internal double ConditionAt(double n, double psi, double m, double? phi3 = null) => _main.ConditionAt(n, psi, m, phi3);

    /// <summary>Параметр луча колонны до тела ПС2 (Mcrc от оси колонны); 0 — сечение трещиновато от N.</summary>
    internal double CrackRayParameter(double n, double psi) => _main.CrackRayParameter(n, psi);

    /// <summary>Ось колонны при продольной силе n: точка (Mx, My), от которой отсчитывается момент.</summary>
    internal (double Mx, double My) Axis(double n)
    {
        if (n >= _axisLo.N && n <= _axisHi.N)
        {
            double t = _axisHi.N > _axisLo.N ? (n - _axisLo.N) / (_axisHi.N - _axisLo.N) : 0.0;
            return (_axisLo.Mx + t * (_axisHi.Mx - _axisLo.Mx), _axisLo.My + t * (_axisHi.My - _axisLo.My));
        }
        var (from, end) = n < _axisLo.N ? (_axisLo, _compressionPole) : (_axisHi, _tensionTip);
        double w = Math.Clamp((n - from.N) / (end.N - from.N), 0.0, 1.0);
        return (from.Mx + w * (end.Mx - from.Mx), from.My + w * (end.My - from.My));
    }

    /// <summary>Растянутый полюс: продольная сила Nt центрального растяжения с g = 1 и его узел.</summary>
    internal ColumnPoint TensionPole() => _main.TensionPole();

    /// <summary>Строит сетку.</summary>
    public LimitSurfaceMesh Build(CancellationToken ct = default)
    {
        _notConverged = 0;
        _noTensileRebar = 0;
        var tp = _main.TensionPole();
        if (!tp.Converged) Interlocked.Increment(ref _notConverged);
        double nT = tp.Force.N, nC = _compressionPole.N;
        if (!(nT > nC))
            throw new InvalidOperationException($"Поверхность «{_criterion.Name}» не замкнута: растянутый полюс не найден.");
        var top = new MeridianNode(0.0, tp.Plane, tp.Force, tp.Active, null);
        var bot = new MeridianNode(Math.PI, CrackMesh.StrainPlanes[_compressionIndex], _compressionPole,
            CrackMesh.Active[_compressionIndex] with { Kind = ConstraintKind.CrackBody }, null);
        var geo = new ColumnGeometry(nT, nC);

        var pre = Run(geo, [0, Math.PI / 2, Math.PI, 1.5 * Math.PI], null, top, bot, ct, countFailures: false);
        double mMax = pre.SelectMany(m => m).Select(x => Math.Sqrt(x.Force.Mx * x.Force.Mx + x.Force.My * x.Force.My))
            .DefaultIfEmpty(0).Max();
        var scale = new ForcePoint(1 / (nT - nC), 1 / (mMax > 0 ? mMax : 1), 1 / (mMax > 0 ? mMax : 1));

        int n0 = Math.Max(_opt.InitialMeridians, 4);
        var psis0 = Enumerable.Range(0, n0).Select(k => TwoPi * k / n0).ToArray();
        var cols = new SortedDictionary<double, List<MeridianNode>>();
        var first = Run(geo, psis0, scale, top, bot, ct);
        for (int k = 0; k < n0; k++) cols[psis0[k]] = first[k];

        var log = new List<(int Count, double MaxDeviation)>();
        var intervals = Enumerable.Range(0, n0).Select(k => (A: psis0[k], B: k + 1 < n0 ? psis0[k + 1] : TwoPi)).ToList();
        while (intervals.Count > 0 && _opt.RefineTol > 0)
        {
            int room = _opt.MaxMeridians - cols.Count;
            if (room <= 0) break;
            if (intervals.Count > room) intervals = intervals.Take(room).ToList();
            var mids = intervals.Select(iv => 0.5 * (iv.A + iv.B)).ToArray();
            var fresh = Run(geo, mids, scale, top, bot, ct);
            var next = new List<(double Dev, double A, double B)>();
            double maxDev = 0;
            for (int k = 0; k < mids.Length; k++)
            {
                cols[mids[k]] = fresh[k];
                var (a, b) = intervals[k];
                double dev = MeridianStitcher.StripDeviation(top, bot, cols[a], cols[b >= TwoPi ? 0.0 : b], fresh[k], scale);
                maxDev = Math.Max(maxDev, dev);
                if (!(dev <= _opt.RefineTol)) { next.Add((dev, a, mids[k])); next.Add((dev, mids[k], b)); }
            }
            log.Add((mids.Length, maxDev));
            intervals = next.OrderByDescending(e => e.Dev).Select(e => (e.A, e.B)).ToList();
        }

        return MeridianStitcher.Assemble(top, bot, cols.Keys.ToList(), cols.Values.ToList(), log);
    }

    /// <summary>Отображение N ↔ параметр колонны t.</summary>
    sealed record ColumnGeometry(double NT, double NC)
    {
        public double T(double n) => Math.PI * (NT - n) / (NT - NC);
        public double N(double t) => NT - (NT - NC) * t / Math.PI;
    }

    List<List<MeridianNode>> Run(ColumnGeometry geo, IReadOnlyList<double> psis, ForcePoint? scale,
        MeridianNode top, MeridianNode bot, CancellationToken ct, bool countFailures = true)
    {
        var res = new List<MeridianNode>[psis.Count];
        if (_opt.Parallel && psis.Count > 1)
        {
            try
            {
                Parallel.For(0, psis.Count, new ParallelOptions { CancellationToken = ct },
                    i => res[i] = ColumnNodes(new ColumnSolver(this, _section.CloneForCalc()), geo, psis[i], scale, top, bot, countFailures));
            }
            catch (AggregateException ae) when (ae.InnerExceptions.Count > 0)
            {
                ExceptionDispatchInfo.Capture(ae.InnerExceptions[0]).Throw();
                throw;
            }
        }
        else
        {
            for (int i = 0; i < psis.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                res[i] = ColumnNodes(_main, geo, psis[i], scale, top, bot, countFailures);
            }
        }
        return res.ToList();
    }

    /// <summary>Метки якорей ступени N = 0 (точка −2 не совпадает ни с одной контрольной точкой).</summary>
    static readonly KinkTransition StepAnchorTension = new(new(-2, 1, false), new(-2, 0, false));
    static readonly KinkTransition StepAnchorCompression = new(new(-2, 0, false), new(-2, -1, false));

    /// <summary>Ключ излома — вид ограничения (точка тела ПС2 вдоль колонны меняется и изломом не считается).</summary>
    static ActiveConstraint KinkKey(ActiveConstraint a) => new(-1, 0, false, a.Kind);

    List<MeridianNode> ColumnNodes(ColumnSolver solver, ColumnGeometry geo, double psi, ForcePoint? scale,
        MeridianNode top, MeridianNode bot, bool countFailures)
    {
        MeridianNode Node(double t, double? phi3, KinkTransition? tr)
        {
            // Узлы ступени — ровно при N = 0 (параметр t у них сдвинут на ±StepHalfWidth только для порядка).
            var p = solver.Column(phi3.HasValue ? 0.0 : geo.N(t), psi, phi3);
            if (!p.Converged && countFailures) Interlocked.Increment(ref _notConverged);
            if (p.NoTensileRebar && countFailures) Interlocked.Increment(ref _noTensileRebar);
            return new MeridianNode(t, p.Plane, p.Force, p.Active, tr);
        }

        // Узлы по N: равномерная сетка без узлов у N = 0 и пара N = 0⁺ (φ3 = 1,2) / 0⁻ (φ3 = 1,0).
        int nInt = Math.Max(_opt.ThetaIntervals, 4);
        double t0 = geo.T(0.0);
        bool step = t0 > 0 && t0 < Math.PI;
        var plan = new List<(double T, double? Phi3, bool StepLeft)>();
        for (int j = 1; j < nInt; j++)
        {
            double t = Math.PI * j / nInt;
            if (step && Math.Abs(t - t0) < 0.25 * Math.PI / nInt) continue;
            plan.Add((t, null, false));
        }
        if (step)
        {
            plan.Add((t0 - StepHalfWidth, 1.2, true));
            plan.Add((t0 + StepHalfWidth, 1.0, false));
        }
        plan.Sort((a, b) => a.T.CompareTo(b.T));

        // Узлы ступени — якоря сшивки (одинаковый «переход» на всех колоннах): иначе между изломами,
        // стоящими на соседних колоннах при разных N, параметр нормируется по-разному и полоса ступени перекашивается.
        var raw = plan.Select(e => (Node: Node(e.T, e.Phi3,
            e.Phi3.HasValue ? (e.StepLeft ? StepAnchorTension : StepAnchorCompression) : null), e.StepLeft)).ToList();

        // Изломы — смена вида активного ограничения между соседними узлами (не через ступень N = 0).
        var nodes = new List<MeridianNode>();
        var stepLeft = new HashSet<double>();
        for (int i = 0; i < raw.Count; i++)
        {
            if (i > 0 && !raw[i - 1].StepLeft)
                Kinks(raw[i - 1].Node, raw[i].Node, 0, nodes);
            nodes.Add(raw[i].Node);
            if (raw[i].StepLeft) stepLeft.Add(raw[i].Node.T);
        }

        void Kinks(MeridianNode a, MeridianNode b, int depth, List<MeridianNode> outList)
        {
            if (a.Active.Kind == b.Active.Kind || depth > MaxKinksPerInterval) return;
            MeridianNode lo = a, hi = b;
            for (int it = 0; it < KinkBisectIters; it++)
            {
                var mid = Node(0.5 * (lo.T + hi.T), null, null);
                if (mid.Active.Kind == a.Active.Kind) lo = mid; else hi = mid;
            }
            var kink = hi with { Transition = new KinkTransition(KinkKey(a.Active), KinkKey(hi.Active)) };
            outList.Add(kink);
            Kinks(kink, b, depth + 1, outList);
        }

        if (scale is not { } s || _opt.RefineTol <= 0) return nodes;

        // Адаптация по N: середина интервала против хорды в нормированных усилиях (интервалы у полюсов — тоже).
        IEnumerable<MeridianNode> Refine(MeridianNode a, MeridianNode b, int depth)
        {
            if (depth >= _opt.MaxThetaDepth) return [];
            var m = Node(0.5 * (a.T + b.T), null, null);
            double dev = SurfaceMeshGeometry.PointSegmentDistance(m.Force.Scale(s), a.Force.Scale(s), b.Force.Scale(s));
            if (dev <= _opt.RefineTol) return [m];
            return Refine(a, m, depth + 1).Append(m).Concat(Refine(m, b, depth + 1)).ToList();
        }

        var refined = Refine(top, nodes[0], 0).ToList();
        refined.Add(nodes[0]);
        for (int i = 0; i + 1 < nodes.Count; i++)
        {
            if (!stepLeft.Contains(nodes[i].T)) refined.AddRange(Refine(nodes[i], nodes[i + 1], 0));
            refined.Add(nodes[i + 1]);
        }
        refined.AddRange(Refine(nodes[^1], bot, 0));
        return refined;
    }

    /// <summary>Расчёт колонн на одном экземпляре сечения (не потокобезопасен).</summary>
    sealed class ColumnSolver(AcrcColumnMesher owner, CrossSection section)
    {
        readonly CrackWidthCore _core = owner._criterion.CreateCore(section);

        /// <summary>Проба g(m): результат ядра и вид наибольшего условия.</summary>
        readonly record struct Probe(bool Ok, double G, ConstraintKind Kind, CrackWidthResult? Result,
            bool NoTensileRebar = false);

        /// <summary>Трещинообразование на колонне по телу ПС2.</summary>
        /// <param name="LongCrc">Параметр луча колонны, с которого трещины образует длительная нагрузка
        /// k·(ось + m·направление); 0 — длительная трещиновата уже на оси.</param>
        readonly record struct Start(bool Axial, bool CompressionBody, double Mcrc, ForcePoint Capacity,
            Kurvature CrackPlane, ActiveConstraint Active, CrackOnset? Onset, double AxisMx, double AxisMy,
            double LongCrc = 0.0);

        /// <summary>Параметр луча до тела ПС2 от точки (n, mx, my) в направлении (c, s); null — точка вне тела.</summary>
        (double M, SurfaceUtilization U)? CrackRay(double n, double mx, double my, double c, double s)
        {
            double r = 10.0 * owner._mRef;
            var u = owner.CrackMesh.Utilization(new ForcePoint(n, mx, my), new ForcePoint(0, r * c, r * s));
            if (!u.RayOriginInside || u.Capacity is not { } cap || u.StrainPlane is null) return null;
            return (Math.Sqrt((cap.Mx - mx) * (cap.Mx - mx) + (cap.My - my) * (cap.My - my)), u);
        }

        Start MomentStart(double n, double c, double s)
        {
            var (ax, ay) = owner.Axis(n);
            // Ось колонны вне тела ПС2 — сечение трещиновато уже от продольной силы.
            if (CrackRay(n, ax, ay, c, s) is not { } ray)
                return new Start(Axial: true, false, 0.0, new ForcePoint(n, ax, ay), default, default, null, ax, ay);
            var cap = ray.U.Capacity!.Value;
            var plane = ray.U.StrainPlane!.Value;
            var act = ray.U.Active ?? default;
            if (act.Side < 0)
                return new Start(false, CompressionBody: true, ray.M, cap, plane, act, null, ax, ay);
            // (N, 0, 0) вне тела ПС2 — трещины от одной N: точка трещинообразования по лучу полной нагрузки
            // на каждой пробе (как у crack_width), граница — от выхода колонны из тела ПС2. Запасная точка —
            // выход колонны из тела ПС2: если луч из нуля не определён (начало координат вне тела ПС2).
            if ((ax != 0.0 || ay != 0.0) && CrackRay(n, 0.0, 0.0, c, s) is null)
                return new Start(Axial: true, false, ray.M, cap, plane, act,
                    _core.Onset(cap.N, cap.Mx, cap.My, plane, byAxialForce: true), ax, ay);
            var onset = _core.Onset(cap.N, cap.Mx, cap.My, plane, byAxialForce: false);
            // Длительные усилия k·(ось + m·e) лежат на луче из k·ось с параметром k·m: один запрос на колонну.
            double k = owner._criterion.LongShare;
            double longCrc = k >= 1.0 ? ray.M
                : CrackRay(n, k * ax, k * ay, c, s) is { } lr ? lr.M / k : 0.0;
            return new Start(false, false, ray.M, cap, plane, act, onset, ax, ay, longCrc);
        }

        /// <summary>Точка трещинообразования на луче полной нагрузки (N за растянутым полюсом тела ПС2).</summary>
        CrackOnset? AxialOnset(double n, double mx, double my)
        {
            var u = owner.CrackMesh.Utilization(n, mx, my, UtilizationMode.All);
            if (!u.RayOriginInside || u.Inside || u.Capacity is not { } cap || u.StrainPlane is not { } plane) return null;
            return _core.Onset(cap.N, cap.Mx, cap.My, plane, byAxialForce: true);
        }

        Probe Evaluate(Start st, double n, double c, double s, double m, double phi3, Probe seed)
        {
            double mx = st.AxisMx + m * c, my = st.AxisMy + m * s;
            var onset = st.Axial ? AxialOnset(n, mx, my) ?? st.Onset : st.Onset;
            if (onset is null) return default;
            double k = owner._criterion.LongShare;
            // Образует ли трещины длительная нагрузка — по телу ПС2 (точно при любом направлении момента).
            bool longCracked = st.Axial || m > st.LongCrc;
            var res = _core.Evaluate(onset, n, k * mx, k * my, mx, my, phi3,
                seed.Result?.PlaneLong, seed.Result?.PlaneTotal, longCracked);
            // Трещина есть, но стержни ещё не растянуты (обычно при большом сжатии): σs ≤ 0, acrc = 0 — g = 0,
            // момент наращивается дальше.
            if (res.NoTensileRebar) return new Probe(true, 0.0, ConstraintKind.CrackBody, null, NoTensileRebar: true);
            if (!res.Cracked || !res.TotalConverged) return default;
            var conds = new (double G, ConstraintKind Kind)[]
            {
                (res.AcrcLong / owner._criterion.AcrcUltLong, ConstraintKind.AcrcLong),
                (res.AcrcShort / owner._criterion.AcrcUltShort, ConstraintKind.AcrcShort),
                (res.UtilSteel, ConstraintKind.SteelStress),
                (res.UtilConcrete, ConstraintKind.ConcreteCompression),
            };
            var worst = conds.MaxBy(x => x.G);
            return new Probe(true, worst.G, worst.Kind, res);
        }

        public double ConditionAt(double n, double psi, double m, double? phi3)
        {
            double c = Math.Cos(psi), s = Math.Sin(psi);
            var st = MomentStart(n, c, s);
            if (st.CompressionBody || (!st.Axial && st.Onset is null)) return double.NaN;
            var p = Evaluate(st, n, c, s, m, phi3 ?? Phi3(n), default);
            return p.Ok ? p.G : double.NaN;
        }

        public double CrackRayParameter(double n, double psi) => MomentStart(n, Math.Cos(psi), Math.Sin(psi)).Mcrc;

        static double Phi3(double n) => n > 0 ? 1.2 : 1.0;

        static ActiveConstraint KindConstraint(ConstraintKind kind) =>
            new(-1, kind == ConstraintKind.ConcreteCompression ? -1 : 1, false, kind);

        static ColumnPoint Point(Start st, double n, double c, double s, double m, Kurvature plane, ActiveConstraint act, bool conv) =>
            new(m, new ForcePoint(n, st.AxisMx + m * c, st.AxisMy + m * s), plane, act, conv, st.Axial);

        public ColumnPoint Column(double n, double psi, double? phi3Override)
        {
            double c = Math.Cos(psi), s = Math.Sin(psi);
            double phi3 = phi3Override ?? Phi3(n);
            var st = MomentStart(n, c, s);
            var crackBody = st.Active with { Kind = ConstraintKind.CrackBody };

            // Граница тела ПС2 — сжатие бетона раньше трещины: дальше по моменту не пройти.
            if (st.CompressionBody)
                return Point(st, n, c, s, st.Mcrc, st.CrackPlane, crackBody, true);
            if (!st.Axial && st.Onset is null)
                return Point(st, n, c, s, st.Mcrc, st.CrackPlane, crackBody, false);

            double m0 = st.Mcrc * (1.0 + 1e-9);
            var lo = Evaluate(st, n, c, s, m0, phi3, default);
            // Растянутая арматура так и не появилась до последней сошедшейся пробы — ширина раскрытия не
            // определена, граница — тело ПС2 (предупреждение в статистике).
            ColumnPoint NoRebar() => Point(st, n, c, s, st.Mcrc, st.CrackPlane, crackBody, true) with { NoTensileRebar = true };
            if (!lo.Ok)
                return Point(st, n, c, s, st.Mcrc, st.CrackPlane, crackBody, false);
            // Раскрытие сразу при образовании трещин превышает предел — граница совпадает с телом ПС2.
            if (lo.G >= 1.0)
            {
                var plane = st.Axial ? lo.Result!.PlaneTotal ?? default : st.CrackPlane;
                var act = st.Axial ? KindConstraint(ConstraintKind.CrackBody) : crackBody;
                return Point(st, n, c, s, m0, plane, act, true);
            }

            double mLo = m0, mHi = double.NaN;
            Probe hi = default;
            bool hiFailed = false;
            double step = Math.Max(st.Mcrc, 0.05 * owner._mRef);
            for (int it = 0; it < ExpandIters; it++)
            {
                double m = mLo + step;
                var p = Evaluate(st, n, c, s, m, phi3, lo);
                if (!p.Ok) { mHi = m; hiFailed = true; break; }
                if (p.G >= 1.0) { mHi = m; hi = p; break; }
                mLo = m; lo = p; step *= 2.0;
            }
            if (lo.NoTensileRebar && (double.IsNaN(mHi) || hiFailed)) return NoRebar();
            if (double.IsNaN(mHi))
                return Point(st, n, c, s, mLo, lo.Result!.PlaneTotal ?? default, KindConstraint(lo.Kind), false);

            // Корень g(m) = 1: метод Иллинойса (регула фальси с половинением застрявшего конца), пока
            // верх — сошедшаяся проба; несошедшийся верх — бисекция.
            double tol = 1e-6 * owner._mRef;
            double fLo = lo.G - 1.0, fHi = hi.Ok ? hi.G - 1.0 : double.NaN;
            int side = 0;
            for (int it = 0; it < BisectIters && mHi - mLo > tol; it++)
            {
                double m = hiFailed || double.IsNaN(fHi) || !(fHi > fLo)
                    ? 0.5 * (mLo + mHi)
                    : mLo - fLo * (mHi - mLo) / (fHi - fLo);
                // шаг в краевые 1 % интервала — сходимость с одной стороны, страхуемся серединой
                double span = mHi - mLo;
                if (m <= mLo + 0.01 * span || m >= mHi - 0.01 * span)
                    m = Math.Clamp(m, mLo + 0.01 * span, mHi - 0.01 * span);
                var p = Evaluate(st, n, c, s, m, phi3, lo);
                if (!p.Ok) { mHi = m; hiFailed = true; fHi = double.NaN; side = 0; }
                else if (p.G >= 1.0)
                {
                    mHi = m; hi = p; hiFailed = false; fHi = p.G - 1.0;
                    if (side == 1) fLo *= 0.5;
                    side = 1;
                }
                else
                {
                    mLo = m; lo = p; fLo = p.G - 1.0;
                    if (side == -1 && !double.IsNaN(fHi)) fHi *= 0.5;
                    side = -1;
                }
                // интервал по m уже мал, а g у верха в пределах 1e-6 — граница найдена
                if (!hiFailed && hi.Ok && hi.G - 1.0 <= 1e-6) break;
            }

            // Верхняя граница — несошедшийся расчёт: граница не найдена, берётся последняя сошедшаяся проба.
            if (hiFailed || !hi.Ok)
            {
                if (lo.NoTensileRebar) return NoRebar();
                return Point(st, n, c, s, mLo, lo.Result!.PlaneTotal ?? default, KindConstraint(lo.Kind), false);
            }
            return Point(st, n, c, s, mHi, hi.Result!.PlaneTotal ?? default, KindConstraint(hi.Kind), true);
        }

        /// <summary>g на оси колонн при продольной силе N (M = 0 от оси); 0 — трещин нет (точка внутри тела ПС2).</summary>
        Probe PoleProbe(double n, Probe seed)
        {
            var (ax, ay) = owner.Axis(n);
            if (CrackRay(n, ax, ay, 1.0, 0.0) is not null)
                return new Probe(true, 0.0, ConstraintKind.CrackBody, null);
            // Запасная точка трещинообразования (луч из нуля не определён) — вершина тела ПС2 с наибольшим N.
            var tip = owner._tensionTip;
            _tipOnset ??= _core.Onset(tip.N, tip.Mx, tip.My, owner.CrackMesh.StrainPlanes[owner._tensionIndex], byAxialForce: true);
            var st = new Start(Axial: true, false, 0.0, new ForcePoint(n, ax, ay), default, default, _tipOnset, ax, ay);
            return Evaluate(st, n, 1.0, 0.0, 0.0, 1.2, seed);
        }

        CrackOnset? _tipOnset;

        public ColumnPoint TensionPole()
        {
            // От вершины тела ПС2 с наибольшим N (тело acrc не короче тела ПС2) вдоль продолжения оси колонн.
            var tip = owner._tensionTip;
            double nLo = tip.N, nHi = double.NaN;
            Probe lo = PoleProbe(nLo, default), hi = default;
            if (!lo.Ok || lo.G >= 1.0)
                return new ColumnPoint(0.0, tip, owner.CrackMesh.StrainPlanes[owner._tensionIndex],
                    owner.CrackMesh.Active[owner._tensionIndex] with { Kind = ConstraintKind.CrackBody }, lo.Ok,
                    ByAxialForce: true);
            bool hiFailed = false;
            double step = Math.Max(tip.N - owner._axisHi.N, 1.0);
            for (int it = 0; it < ExpandIters; it++)
            {
                double n = nLo + step;
                var p = PoleProbe(n, lo);
                if (!p.Ok) { nHi = n; hiFailed = true; break; }
                if (p.G >= 1.0) { nHi = n; hi = p; break; }
                nLo = n; lo = p; step *= 2.0;
            }
            if (double.IsNaN(nHi))
                throw new InvalidOperationException(
                    $"Поверхность «{owner._criterion.Name}» не замкнута: при центральном растяжении граница не достигается.");

            double tol = 1e-7 * Math.Max(nHi, 1.0);
            for (int it = 0; it < BisectIters && nHi - nLo > tol; it++)
            {
                double n = 0.5 * (nLo + nHi);
                var p = PoleProbe(n, lo.Result is null ? default : lo);
                if (!p.Ok) { nHi = n; hiFailed = true; }
                else if (p.G >= 1.0) { nHi = n; hi = p; hiFailed = false; }
                else { nLo = n; lo = p; }
            }

            var best = hiFailed || !hi.Ok ? lo : hi;
            double nPole = hiFailed || !hi.Ok ? nLo : nHi;
            // Трещин ещё нет у полюса (g перескакивает с 0) — граница совпадает с телом ПС2.
            var kind = best.Result is null || lo.Result is null ? ConstraintKind.CrackBody : best.Kind;
            var plane = best.Result?.PlaneTotal ?? default;
            var (pmx, pmy) = owner.Axis(nPole);
            return new ColumnPoint(0.0, new ForcePoint(nPole, pmx, pmy), plane, KindConstraint(kind),
                !(hiFailed || !hi.Ok), ByAxialForce: true);
        }
    }
}
