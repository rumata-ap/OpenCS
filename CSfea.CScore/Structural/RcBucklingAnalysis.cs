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

    /// <summary>Наименьшие положительные коэффициенты сочетания λ по возрастанию.</summary>
    public required double[] Lambda { get; init; }

    /// <summary>Формы (полные DOF сетки), наибольшая по модулю компонента — 1.</summary>
    public required double[][] Modes { get; init; }

    /// <summary>Итерации подпространства сошлись.</summary>
    public bool Converged { get; init; }
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
        return new RcBucklingResult { Model = model, URef = u, Lambda = r.Lambda, Modes = r.Modes, Converged = r.Converged };
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
        var solved = result.Build.Mesh.SolveLocalBuckling(result.URef, result.Build.Bc, groups);
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
        var caseIds = combinations.SelectMany(c => c.Select(t => t.LoadCase)).Distinct().ToArray();
        var loads = caseIds.Select(id => build.Combination([(id, 1.0)])).ToList();
        double[][] u = loads.Count == 0 ? [] : build.Mesh.SolveLinear(loads, build.Bc);
        ct.ThrowIfCancellationRequested();
        var column = new Dictionary<int, int>();
        for (int k = 0; k < caseIds.Length; k++) column[caseIds[k]] = k;

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
