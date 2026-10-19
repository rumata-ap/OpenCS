using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>Метод назначения расчётных длин: по формам общей устойчивости (<see cref="RcBucklingLengths"/>), по номограммам ASCE, по табл. 31 СП 16.</summary>
public enum RcLengthMethod { Mode, Asce, Sp16 }

/// <summary>
/// Как решается, есть ли смещение концов элемента в плоскости: <see cref="ByMode"/> — по общей форме, в которой элемент
/// участвует (доля смещения хорды в работе продольной силы); <see cref="Stiffness"/> — по жёсткости остальной схемы на
/// взаимное смещение концов (r₃ окружения) против 12EI/l³ элемента; <see cref="Manual"/> — задано пользователем.
/// </summary>
public enum RcSwayRule { ByMode, Stiffness, Manual }

/// <summary>Почему плоскость элемента со смещением или без (<see cref="RcNomogramPlane.SwayValue"/> — величина признака).</summary>
public enum RcSwayReason { ModeChord, NotInModes, Stiffness, NoSurround, Manual }

/// <summary>
/// Параметры номограммного расчёта: метод, правило смещения, G опорных узлов по рекомендации AISC (заделка — 1, шарнир
/// — 10), порог жёсткости окружения (несмещаемо при r₃ ≥ <paramref name="SwayStiffness"/>·12EI/l³) и порог доли
/// смещения хорды в форме (со смещением при доле ≥ <paramref name="SwayChord"/>).
/// </summary>
public sealed record RcNomogramOptions(RcLengthMethod Method = RcLengthMethod.Asce, RcSwayRule Sway = RcSwayRule.ByMode,
    double GFixed = 1, double GPinned = 10, double SwayStiffness = 5, double SwayChord = 0.5);

/// <summary>Дальний конец ригеля: узел (обычное примыкание), заделка, шарнир (шарнир стержня или шарнирная опора), свободный.</summary>
public enum RcFarEnd { Joint, Fixed, Pinned, Free }

/// <summary>
/// Стержень у узла конца проверяемого элемента: расчётный элемент <paramref name="Unit"/> (−1 — стержень вне
/// элементов, тогда <paramref name="Beam"/> — индекс стержня сетки), колонна (соосен проверяемому) или ригель,
/// погонная жёсткость EI/L относительно оси изгиба проверяемого (Н·м; изгиб по осям стержня в проекции, без кручения),
/// длина L до конца элемента, коэффициент к ней (дальний конец по ASCE и продольная сила ригеля, ур. (2.12)), сжатие
/// ригеля Q, Н (плюс — сжатие), шарнир у узла (вклад — 0).
/// </summary>
public sealed record RcNomogramArm(int Unit, int Beam, bool Column, double Stiffness, double Length, double Factor,
    RcFarEnd Far, double Q, bool Released);

/// <summary>
/// Узел конца элемента: индекс узла сетки, G (+∞ — нет закрепления от поворота), суммы погонных жёсткостей колонн
/// (с самим элементом) и ригелей (с коэффициентами и пружинами), опора узла (заделка от поворота — G по AISC,
/// шарнирная — не больше G шарнира), эквивалент пружины поворота (Н·м: k/6 со смещением, k/2 без), стержни узла, число
/// примыкающих пластин (не учитываются).
/// </summary>
public sealed record RcNomogramEnd(int Node, double G, double Columns, double Girders, bool SupportFixed, bool SupportPinned,
    double Spring, IReadOnlyList<RcNomogramArm> Arms, int Shells, bool OwnReleased);

