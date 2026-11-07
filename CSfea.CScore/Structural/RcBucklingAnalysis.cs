using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>Параметры расчёта устойчивости <see cref="RcBucklingAnalysis"/>.</summary>
/// <param name="Modes">Число искомых форм (наименьших положительных λ).</param>
/// <param name="MinSubdivisions">Наименьшее число КЭ на расчётный элемент (КонЭ или цепочку) после дробления.</param>
public sealed record RcBucklingOptions(int Modes = 20, int MinSubdivisions = 4);

/// <summary>Модель устойчивости: сетка после дробления расчётных элементов (<see cref="RcBucklingAnalysis.Prepare"/>).</summary>
public sealed class RcBucklingModel
{
    /// <summary>Сетка модели после дробления (индексы стержней — <see cref="RcStructuralMeshBuild.BeamIds"/>).</summary>
    public required RcStructuralMeshBuild Build { get; init; }

    /// <summary>Номер стержня исходной модели → индексы стержней сетки его подэлементов по порядку от I к J.</summary>
    public required IReadOnlyDictionary<int, int[]> SubBeams { get; init; }

    /// <summary>
    /// Продольные силы подэлементов стержня <paramref name="beamId"/> исходной модели при перемещениях
    /// <paramref name="u"/>, растяжение — плюс.
    /// </summary>
    public IEnumerable<double> Axial(int beamId, double[] u) =>
        SubBeams[beamId].Select(i => Build.Mesh.BeamAxialForce(i, u));
}

/// <summary>
/// Определяющее сочетание расчётного элемента по п. 10.3.2 СП 16: <paramref name="Combination"/> — индекс сочетания
/// с наибольшим сжатием подэлементов элемента (−1 — сжатия нет ни в одном), <paramref name="N"/> — эта продольная
/// сила (сжатие — минус).
/// </summary>
public sealed record RcBucklingGoverning(int Combination, double N);

/// <summary>Результат расчёта устойчивости по сочетанию.</summary>
public sealed class RcBucklingResult
{
    /// <summary>Модель устойчивости, по которой решена задача.</summary>
    public required RcBucklingModel Model { get; init; }

    /// <summary>Сетка модели после дробления (индексы стержней — <see cref="RcStructuralMeshBuild.BeamIds"/>).</summary>
    public RcStructuralMeshBuild Build => Model.Build;

    /// <summary>Номер стержня исходной модели → индексы стержней сетки его подэлементов по порядку от I к J.</summary>
    public IReadOnlyDictionary<int, int[]> SubBeams => Model.SubBeams;

    /// <summary>Перемещения линейного расчёта по сочетанию (полные DOF сетки) — состояние, по которому собрана K_σ.</summary>
    public required double[] URef { get; init; }

    /// <summary>
    /// Заданные продольные силы стержней сетки (огибающая сжатия по сочетаниям, <see cref="RcBucklingAnalysis.SolveEnvelope"/>);
    /// null — силы по <see cref="URef"/>. Оболочки — всегда по <see cref="URef"/>.
    /// </summary>
    public double[]? BeamAxial { get; init; }

    /// <summary>Продольная сила стержня сетки, по которой собрана K_σ (растяжение — плюс).</summary>
    public double Axial(int beam) => BeamAxial?[beam] ?? Build.Mesh.BeamAxialForce(beam, URef);

    /// <summary>Наименьшие положительные коэффициенты сочетания λ по возрастанию.</summary>
    public required double[] Lambda { get; init; }

    /// <summary>Формы (полные DOF сетки), наибольшая по модулю компонента — 1.</summary>
    public required double[][] Modes { get; init; }

    /// <summary>Итерации подпространства сошлись.</summary>
    public bool Converged { get; init; }

    /// <summary>Замечания расчёта (например, заданные перемещения сочетаний, не вошедшие в нагрузку).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Форма <paramref name="mode"/> (полные DOF сетки дробления) на узлах исходной модели <paramref name="nodeIds"/>
    /// (6 DOF) и промежуточных точках дробления её стержней (поступательные, по порядку от I к J, равномерно по длине);
    /// нормирование — наибольший модуль поступательного перемещения узла или точки = 1 (повороты — тем же множителем).
    /// </summary>
    public RcBucklingModeShape ModeShape(double[] mode, IEnumerable<int> nodeIds)
    {
        var (nodes, bars) = RawShape(mode, nodeIds);
        double max = Scale(nodes, bars);
        if (max > 0)
        {
            foreach (var u in nodes.Values) for (int k = 0; k < 6; k++) u[k] /= max;
            foreach (var p in bars.Values) foreach (var u in p) for (int k = 0; k < 3; k++) u[k] /= max;
        }
        return new RcBucklingModeShape(nodes, bars);
    }

