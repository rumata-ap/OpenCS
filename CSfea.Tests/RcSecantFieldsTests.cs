using CScore;
using CSfea.Core;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;

namespace CSfea.Tests;

/// <summary>
/// Поля шагов секущего расчёта (<see cref="RcSecantFieldExtractor"/>): усилия концов стержней против статики с
/// пролётной нагрузкой по стадиям, мембранные усилия пластины и их поворот в оси выдачи, состояние ЖБ-полосы с
/// трещинами.
/// </summary>
[HarnessChecks]
public class RcSecantFieldsTests
{
    private const double E = 30e9;

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Поля шага: стержни — свободно опёртая балка, две стадии");
        RunBeamStages();

        TestHarness.Section("Поля шага: пластина на растяжение, оси выдачи");
        RunPlateTension();

        TestHarness.Section("Поля шага: ЖБ-полоса с трещинами");
        RunCrackedStrip();
    }

    /// <summary>
    /// Балка 6 м из 4 КЭ, q по всем КЭ; стадии «q» (2 шага) и «+q» (1 шаг): в середине |M| = qL²/8 (в конце второй
    /// стадии — вдвое), у опоры |Q| = qL/2, на λ = 0,5 первой стадии — половина. Силы концов соседних КЭ в среднем узле
    /// уравновешены.
    /// </summary>
    private static void RunBeamStages()
    {
        const double l = 6.0, q = 10e3;
        const int n = 4;
        var rect = new RcBeamSection("rect") { Elastic = new BeamSection(E, 0.15, 0.3 * 0.125 / 12, 0.5 * 0.027 / 12, 2.8e-3) };
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, l * i / n, 0, 0));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(10 + i, i, i + 1, rect, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b001111));
        m.Supports.Add(new RcSupport(n, 0b000110));
        var lc = new RcLoadCase(1, "q");
        foreach (var b in m.Beams) lc.Beams.Add(new RcBeamLoad(b.Id, [0, 0, -q]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("q", [(1, 1.0)], 2));
        m.Stages.Add(new RcStage("+q", [(1, 1.0)], 1));

        var fields = new List<RcSecantStepFields>();
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions { OnStep = (_, get) => fields.Add(get()) });
        TestHarness.Check("балка: расчёт сошёлся, полей 3", run.Result.Completed && fields.Count == 3, $"полей {fields.Count}");
        if (fields.Count != 3) return;

        double m0 = q * l * l / 8, q0 = q * l / 2;
        foreach (var (f, k) in new[] { (fields[0], 0.5), (fields[1], 1.0), (fields[2], 2.0) })
        {
            var left = Beam(f, 1);    // КЭ 11: узлы 1–2, конец j — середина
            var right = Beam(f, 2);   // КЭ 12: узлы 2–3, конец i — середина
            var support = Beam(f, 0);
            string tag = $"стадия {f.Stage + 1}, λ = {f.LoadFactor:0.##}";
            TestHarness.CheckRel($"{tag}: |My| середины = {k:0.#}·qL²/8", Math.Abs(left[10]), k * m0, 1e-6);
            TestHarness.CheckRel($"{tag}: |Qz| у опоры = {k:0.#}·qL/2", Math.Abs(support[2]), k * q0, 1e-6);
            TestHarness.Check($"{tag}: My у шарнирной опоры ≈ 0", Math.Abs(support[4]) < 1e-6 * m0, $"{support[4]:e2}");
            // КЭ соосны — местные оси соседних КЭ совпадают; узловой нагрузки в середине нет.
            double unbalanced = Enumerable.Range(0, 6).Max(c => Math.Abs(left[6 + c] + right[c]));
            TestHarness.Check($"{tag}: силы концов в середине уравновешены", unbalanced < 1e-6 * m0, $"{unbalanced:e2}");
        }
        var last = run.LastConvergedFields();
        TestHarness.Check("балка: поля после расчёта = поля последнего шага",
            last != null && last.BeamForces.SequenceEqual(fields[2].BeamForces) && last.Stage == 1);
        TestHarness.CheckRel("балка: перемещение середины в полях = U шага", fields[2].Displacements[6 * 2 + 2],
            run.Result.Steps[^1].U[run.Build.Dof(2, 2)], 1e-12);
    }

    private static double[] Beam(RcSecantStepFields f, int index) =>
        f.BeamForces.Skip(index * RcSecantStepFields.BeamForceComponents).Take(RcSecantStepFields.BeamForceComponents).ToArray();

    /// <summary>
    /// Упругая пластина 2 × 1 м (2 КЭ), левая кромка закреплена по x, справа — сила F по x: Nx = F/1 м в осях сечения
    /// (ось x сечения — глобальная X). Ось выдачи повёрнута на 90° (угол оси выдачи в осях сечения): Ny выдачи = F, Nx = 0.
    /// </summary>
    private static void RunPlateTension()
    {
        const double f = 100e3;
        var lam = new Laminate([new Ply(new OrthotropicMaterial(E, E, 0.0, E / 2), 0.0, 0.2)]);
        var sec = new RcShellSection("упругая") { Elastic = lam };
        var m = new RcStructuralModel();
        int Id(int i, int j) => i * 2 + j;
        for (int i = 0; i <= 2; i++)
            for (int j = 0; j <= 1; j++)
                m.Nodes.Add(new RcNode(Id(i, j), i, j, 0));
        for (int i = 0; i < 2; i++)
            m.Shells.Add(new RcShell(100 + i, [Id(i, 0), Id(i + 1, 0), Id(i + 1, 1), Id(i, 1)], sec, [1.0, 0, 0]));
        foreach (var node in m.Nodes) m.Supports.Add(new RcSupport(node.Id, 0b111100));
        m.Supports.Add(new RcSupport(Id(0, 0), 0b111111));
        m.Supports.Add(new RcSupport(Id(0, 1), 0b111101));
        var lc = new RcLoadCase(1, "F");
        lc.Nodal.Add(new RcNodalLoad(Id(2, 0), [f / 2, 0, 0, 0, 0, 0]));
        lc.Nodal.Add(new RcNodalLoad(Id(2, 1), [f / 2, 0, 0, 0, 0, 0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("F", [(1, 1.0)]));

        var plain = RcSecantAnalysis.Run(m, new RcSecantOptions()).LastConvergedFields();
        var rotated = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            ShellForceAngles = new Dictionary<int, double> { [100] = 90, [101] = 90 },
        }).LastConvergedFields();
        TestHarness.Check("пластина: поля сняты", plain != null && rotated != null);
        if (plain == null || rotated == null) return;
        for (int e = 0; e < 2; e++)
        {
            int o = e * RcSecantStepFields.ShellForceComponents;
            TestHarness.CheckRel($"КЭ {100 + e}: Nx = F/b", plain.ShellForces[o], f, 1e-6);
            TestHarness.Check($"КЭ {100 + e}: Ny, Nxy, моменты ≈ 0",
                Enumerable.Range(1, 7).All(c => Math.Abs(plain.ShellForces[o + c]) < 1e-6 * f));
            TestHarness.CheckRel($"КЭ {100 + e}: ось выдачи 90° — Ny = F/b", rotated.ShellForces[o + 1], f, 1e-6);
            TestHarness.Check($"КЭ {100 + e}: ось выдачи 90° — Nx ≈ 0", Math.Abs(rotated.ShellForces[o]) < 1e-6 * f);
            TestHarness.Check($"КЭ {100 + e}: упругий КЭ — состояние NaN",
                double.IsNaN(plain.ShellStates[e * RcSecantStepFields.ShellStateComponents]));
        }
    }

    /// <summary>
    /// ЖБ-полоса из <see cref="SecantPicardTests"/> (M в пролёте ≈ 2,5·Mcrc), послойное правило: в середине нижняя
    /// половина треснула целиком, верхняя — меньше чем наполовину, ψs &lt; 1, арматура упругая (0 &lt; σs/σs,т &lt; 1),
    /// бетон далёк от εb2; Mx в осях сечения (вдоль полосы) — статика на 1 м ширины.
    /// </summary>
    private static void RunCrackedStrip()
    {
        const double span = 6.0;
        double q = 8 * 30.0 / (span * span);
        var m = SecantPicardTests.StripModel(q, 0, out _);
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions { PlateCrackRule = PlateCrackRule.Layer });
        var f = run.LastConvergedFields();
        TestHarness.Check("полоса: поля сняты", f != null && run.Result.Completed);
        if (f == null) return;
        int mid = 2 * (20 / 2 - 1);   // КЭ у середины пролёта (20 × 2 КЭ, нумерация по i, затем j)
        int so = mid * RcSecantStepFields.ShellStateComponents;
        double crBottom = f.ShellStates[so], crTop = f.ShellStates[so + 1], psi = f.ShellStates[so + 2],
            sig = f.ShellStates[so + 3], eps = f.ShellStates[so + 4];
        // Нейтральная ось сечения с трещиной выше середины толщины: трещина заходит в верхнюю половину.
        TestHarness.Check("середина: низ треснул целиком, верх — меньше половины", crBottom == 1 && crTop < 0.5,
            $"низ {crBottom:0.##}, верх {crTop:0.##}");
        TestHarness.Check("середина: ψs < 1", psi < 1, $"ψs = {psi:0.###}");
        TestHarness.Check("середина: 0 < σs/σs,т < 1", sig is > 0 and < 1, $"σs/σs,т = {sig:0.###}");
        TestHarness.Check("середина: εb/εb2 < 0,5", eps is > 0 and < 0.5, $"εb/εb2 = {eps:0.###}");
        TestHarness.Check("середина: флаг трещины", (f.ShellFlags[mid] & RcSecantStepFields.Cracked) != 0);
        // Правило «сечение»: первая трещина выключает растяжение всех слоёв — в полях треснувшими считаются обе половины.
        var section = RcSecantAnalysis.Run(SecantPicardTests.StripModel(q, 0, out _),
            new RcSecantOptions { PlateCrackRule = PlateCrackRule.Section }).LastConvergedFields();
        TestHarness.Check("правило «сечение»: в середине треснули все слои",
            section != null && section.ShellStates[so] == 1 && section.ShellStates[so + 1] == 1);
        // Ось сечения — вдоль полосы; момент на 1 м ширины в КЭ у середины (центр КЭ — 0,15 м от середины).
        double x = span / 2 - span / 40;
        double mRef = q * 1e3 * x * (span - x) / 2;
        TestHarness.CheckRel("середина: |Mx| = q·x·(L − x)/2", Math.Abs(f.ShellForces[mid * RcSecantStepFields.ShellForceComponents + 3]),
            mRef, 0.02);
    }
}