/// <summary>
/// Номограммный расчёт элемента в плоскости: метод (<see cref="RcLengthMethod.Asce"/> или
/// <see cref="RcLengthMethod.Sp16"/>; для наклонных и горизонтальных элементов СП 16 заменяется ASCE —
/// <paramref name="Sp16NotApplicable"/>), смещение и его причина, концы (A — I-конец элемента, у СП 16 — нижний), K;
/// для СП 16 — формула, приведённые p и n, этаж, число пролётов k.
/// </summary>
public sealed record RcNomogramPlane(RcLengthMethod Method, bool Sway, RcSwayReason SwayReason, double SwayValue,
    RcNomogramEnd A, RcNomogramEnd B, double K, Sp16FrameMu? Sp16 = null, Sp16Storey Storey = Sp16Storey.Single,
    int Spans = 0, bool Sp16NotApplicable = false);

/// <summary>
/// Поэлементный расчёт μ по номограммам (ASCE 1997, п. 2.3.1, ур. (2.9)–(2.13)) и по табл. 31 СП 16 для произвольной
/// стержневой схемы. Для расчётного элемента и плоскости его изгиба (ось a — местная z или y) у каждого конца
/// собираются стержни узла: соосные элементу — колонны, прочие — ригели; вклад — EI/L относительно оси a по
/// проекциям осей изгиба стержня (кручение не учитывается, как в номограмме), шарнир у узла — 0, L — до конца
/// расчётного элемента стержня. Ригели ASCE умножаются на коэффициент дальнего конца (со смещением: заделка 2/3, шарнир
/// 1/2, свободный 1/6; без смещения: 2; 1,5; 1/2) и на 1 − Q/Q_cr при сжатии (ур. 2.12). Опорный узел, закреплённый от
/// поворота вокруг a, — G по AISC (по умолчанию 1), шарнирный — не больше G шарнира (10). Пружины поворота — как ригель
/// k/6 (k/2). Смещение — по правилу <see cref="RcSwayRule"/>. N_cr = π²EI/(μl)².
/// </summary>
public static class RcNomogramLengths
{
    // Соосность: |cos| угла между осями, при котором стержень — продолжение колонны; вертикальность для СП 16.
    const double CollinearCos = 0.996, VerticalCos = 0.9;

    /// <param name="result">Задача устойчивости по сочетанию (сетка, перемещения сочетания, формы).</param>
    /// <param name="units">Все расчётные элементы схемы — номера стержней исходной модели.</param>
    /// <param name="which">Элементы, которым назначаются длины.</param>
    /// <param name="byMode">Назначение по формам (<see cref="RcBucklingLengths.Assign"/>) — форма для правила смещения
    /// и справочное значение.</param>
    /// <param name="local">Локальные задачи (жёсткости окружения для правила <see cref="RcSwayRule.Stiffness"/>).</param>
    /// <param name="manual">Правило <see cref="RcSwayRule.Manual"/>: (элемент, плоскость xy) → со смещением.</param>
    /// <returns>По элементу в порядке <paramref name="units"/>: плоскости с источником
    /// <see cref="RcBucklingLengthSource.Nomogram"/> (по формам — в <see cref="RcBucklingPlaneLength.Form"/>);
    /// элементы вне <paramref name="which"/> и без сжатия — как в <paramref name="byMode"/>.</returns>
    public static RcBucklingUnitLength?[] Assign(RcBucklingResult result, IReadOnlyList<IReadOnlyList<int>> units,
        IEnumerable<int> which, IReadOnlyList<RcBucklingUnitLength?> byMode, RcNomogramOptions options,
        IReadOnlyList<StructuralLocalBuckling?>? local = null, Func<int, bool, bool>? manual = null)
    {
        var topo = new Topology(result, units);
        var lengths = byMode.ToArray();
        foreach (int i in which.Distinct())
        {
            if (byMode[i] is not { } x) continue;
            lengths[i] = x with
            {
                AboutZ = Plane(topo, i, true, x, x.AboutZ, options, local?[i], manual),
                AboutY = Plane(topo, i, false, x, x.AboutY, options, local?[i], manual),
            };
        }
        return lengths;
    }