    /// <summary>
    /// Изгибающие моменты формы <paramref name="mode"/> (EI·η″ для EN 1993-1-1 (5.9)) по стержням исходной модели:
    /// номер стержня → (My, Mz) в местных осях в узле I, промежуточных точках дробления и узле J (n + 2 значения, как
    /// <see cref="RcBucklingModeShape.BarPoints"/> + концы); моменты подэлементов продольно-поперечного изгиба
    /// (K_e + λ·K_σ)·d при λ формы <paramref name="lambda"/> и силах <see cref="Axial(int)"/> — EI·w″ концов с погрешностью
    /// O((kh)⁴) (одно K_e·d завышает на (kh)²/12: 1,3 % при 8 КЭ на полуволну, 5 % при 4); в стыках — среднее соседних.
    /// Нормирование — тот же множитель, что у <see cref="ModeShape"/> с теми же <paramref name="nodeIds"/> (Н·м на 1 м
    /// наибольшего поступательного перемещения формы).
    /// </summary>
    public IReadOnlyDictionary<int, double[][]> ModeMoments(double[] mode, double lambda, IEnumerable<int> nodeIds)
    {
        var (nodes, bars) = RawShape(mode, nodeIds);
        double max = Scale(nodes, bars);
        double inv = max > 0 ? 1 / max : 1;
        var result = new Dictionary<int, double[][]>();
        foreach (var (beam, subs) in SubBeams)
        {
            var m = new double[subs.Length + 1][];
            for (int k = 0; k < subs.Length; k++)
            {
                var f = Build.Mesh.BeamLocalEndForces(subs[k], mode, Axial(subs[k]), lambda);
                double[] atI = [-f[4] * inv, -f[5] * inv], atJ = [f[10] * inv, f[11] * inv];
                m[k] = m[k] == null ? atI : [0.5 * (m[k][0] + atI[0]), 0.5 * (m[k][1] + atI[1])];
                m[k + 1] = atJ;
            }
            result[beam] = m;
        }
        return result;
    }

    /// <summary>
    /// Продольные силы стержней исходной модели, по которым собрана K_σ (<see cref="Axial(int)"/>): номер стержня →
    /// наибольшее сжатие по его подэлементам (Н, растяжение — плюс; растянутый во всех — наименьшее растяжение).
    /// </summary>
    public IReadOnlyDictionary<int, double> BarAxial() =>
        SubBeams.ToDictionary(p => p.Key, p => p.Value.Select(Axial).DefaultIfEmpty(0).Min());

    (Dictionary<int, double[]> Nodes, Dictionary<int, double[][]> Bars) RawShape(double[] mode, IEnumerable<int> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(nodeIds);
        var nodes = new Dictionary<int, double[]>();
        foreach (int id in nodeIds)
            if (Build.NodeIndex.TryGetValue(id, out int i)) nodes[id] = mode[(6 * i)..(6 * i + 6)];
        var bars = new Dictionary<int, double[][]>();
        foreach (var (beam, subs) in SubBeams)
            if (subs.Length >= 2)
                bars[beam] = subs.Take(subs.Length - 1).Select(s => mode[(6 * Build.Mesh.Beams[s].J)..(6 * Build.Mesh.Beams[s].J + 3)]).ToArray();
        return (nodes, bars);
    }

    // Наибольший модуль поступательного перемещения узла или точки дробления — множитель нормирования формы.
    static double Scale(Dictionary<int, double[]> nodes, Dictionary<int, double[][]> bars)
    {
        static double Norm(double[] u) => Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
        return nodes.Values.Concat(bars.Values.SelectMany(p => p)).Select(Norm).DefaultIfEmpty(0).Max();
    }
}

