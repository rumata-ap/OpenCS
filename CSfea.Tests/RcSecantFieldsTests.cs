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

        TestHarness.Section("Поля шага: жёсткости и деформации упругой консоли, полные матрицы конечного состояния");
        RunElasticCantileverStiffness();

        TestHarness.Section("Поля шага: секущие жёсткости ЖБ-балки с трещинами");
        RunCrackedBeamStiffness();

        TestHarness.Section("Поля шага: жёсткости и деформации пластины в осях выдачи");
        RunPlateStiffness();
    }

    /// <summary>
    /// Упругая консоль 3 м из 2 КЭ, на конце N и M_y: секущие жёсткости = начальные = EA, EIy, EIz сечения, GJ — задана,
    /// сдвига нет (+∞); деформации ε₀ = N/EA и κy = M/EI во всех точках (эпюра M постоянна); без галки деформаций нет;
    /// полные матрицы — только у конечного состояния (<see cref="RcSecantRun.LastConvergedFields"/>).
    /// </summary>
    private static void RunElasticCantileverStiffness()
    {
        const double l = 3.0, n = 200e3, mom = 50e3, a = 0.15, iy = 0.3 * 0.125 / 12, iz = 0.5 * 0.027 / 12, j = 2.8e-3;
        var rect = new RcBeamSection("rect") { Elastic = new BeamSection(E, a, iy, iz, j) };
        var m = new RcStructuralModel();
        for (int i = 0; i <= 2; i++) m.Nodes.Add(new RcNode(i, l * i / 2, 0, 0));
        for (int i = 0; i < 2; i++) m.Beams.Add(new RcBeam(10 + i, i, i + 1, rect, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b111111));
        var lc = new RcLoadCase(1, "N+M");
        lc.Nodal.Add(new RcNodalLoad(2, [n, 0, 0, 0, mom, 0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("N+M", [(1, 1.0)]));

        var steps = new List<RcSecantStepFields>();
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions { RecordSectionStrains = true, OnStep = (_, get) => steps.Add(get()) });
        var last = run.LastConvergedFields();
        TestHarness.Check("консоль: поля сняты", last != null && steps.Count == 1);
        if (last == null || steps.Count != 1) return;
        var f = steps[0];
        TestHarness.Check("консоль: жёсткости есть, полных матриц у шага нет, у конечного состояния есть",
            f.HasStiffness && !f.HasMatrices && f.BeamMatrices.Length == 0 && last.HasMatrices
            && last.BeamMatrices.Length == 2 * RcSecantStepFields.BeamMatrixComponents);
        double gj = new BeamSection(E, a, iy, iz, j).G * j;
        for (int e = 0; e < 2; e++)
        {
            int o = e * RcSecantStepFields.BeamStiffnessComponents;
            var k = f.BeamStiffness;
            TestHarness.CheckRel($"КЭ {10 + e}: EA₀ = E·A", k[o + 15], E * a, 1e-9);
            TestHarness.CheckRel($"КЭ {10 + e}: EIy₀ = E·Iy", k[o + 16], E * iy, 1e-9);
            TestHarness.CheckRel($"КЭ {10 + e}: EIz₀ = E·Iz", k[o + 17], E * iz, 1e-9);
            TestHarness.CheckRel($"КЭ {10 + e}: GJ", k[o + 20], gj, 1e-9);
            TestHarness.Check($"КЭ {10 + e}: секущие в трёх точках = начальные, сдвига нет",
                Enumerable.Range(0, 3).All(p => k[o + 5 * p] == k[o + 15] && k[o + 5 * p + 1] == k[o + 16]
                    && k[o + 5 * p + 2] == k[o + 17] && double.IsPositiveInfinity(k[o + 5 * p + 3])));
            for (int p = 0; p < 3; p++)
            {
                int so = e * RcSecantStepFields.BeamStrainComponents + 3 * p;
                TestHarness.CheckRel($"КЭ {10 + e}, ξ = {0.5 * p}: ε₀ = N/EA", f.BeamStrains[so], n / (E * a), 1e-6);
                TestHarness.CheckRel($"КЭ {10 + e}, ξ = {0.5 * p}: |κy| = M/EIy", Math.Abs(f.BeamStrains[so + 1]), mom / (E * iy), 1e-6);
            }
            int mo = e * RcSecantStepFields.BeamMatrixComponents;
            TestHarness.CheckRel($"КЭ {10 + e}: матрица ξ = ½ — EIy на диагонали", last.BeamMatrices[mo + 11 + 4], E * iy, 1e-9);
        }
        var noStrains = RcSecantAnalysis.Run(m, new RcSecantOptions()).LastConvergedFields();
        TestHarness.Check("консоль: без галки деформаций нет, жёсткости есть",
            noStrains is { HasStrains: false, HasStiffness: true } && noStrains.BeamStrains.Length == 0);
    }

    /// <summary>
    /// Балка из <see cref="SecantPicardTests"/> (M в пролёте ≈ 2,5·Mcrc, 24 КЭ): у середины точка с трещиной —
    /// EIy/EIy₀ &lt; 0,6, у опоры трещин нет — EIy/EIy₀ &gt; 0,9; секущая середины — диагональ полной матрицы точки.
    /// </summary>
    private static void RunCrackedBeamStiffness()
    {
        const int n = 24;
        const double span = 6.0;
        double q = 8 * 30.0 / (span * span);
        var rs = new RcBeamSection("ЖБ 1×0,2")
        {
            Cross = RcTestMaterials.Rect(1.0, 0.2, 0.03, 7.85e-4, 2.5e-4, nBars: 5, nx: 4, ny: 40), Calc = CalcType.N, TorsionGJ = 1e7,
        };
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, span * i / n, 0, 0));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(i, i, i + 1, rs, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b001111));
        m.Supports.Add(new RcSupport(n, 0b000110));
        var lc = new RcLoadCase(1, "q");
        foreach (var b in m.Beams) lc.Beams.Add(new RcBeamLoad(b.Id, [0, 0, -q * 1e3]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("q", [(1, 1.0)], 2));

        var f = RcSecantAnalysis.Run(m, new RcSecantOptions { RecordSectionStrains = true }).LastConvergedFields();
        TestHarness.Check("ЖБ-балка: поля сняты", f is { HasStiffness: true, HasMatrices: true, HasStrains: true });
        if (f == null) return;
        int mid = n / 2, o = mid * RcSecantStepFields.BeamStiffnessComponents;   // КЭ 12, ξ = 0 — середина пролёта
        double ratioMid = f.BeamStiffness[o + 1] / f.BeamStiffness[o + 16];
        TestHarness.Check("середина: EIy/EIy₀ < 0,6", ratioMid is > 0 and < 0.6, $"EIy/EIy₀ = {ratioMid:0.###}");
        double ratioSupport = f.BeamStiffness[1] / f.BeamStiffness[16];   // КЭ 0, ξ = 0 — опора
        // У опоры трещин нет: секущая ниже начальной только на кривизну диаграммы бетона.
        TestHarness.Check("опора: EIy/EIy₀ > 0,9 (трещин нет)", ratioSupport is > 0.9 and <= 1.0 + 1e-9,
            $"EIy/EIy₀ = {ratioSupport:0.###}");
        TestHarness.Check("середина: GAy, GAz конечны (КЭ Тимошенко)",
            double.IsFinite(f.BeamStiffness[o + 3]) && double.IsFinite(f.BeamStiffness[o + 4]));
        int mo = mid * RcSecantStepFields.BeamMatrixComponents;
        TestHarness.CheckRel("середина: EIy — диагональ полной матрицы точки", f.BeamMatrices[mo + 4], f.BeamStiffness[o + 1], 1e-6);
        TestHarness.Check("середина: κy ≠ 0", Math.Abs(f.BeamStrains[mid * RcSecantStepFields.BeamStrainComponents + 1]) > 1e-4);
    }

    /// <summary>
    /// Пластина на растяжение (как <see cref="RunPlateTension"/>), ортотропный ламинат E1 = E, E2 = E/2, ν = 0:
    /// A11 = E·h, A22 = E·h/2, D11 = E·h³/12, εx = F/(E·h); при оси выдачи под 90° члены и деформации меняются местами.
    /// Слоистая ЖБ-полоса с трещинами — D11/D11₀ &lt; 1 у середины, полная матрица — в осях сечения.
    /// </summary>
    private static void RunPlateStiffness()
    {
        const double f = 100e3, h = 0.2;
        var lam = new Laminate([new Ply(new OrthotropicMaterial(E, E / 2, 0.0, E / 4), 0.0, h)]);
        var sec = new RcShellSection("орто") { Elastic = lam };
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

        var plain = RcSecantAnalysis.Run(m, new RcSecantOptions { RecordSectionStrains = true }).LastConvergedFields();
        var rotated = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            RecordSectionStrains = true, ShellForceAngles = new Dictionary<int, double> { [100] = 90, [101] = 90 },
        }).LastConvergedFields();
        TestHarness.Check("пластина: поля сняты", plain != null && rotated != null);
        if (plain == null || rotated == null) return;
        var k = plain.ShellStiffness;
        TestHarness.CheckRel("A11 = E·h", k[0], E * h, 1e-9);
        TestHarness.CheckRel("A22 = E·h/2", k[1], E * h / 2, 1e-9);
        TestHarness.CheckRel("D11 = E·h³/12", k[4], E * h * h * h / 12, 1e-9);
        TestHarness.Check("упругий КЭ: секущие = начальные", Enumerable.Range(0, 8).All(i => k[i] == k[8 + i]));
        TestHarness.CheckRel("εx = F/(E·h)", plain.ShellStrains[0], f / (E * h), 1e-6);
        TestHarness.CheckRel("ось выдачи 90°: A11 = A22 сечения", rotated.ShellStiffness[0], E * h / 2, 1e-9);
        TestHarness.CheckRel("ось выдачи 90°: D22 = D11 сечения", rotated.ShellStiffness[5], E * h * h * h / 12, 1e-9);
        TestHarness.CheckRel("ось выдачи 90°: εy = F/(E·h)", rotated.ShellStrains[1], f / (E * h), 1e-6);
        TestHarness.CheckRel("ось выдачи 90°: полная матрица — в осях сечения (A11 = E·h)", rotated.ShellMatrices[0], E * h, 1e-6);

        const double span = 6.0;
        var strip = RcSecantAnalysis.Run(SecantPicardTests.StripModel(8 * 30.0 / (span * span), 0, out _),
            new RcSecantOptions()).LastConvergedFields();
        TestHarness.Check("полоса: поля сняты", strip is { HasStiffness: true, HasMatrices: true });
        if (strip == null) return;
        int mid = 2 * (20 / 2 - 1), o = mid * RcSecantStepFields.ShellStiffnessComponents;
        double ratio = strip.ShellStiffness[o + 4] / strip.ShellStiffness[o + 12];
        TestHarness.Check("полоса, середина: D11/D11₀ < 0,6", ratio is > 0 and < 0.6, $"D11/D11₀ = {ratio:0.###}");
        TestHarness.CheckRel("полоса, середина: D11 — член полной матрицы (D[0,0])",
            strip.ShellMatrices[mid * RcSecantStepFields.ShellMatrixComponents + 18], strip.ShellStiffness[o + 4], 1e-6);
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