    static RcBucklingPlaneLength Plane(Topology t, int unit, bool xy, RcBucklingUnitLength x, RcBucklingPlaneLength form,
        RcNomogramOptions o, StructuralLocalBuckling? local, Func<int, bool, bool>? manual)
    {
        var (sway, reason, value) = Sway(t, unit, xy, x, form, o, local, manual);
        var nomo = Compute(t, unit, xy, sway, reason, value, o);
        double ei = form.EI, l = x.Length;
        double mu = nomo.Sp16 is { } s ? s.Mu : nomo.K;
        if (!(double.IsFinite(mu) && mu > 0))
            return new(RcBucklingLengthSource.Nomogram, double.NaN, double.NaN, ei, Nomogram: nomo, Form: form);
        double ncr = Math.PI * Math.PI * ei / (mu * mu * l * l);
        return new(RcBucklingLengthSource.Nomogram, mu, ncr, ei, Nomogram: nomo, Form: form);
    }

    // Смещение концов в плоскости по правилу.
    static (bool Sway, RcSwayReason Reason, double Value) Sway(Topology t, int unit, bool xy, RcBucklingUnitLength x,
        RcBucklingPlaneLength form, RcNomogramOptions o, StructuralLocalBuckling? local, Func<int, bool, bool>? manual)
    {
        switch (o.Sway)
        {
            case RcSwayRule.Manual when manual != null:
                return (manual(unit, xy), RcSwayReason.Manual, double.NaN);
            case RcSwayRule.Stiffness:
                var around = xy ? local?.Surround?.Xy : local?.Surround?.Xz;
                if (around != null)
                {
                    double own = 12 * form.EI / Math.Pow(x.Length, 3);
                    double ratio = around.R3 / own;
                    return (!(ratio >= o.SwayStiffness), RcSwayReason.Stiffness, ratio);
                }
                break;
        }
        if (form.Mode < 0) return (false, o.Sway == RcSwayRule.ByMode ? RcSwayReason.NotInModes : RcSwayReason.NoSurround, double.NaN);
        double share = t.ChordShare(unit, xy, form.Mode);
        return (share >= o.SwayChord, o.Sway == RcSwayRule.ByMode ? RcSwayReason.ModeChord : RcSwayReason.NoSurround, share);
    }

    static RcNomogramPlane Compute(Topology t, int unit, bool xy, bool sway, RcSwayReason reason, double value, RcNomogramOptions o)
    {
        var axis = t.BendingAxis(unit, xy);
        var (nodeA, nodeB) = t.Ends(unit);
        bool vertical = Math.Abs(t.Direction(unit)[2]) >= VerticalCos;
        bool sp16 = o.Method == RcLengthMethod.Sp16 && vertical;
        if (sp16 && t.Mesh.Nodes[nodeA][2] > t.Mesh.Nodes[nodeB][2]) (nodeA, nodeB) = (nodeB, nodeA);
        var a = End(t, unit, nodeA, axis, sway, o, sp16);
        var b = End(t, unit, nodeB, axis, sway, o, sp16);
        if (!sp16)
        {
            double k = sway ? RcAlignmentChart.Sway(a.G, b.G) : RcAlignmentChart.Braced(a.G, b.G);
            return new(RcLengthMethod.Asce, sway, reason, value, a, b, k, Sp16NotApplicable: o.Method == RcLengthMethod.Sp16);
        }
        // СП 16: p — ригели у нижнего конца, n — у верхнего, в долях погонной жёсткости самой колонны.
        double own = t.OwnStiffness(unit, nodeA, axis);
        double Sum(RcNomogramEnd e) => e.Arms.Where(r => !r.Column).Sum(r => r.Stiffness) / own;
        bool topFree = !b.Arms.Any(r => r.Column && !r.Released);
        bool supported = a.SupportFixed || a.SupportPinned;
        bool bottom = supported || !a.Arms.Any(r => r.Column && !r.Released);
        double p = a.SupportFixed ? double.PositiveInfinity : Sum(a);
        double n = Sum(b);
        var storey = topFree && bottom ? Sp16Storey.Single : topFree ? Sp16Storey.Top : bottom ? Sp16Storey.Bottom : Sp16Storey.Middle;
        int spans = t.Spans(unit, b.Arms.Any(r => !r.Column && r.Stiffness > 0) ? nodeB : nodeA, axis);
        var mu = Sp16FrameTable.Mu(sway, storey, p, n, spans);
        return new(RcLengthMethod.Sp16, sway, reason, value, a, b, mu.Mu, mu, storey, spans);
    }