/// <summary>
/// Форма потери устойчивости на узлах исходной модели (<see cref="RcBucklingResult.ModeShape"/>): узел → 6 DOF
/// (перемещения и повороты), стержень → поступательные перемещения промежуточных точек дробления от I к J; нормирована
/// на наибольшее поступательное перемещение = 1.
/// </summary>
public sealed record RcBucklingModeShape(IReadOnlyDictionary<int, double[]> Nodes, IReadOnlyDictionary<int, double[][]> BarPoints)
{
    /// <summary>Несовершенство u₀ = Σ aᵢ·φᵢ по формам с амплитудами (м — множитель нормированной формы).</summary>
    public static RcModesImperfection Sum(IEnumerable<(RcBucklingModeShape Shape, double Amplitude)> modes)
    {
        ArgumentNullException.ThrowIfNull(modes);
        var nodes = new Dictionary<int, double[]>();
        var bars = new Dictionary<int, double[][]>();
        foreach (var (shape, a) in modes)
        {
            foreach (var (id, u) in shape.Nodes)
            {
                var acc = nodes.TryGetValue(id, out var x) ? x : nodes[id] = new double[6];
                for (int k = 0; k < 6; k++) acc[k] += a * u[k];
            }
            foreach (var (id, points) in shape.BarPoints)
            {
                if (!bars.TryGetValue(id, out var acc))
                    bars[id] = acc = points.Select(_ => new double[3]).ToArray();
                if (acc.Length != points.Length)
                    throw new ArgumentException($"Формы: у стержня {id} разное число точек дробления ({acc.Length} и {points.Length}).");
                for (int i = 0; i < points.Length; i++)
                    for (int k = 0; k < 3; k++) acc[i][k] += a * points[i][k];
            }
        }
        return new RcModesImperfection(nodes, bars);
    }
}

/// <summary>
/// Линейный расчёт устойчивости модели по недеформированной схеме для расчётных длин (СП 16 п. 10.3.11): стержни
/// расчётных элементов дробятся (шарниры — на крайних подэлементах, пролётные нагрузки — см. <see cref="Subdivide"/>),
/// линейный расчёт по сочетанию, K_σ от его усилий, (K + λ·K_σ)·φ = 0 (<see cref="StructuralMesh.SolveBuckling"/>).
/// </summary>
public static class RcBucklingAnalysis
{
    /// <param name="model">Модель с упругими сечениями стержней.</param>
    /// <param name="combination">Сочетание: загружения с коэффициентами.</param>
    /// <param name="units">
    /// Расчётные элементы — цепочки номеров стержней модели (КонЭ или цепочка КЭ между раскреплениями); каждый
    /// дробится не менее чем на <see cref="RcBucklingOptions.MinSubdivisions"/> КЭ. Стержни вне элементов не дробятся.
    /// </param>
    public static RcBucklingResult Run(RcStructuralModel model, IReadOnlyList<(int LoadCase, double Factor)> combination,
        IEnumerable<IReadOnlyList<int>> units, RcBucklingOptions? options = null, IRcSectionFactory? sections = null,
        CancellationToken ct = default)
    {
        options ??= new RcBucklingOptions();
        return Solve(Prepare(model, units, options, sections), combination, options, ct);
    }

    /// <summary>Модель устойчивости: дробление расчётных элементов (<see cref="Run"/>) и сборка сетки.</summary>
    public static RcBucklingModel Prepare(RcStructuralModel model, IEnumerable<IReadOnlyList<int>> units,
        RcBucklingOptions? options = null, IRcSectionFactory? sections = null)
    {
        options ??= new RcBucklingOptions();
        var (sub, map) = Subdivide(model, Divisions(units, options.MinSubdivisions));
        var build = RcStructuralMeshBuilder.Build(sub, sections ?? new LinearRcSectionFactory());
        var beamIndex = new Dictionary<int, int>();
        for (int i = 0; i < build.BeamIds.Length; i++) beamIndex[build.BeamIds[i]] = i;
        return new RcBucklingModel
        {
            Build = build, SubBeams = map.ToDictionary(p => p.Key, p => p.Value.Select(id => beamIndex[id]).ToArray()),
        };
    }

    /// <summary>Линейный расчёт по сочетанию, K_σ от его усилий и задача устойчивости.</summary>
    public static RcBucklingResult Solve(RcBucklingModel model, IReadOnlyList<(int LoadCase, double Factor)> combination,
        RcBucklingOptions? options = null, CancellationToken ct = default)
    {
        options ??= new RcBucklingOptions();
        var build = model.Build;
        var u = build.Mesh.SolveLinear(build.Combination(combination), build.Bc);
        ct.ThrowIfCancellationRequested();
        var r = build.Mesh.SolveBuckling(u, build.Bc, options.Modes);
        return new RcBucklingResult
        {
            Model = model, URef = u, Lambda = r.Lambda, Modes = r.Modes, Converged = r.Converged,
            Warnings = PrescribedWarnings(build, [combination]),
        };
    }

    // Заданные перемещения сочетаний в нагрузку устойчивости не входят: их DOF закреплены с нулевыми значениями.
    static IReadOnlyList<string> PrescribedWarnings(RcStructuralMeshBuild build,
        IReadOnlyList<IReadOnlyList<(int LoadCase, double Factor)>> combinations)
    {
        int n = combinations.Count(c => build.PrescribedCombination(c) != null);
        return n == 0 ? [] :
            [$"Заданные перемещения ({n} сочет.) в нагрузку устойчивости не входят: их DOF закреплены с нулевыми значениями."];
    }

    /// <summary>
    /// Задача устойчивости по огибающей сжатия: у каждого стержня сетки — наибольшее сжатие по сочетаниям (стержень,
    /// растянутый во всех, — с наименьшим растяжением), K_σ стержней собирается по этим силам одновременно. Так
    /// незагруженные в одном сочетании колонны не раскрепляют загруженные в другом (μ по сочетанию с наибольшим N
    /// элемента для остальных сочетаний бывает не в запас). Оболочки — по сочетанию <paramref name="shellCombination"/>
    /// (мембранные усилия не огибаются). λ — коэффициент к огибающей: λ₁ ≤ 1 — схема неустойчива при огибающей.
    /// </summary>
    public static RcBucklingResult SolveEnvelope(RcBucklingModel model,
        IReadOnlyList<IReadOnlyList<(int LoadCase, double Factor)>> combinations, int shellCombination,
        RcBucklingOptions? options = null, CancellationToken ct = default)
    {
        options ??= new RcBucklingOptions();
        var build = model.Build;
        var (u, column, caseIds) = SolveCases(build, combinations, ct);
        var mesh = build.Mesh;
        var axial = new double[mesh.Beams.Count];
        for (int b = 0; b < axial.Length; b++)
        {
            var forces = u.Select(uk => mesh.BeamAxialForce(b, uk)).ToArray();
            double env = double.PositiveInfinity;
            foreach (var c in combinations)
                env = Math.Min(env, c.Sum(t => t.Factor * forces[column[t.LoadCase]]));
            axial[b] = double.IsFinite(env) ? env : 0;
        }
        ct.ThrowIfCancellationRequested();
        var uShell = Sum(build, u, column, combinations[shellCombination]);
        var r = mesh.SolveBuckling(uShell, build.Bc, options.Modes, beamAxial: axial);
        return new RcBucklingResult
        {
            Model = model, URef = uShell, BeamAxial = axial, Lambda = r.Lambda, Modes = r.Modes, Converged = r.Converged,
            Warnings = PrescribedWarnings(build, combinations),
        };
    }

    // Линейные решения загружений сочетаний одной факторизацией: перемещения по загружениям, загружение → столбец.
    static (double[][] U, Dictionary<int, int> Column, int[] CaseIds) SolveCases(RcStructuralMeshBuild build,
        IReadOnlyList<IReadOnlyList<(int LoadCase, double Factor)>> combinations, CancellationToken ct)
    {
        var caseIds = combinations.SelectMany(c => c.Select(t => t.LoadCase)).Distinct().ToArray();
        var loads = caseIds.Select(id => build.Combination([(id, 1.0)])).ToList();
        double[][] u = loads.Count == 0 ? [] : build.Mesh.SolveLinear(loads, build.Bc);
        ct.ThrowIfCancellationRequested();
        var column = new Dictionary<int, int>();
        for (int k = 0; k < caseIds.Length; k++) column[caseIds[k]] = k;
        return (u, column, caseIds);
    }