    // Узел конца: стержни, суммы, G.
    static RcNomogramEnd End(Topology t, int unit, int node, double[] axis, bool sway, RcNomogramOptions o, bool plain)
    {
        var arms = new List<RcNomogramArm>();
        double columns = 0, girders = 0;
        bool ownReleased = false;
        foreach (var arm in t.Arms(node))
        {
            double stiff = t.ArmStiffness(arm, axis, out bool released);
            if (arm.Unit == unit)
            {
                // Сам элемент — в числитель G; шарнир на конце — узел его не держит.
                if (released) ownReleased = true;
                else columns += stiff;
                continue;
            }
            bool column = Math.Abs(Dot(arm.Direction, t.Direction(unit))) >= CollinearCos;
            double factor = 1;
            var far = t.FarEnd(arm, axis);
            double q = -t.Axial(arm.Beam);
            if (!column && !plain)
            {
                factor = (sway, far) switch
                {
                    (true, RcFarEnd.Fixed) => 2.0 / 3, (true, RcFarEnd.Pinned) => 0.5, (true, RcFarEnd.Free) => 1.0 / 6,
                    (false, RcFarEnd.Fixed) => 2.0, (false, RcFarEnd.Pinned) => 1.5, (false, RcFarEnd.Free) => 0.5,
                    _ => 1.0,
                };
                // Сжатие ригеля снижает его жёсткость (ур. 2.12); растяжение в запас не учитывается.
                double qcr = Math.PI * Math.PI * stiff * arm.Length;   // π²·EI/L² = π²·(EI/L)/L
                if (q > 0 && qcr > 0) factor *= 1 - q / qcr;
            }
            double value = released ? 0 : stiff;
            if (column) columns += value; else girders += factor * value;
            arms.Add(new RcNomogramArm(arm.Unit, arm.Beam, column, value, arm.Length, factor, far, q, released));
        }
        double spring = t.RotationalSpring(node, axis) / (sway ? 6 : 2);
        if (!plain) girders += spring;
        // Шарнирная опора — узел держит элемент вдоль его оси и колонна дальше не продолжается (закрепление этажа
        // из плоскости — не опора элемента).
        bool supportFixed = t.RotationFixed(node, axis);
        bool supportPinned = !supportFixed && t.AxiallySupported(node, t.Direction(unit)) && !arms.Any(r => r.Column);
        double g = ownReleased ? double.PositiveInfinity
            : supportFixed ? o.GFixed
            : girders > 0 ? columns / girders : double.PositiveInfinity;
        if (supportPinned && !ownReleased) g = Math.Min(g, o.GPinned);
        return new RcNomogramEnd(node, g, columns, girders, supportFixed, supportPinned, plain ? 0 : spring, arms,
            t.ShellCount(node), ownReleased);
    }

    static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    /// <summary>Отрезок стержня от узла до конца его расчётного элемента: первый стержень сетки у узла, направление от узла.</summary>
    sealed record Arm(int Unit, int Beam, int NearNode, int FarNode, int FarBeam, double Length, double[] Direction);

    // Топология сетки: цепочки узлов элементов, стержни у узлов, опоры.
    sealed class Topology
    {
        public StructuralMesh Mesh { get; }
        /// <summary>Продольная сила стержня сетки задачи (огибающая или по сочетанию), растяжение — плюс.</summary>
        public double Axial(int beam) => _result.Axial(beam);
        readonly RcBucklingResult _result;
        readonly int[][] _nodes;          // узлы элемента по порядку
        readonly int[][] _beams;          // стержни сетки элемента по порядку
        readonly double[][] _s;           // накопленная длина в узлах
        readonly Dictionary<int, List<Arm>> _arms = new();
        readonly int[] _shellCount;

        public Topology(RcBucklingResult result, IReadOnlyList<IReadOnlyList<int>> units)
        {
            _result = result;
            Mesh = result.Build.Mesh;
            int n = units.Count;
            _nodes = new int[n][];
            _beams = new int[n][];
            _s = new double[n][];
            var owner = new int[Mesh.Beams.Count];
            Array.Fill(owner, -1);
            for (int u = 0; u < n; u++)
            {
                var subs = units[u].SelectMany(id => result.SubBeams[id]).ToArray();
                _beams[u] = subs;
                _nodes[u] = Chain(subs);
                _s[u] = new double[_nodes[u].Length];
                for (int k = 1; k < _nodes[u].Length; k++)
                    _s[u][k] = _s[u][k - 1] + Distance(_nodes[u][k - 1], _nodes[u][k]);
                foreach (int b in subs) owner[b] = u;
            }
            for (int u = 0; u < n; u++)
            {
                var nodes = _nodes[u];
                var subs = _beams[u];
                double total = _s[u][^1];
                for (int k = 0; k < subs.Length; k++)
                {
                    // От узла k — к концу цепочки, от узла k + 1 — к началу.
                    Add(new Arm(u, subs[k], nodes[k], nodes[^1], subs[^1], total - _s[u][k], Dir(nodes[k], nodes[k + 1])));
                    Add(new Arm(u, subs[k], nodes[k + 1], nodes[0], subs[0], _s[u][k + 1], Dir(nodes[k + 1], nodes[k])));
                }
            }
            // Стержни сетки вне расчётных элементов — сами по себе.
            for (int b = 0; b < Mesh.Beams.Count; b++)
            {
                if (owner[b] >= 0) continue;
                var beam = Mesh.Beams[b];
                double l = Distance(beam.I, beam.J);
                Add(new Arm(-1, b, beam.I, beam.J, b, l, Dir(beam.I, beam.J)));
                Add(new Arm(-1, b, beam.J, beam.I, b, l, Dir(beam.J, beam.I)));
            }
            _shellCount = new int[Mesh.NNodes];
            foreach (var s in Mesh.Shells) foreach (int node in s.Nodes) _shellCount[node]++;
        }

        void Add(Arm a)
        {
            if (!_arms.TryGetValue(a.NearNode, out var list)) _arms[a.NearNode] = list = [];
            list.Add(a);
        }

        public IReadOnlyList<Arm> Arms(int node) => _arms.TryGetValue(node, out var list) ? list : [];

        public int ShellCount(int node) => _shellCount[node];

        public (int A, int B) Ends(int unit) => (_nodes[unit][0], _nodes[unit][^1]);

        public double[] Direction(int unit) => Dir(_nodes[unit][0], _nodes[unit][^1]);

        // Ось изгиба плоскости: xy — местная z первого стержня, xz — местная y.
        public double[] BendingAxis(int unit, bool xy)
        {
            var (r, _) = Frame(_beams[unit][0]);
            int row = xy ? 2 : 1;
            return [r[row, 0], r[row, 1], r[row, 2]];
        }

        // Поперечное направление плоскости: xy — местная y, xz — местная z.
        double[] Transverse(int unit, bool xy)
        {
            var (r, _) = Frame(_beams[unit][0]);
            int row = xy ? 1 : 2;
            return [r[row, 0], r[row, 1], r[row, 2]];
        }

        (double[,] R, double L) Frame(int beam) => BeamElements.Beam3dFrame(Mesh.BeamCoords(beam), Mesh.Beams[beam].RefVec);