    // Перемещения сочетания — сумма решений загружений с коэффициентами.
    static double[] Sum(RcStructuralMeshBuild build, double[][] u, Dictionary<int, int> column,
        IReadOnlyList<(int LoadCase, double Factor)> combination)
    {
        var sum = new double[u.Length > 0 ? u[0].Length : build.Mesh.NDof];
        foreach (var (lc, f) in combination)
        {
            var uk = u[column[lc]];
            for (int i = 0; i < sum.Length; i++) sum[i] += f * uk[i];
        }
        return sum;
    }

    /// <summary>
    /// Локальные задачи устойчивости расчётных элементов (спека 2026-10-18, п. 3Д): остальная схема — при фактическом
    /// сочетании задачи <paramref name="result"/>, продольные силы подэлементов элемента умножаются на α
    /// (<see cref="StructuralMesh.SolveLocalBuckling"/>). Только для элементов <paramref name="which"/> (по умолчанию —
    /// всех), у остальных — null; при λ₁ ≤ 1 задача не имеет смысла (схема неустойчива при сочетании).
    /// </summary>
    public static StructuralLocalBuckling?[] SolveLocal(RcBucklingResult result, IReadOnlyList<IReadOnlyList<int>> units,
        IEnumerable<int>? which = null)
    {
        var ids = (which ?? Enumerable.Range(0, units.Count)).Distinct().ToArray();
        var groups = ids.Select(i => units[i].SelectMany(id => result.SubBeams[id]).ToArray()).ToList();
        var solved = result.Build.Mesh.SolveLocalBuckling(result.URef, result.Build.Bc, groups, beamAxial: result.BeamAxial);
        var local = new StructuralLocalBuckling?[units.Count];
        for (int k = 0; k < ids.Length; k++) local[ids[k]] = solved[k];
        return local;
    }

    /// <summary>
    /// Определяющие сочетания расчётных элементов по п. 10.3.2 СП 16 (μ — по сочетанию с наибольшей продольной силой,
    /// для всех сочетаний): загружения решаются одной факторизацией, продольные силы подэлементов сочетаний — суммой по
    /// загружениям; у элемента — сочетание с наименьшей (наиболее сжимающей) силой его подэлементов.
    /// </summary>
    /// <returns>По элементу в порядке <paramref name="units"/>.</returns>
    public static RcBucklingGoverning[] Governing(RcBucklingModel model, IReadOnlyList<IReadOnlyList<int>> units,
        IReadOnlyList<IReadOnlyList<(int LoadCase, double Factor)>> combinations, CancellationToken ct = default)
    {
        var build = model.Build;
        var (u, column, _) = SolveCases(build, combinations, ct);

        var result = new RcBucklingGoverning[units.Count];
        for (int i = 0; i < units.Count; i++)
        {
            // Силы подэлементов по загружениям: [подэлемент][загружение].
            var axial = units[i].SelectMany(id => model.SubBeams[id])
                .Select(b => u.Select(uk => build.Mesh.BeamAxialForce(b, uk)).ToArray()).ToList();
            int best = -1;
            double nMin = 0;
            for (int c = 0; c < combinations.Count; c++)
                foreach (var forces in axial)
                {
                    double n = combinations[c].Sum(t => t.Factor * forces[column[t.LoadCase]]);
                    if (n < nMin) (best, nMin) = (c, n);
                }
            result[i] = new RcBucklingGoverning(best, nMin);
        }
        // Сжатие на уровне шума округления (ригели под узловой нагрузкой: N ≈ −10⁻¹⁰ Н) — не сжатие: иначе μ ~ 10⁶.
        double nMax = result.Select(g => -g.N).DefaultIfEmpty(0).Max();
        for (int i = 0; i < result.Length; i++)
            if (result[i].Combination >= 0 && -result[i].N < NegligibleCompression * nMax)
                result[i] = new RcBucklingGoverning(-1, 0);
        return result;
    }

    // Доля наибольшего по схеме сжатия, ниже которой сжатие элемента считается шумом.
    const double NegligibleCompression = 1e-6;