        /// <summary>EI/L отрезка относительно оси a; шарнир стержня у ближнего узла по поворотам — исключается по осям.</summary>
        public double ArmStiffness(Arm arm, double[] axis, out bool released)
        {
            var beam = Mesh.Beams[arm.Beam];
            var (r, _) = Frame(arm.Beam);
            var (_, eiy, eiz, _) = BeamElements.SectionStiffness(beam.Section);
            int rel = beam.I == arm.NearNode ? beam.ReleaseI : beam.ReleaseJ;
            double cy = r[1, 0] * axis[0] + r[1, 1] * axis[1] + r[1, 2] * axis[2];
            double cz = r[2, 0] * axis[0] + r[2, 1] * axis[1] + r[2, 2] * axis[2];
            bool freeY = (rel & (1 << 4)) != 0, freeZ = (rel & (1 << 5)) != 0;
            double full = (eiy * cy * cy + eiz * cz * cz) / arm.Length;
            double kept = ((freeY ? 0 : eiy * cy * cy) + (freeZ ? 0 : eiz * cz * cz)) / arm.Length;
            released = full > 0 && kept < 1e-6 * full;
            return released ? full : kept;
        }

        /// <summary>Погонная жёсткость самого элемента у его конца (без учёта шарнира).</summary>
        public double OwnStiffness(int unit, int node, double[] axis)
        {
            var arm = Arms(node).First(a => a.Unit == unit);
            double v = ArmStiffness(arm, axis, out _);
            return v;
        }

        /// <summary>Дальний конец отрезка: шарнир стержня или опора без закрепления поворота — шарнир; опора с закреплением поворота — заделка; ничего — свободный.</summary>
        public RcFarEnd FarEnd(Arm arm, double[] axis)
        {
            int far = arm.FarNode;
            var beam = Mesh.Beams[arm.FarBeam];
            int rel = beam.I == far ? beam.ReleaseI : beam.ReleaseJ;
            var (r, _) = Frame(arm.FarBeam);
            double cy = Math.Abs(r[1, 0] * axis[0] + r[1, 1] * axis[1] + r[1, 2] * axis[2]);
            double cz = Math.Abs(r[2, 0] * axis[0] + r[2, 1] * axis[1] + r[2, 2] * axis[2]);
            bool hinge = (cz >= cy ? rel & (1 << 5) : rel & (1 << 4)) != 0;
            if (hinge) return RcFarEnd.Pinned;
            if (RotationFixed(far, axis)) return RcFarEnd.Fixed;
            bool alone = Arms(far).Count(a => a.Beam != arm.FarBeam) == 0 && _shellCount[far] == 0 && RotationalSpring(far, axis) == 0;
            if (!alone) return RcFarEnd.Joint;
            return TranslationSupported(far) ? RcFarEnd.Pinned : RcFarEnd.Free;
        }

        /// <summary>Поворот узла вокруг оси a закреплён опорой (все заметные компоненты оси).</summary>
        public bool RotationFixed(int node, double[] axis)
        {
            bool any = false;
            for (int c = 0; c < 3; c++)
            {
                if (Math.Abs(axis[c]) < 1e-3) continue;
                if (!_result.Build.SupportDofs.Contains(6 * node + 3 + c)) return false;
                any = true;
            }
            return any;
        }

        /// <summary>Перемещение узла вдоль направления d закреплено опорой (все заметные компоненты).</summary>
        public bool AxiallySupported(int node, double[] d)
        {
            bool any = false;
            for (int c = 0; c < 3; c++)
            {
                if (Math.Abs(d[c]) < 0.1) continue;
                if (!_result.Build.SupportDofs.Contains(6 * node + c)) return false;
                any = true;
            }
            return any;
        }

        public bool TranslationSupported(int node)
        {
            for (int c = 0; c < 3; c++) if (_result.Build.SupportDofs.Contains(6 * node + c)) return true;
            return false;
        }