    /// <summary>
    /// Число частей стержней: стержень элемента из k КЭ — ⌈min/k⌉ частей; стержень нескольких элементов — наибольшее.
    /// </summary>
    public static Dictionary<int, int> Divisions(IEnumerable<IReadOnlyList<int>> units, int minPerUnit)
    {
        var d = new Dictionary<int, int>();
        foreach (var unit in units)
        {
            if (unit.Count == 0) continue;
            int n = Math.Max(1, (minPerUnit + unit.Count - 1) / unit.Count);
            foreach (int id in unit) d[id] = Math.Max(n, d.GetValueOrDefault(id, 1));
        }
        return d;
    }

    /// <summary>
    /// Копия модели с раздроблёнными стержнями: промежуточные узлы на равных долях, сечение и опорный вектор те же,
    /// шарнир I — у первого подэлемента, J — у последнего. Номера новых узлов и стержней — после наибольших исходных.
    /// Погонная нагрузка переносится на каждый подэлемент; концевые силы пролётной нагрузки — на концы крайних
    /// подэлементов (равнодействующая и моменты концов сохраняются, продольная сила внутри стержня становится
    /// постоянной — для K_σ это средняя по длине). Возвращает модель и соответствие: стержень → подэлементы от I к J
    /// (неразрезанный — сам).
    /// </summary>
    public static (RcStructuralModel Model, Dictionary<int, int[]> Map) Subdivide(
        RcStructuralModel model, IReadOnlyDictionary<int, int> divisions)
    {
        var m = new RcStructuralModel();
        m.Nodes.AddRange(model.Nodes);
        m.Shells.AddRange(model.Shells);
        m.RigidBodies.AddRange(model.RigidBodies);
        m.Supports.AddRange(model.Supports);
        m.Springs.AddRange(model.Springs);
        m.Stages.AddRange(model.Stages);
        var coords = model.Nodes.ToDictionary(n => n.Id);
        int nextNode = model.Nodes.Count == 0 ? 1 : model.Nodes.Max(n => n.Id) + 1;
        int nextBeam = model.Beams.Count == 0 ? 1 : model.Beams.Max(b => b.Id) + 1;
        var map = new Dictionary<int, int[]>();
        foreach (var b in model.Beams)
        {
            int n = divisions.GetValueOrDefault(b.Id, 1);
            if (n <= 1)
            {
                m.Beams.Add(b);
                map[b.Id] = [b.Id];
                continue;
            }
            var a = coords[b.NodeI];
            var c = coords[b.NodeJ];
            var ids = new int[n];
            int prev = b.NodeI;
            for (int k = 0; k < n; k++)
            {
                int next;
                if (k == n - 1) next = b.NodeJ;
                else
                {
                    double t = (k + 1.0) / n;
                    next = nextNode++;
                    m.Nodes.Add(new RcNode(next, a.X + t * (c.X - a.X), a.Y + t * (c.Y - a.Y), a.Z + t * (c.Z - a.Z)));
                }
                ids[k] = nextBeam++;
                m.Beams.Add(b with
                {
                    Id = ids[k], NodeI = prev, NodeJ = next,
                    ReleaseI = k == 0 ? b.ReleaseI : 0, ReleaseJ = k == n - 1 ? b.ReleaseJ : 0,
                });
                prev = next;
            }
            map[b.Id] = ids;
        }

        foreach (var lc in model.LoadCases)
        {
            var nlc = new RcLoadCase(lc.Id, lc.Name);
            nlc.Nodal.AddRange(lc.Nodal);
            nlc.Prescribed.AddRange(lc.Prescribed);
            nlc.Shells.AddRange(lc.Shells);
            foreach (var bl in lc.Beams)
                foreach (int id in map.GetValueOrDefault(bl.BeamId, [bl.BeamId]))
                    nlc.Beams.Add(bl with { BeamId = id });
            foreach (var be in lc.BeamEnds)
            {
                var ids = map.GetValueOrDefault(be.BeamId, [be.BeamId]);
                if (ids.Length == 1)
                {
                    nlc.BeamEnds.Add(be with { BeamId = ids[0] });
                    continue;
                }
                var fi = new double[12];
                var fj = new double[12];
                Array.Copy(be.Forces, 0, fi, 0, 6);
                Array.Copy(be.Forces, 6, fj, 6, 6);
                nlc.BeamEnds.Add(new RcBeamEndLoad(ids[0], fi));
                nlc.BeamEnds.Add(new RcBeamEndLoad(ids[^1], fj));
            }
            m.LoadCases.Add(nlc);
        }
        return (m, map);
    }
}