        /// <summary>Жёсткость пружин поворота узла вокруг оси a, Н·м/рад.</summary>
        public double RotationalSpring(int node, double[] axis)
        {
            double k = 0;
            foreach (var (n, dof, stiffness) in _result.Build.NodeSprings)
                if (n == node && dof >= 3) k += stiffness * axis[dof - 3] * axis[dof - 3];
            return k;
        }

        /// <summary>
        /// Доля смещения хорды в работе продольной силы элемента в форме k по плоскости: (Δv)²/l против Σ(Δv_j)²/l_j по
        /// подэлементам; 0 — изгиб между несмещёнными концами, около 0,8 — смещение конца при закреплённых поворотах.
        /// </summary>
        public double ChordShare(int unit, bool xy, int mode)
        {
            var phi = _result.Modes[mode];
            var t = Transverse(unit, xy);
            var nodes = _nodes[unit];
            double V(int node) => phi[6 * node] * t[0] + phi[6 * node + 1] * t[1] + phi[6 * node + 2] * t[2];
            double sum = 0;
            for (int k = 1; k < nodes.Length; k++)
            {
                double dv = V(nodes[k]) - V(nodes[k - 1]);
                sum += dv * dv / (_s[unit][k] - _s[unit][k - 1]);
            }
            double chord = V(nodes[^1]) - V(nodes[0]);
            return sum > 0 ? chord * chord / _s[unit][^1] / sum : 0;
        }

        /// <summary>
        /// Число пролётов k для табл. 31: ригели в плоскости изгиба у узла (ось ригеля ⟂ оси a) и их соосные
        /// продолжения через узлы; один ригель у узла — крайняя колонна, k = 1 (примечание к таблице).
        /// </summary>
        public int Spans(int unit, int node, double[] axis)
        {
            var start = Arms(node).Where(a => a.Unit != unit && InPlane(a, axis) && Math.Abs(Dot(a.Direction, Direction(unit))) < CollinearCos).ToList();
            if (start.Count <= 1) return 1;
            int count = 0;
            foreach (var first in start)
            {
                var arm = first;
                var seen = new HashSet<int>();
                while (arm != null && seen.Add(arm.FarNode))
                {
                    count++;
                    var dir = arm.Direction;
                    int prevUnit = arm.Unit;
                    arm = Arms(arm.FarNode).FirstOrDefault(a => (a.Unit != prevUnit || a.Unit < 0) && a.Beam != arm.FarBeam
                        && Dot(a.Direction, dir) >= CollinearCos);
                }
            }
            return Math.Max(1, count);
        }

        bool InPlane(Arm a, double[] axis) => Math.Abs(Dot(a.Direction, axis)) < 0.1;

        double Distance(int a, int b)
        {
            var p = Mesh.Nodes[a];
            var q = Mesh.Nodes[b];
            return Math.Sqrt((q[0] - p[0]) * (q[0] - p[0]) + (q[1] - p[1]) * (q[1] - p[1]) + (q[2] - p[2]) * (q[2] - p[2]));
        }

        double[] Dir(int a, int b)
        {
            var p = Mesh.Nodes[a];
            var q = Mesh.Nodes[b];
            double l = Distance(a, b);
            return [(q[0] - p[0]) / l, (q[1] - p[1]) / l, (q[2] - p[2]) / l];
        }

        // Узлы цепочки стержней по порядку (стержни подряд имеют общий узел).
        int[] Chain(int[] subs)
        {
            var b0 = Mesh.Beams[subs[0]];
            if (subs.Length == 1) return [b0.I, b0.J];
            var b1 = Mesh.Beams[subs[1]];
            int start = b0.I == b1.I || b0.I == b1.J ? b0.J : b0.I;
            var list = new List<int> { start };
            int cur = start;
            foreach (int s in subs)
            {
                var b = Mesh.Beams[s];
                cur = b.I == cur ? b.J : b.I;
                list.Add(cur);
            }
            return list.ToArray();
        }
    }
}
