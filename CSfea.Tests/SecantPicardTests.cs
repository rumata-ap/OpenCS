using CScore;
using CSfea.Core;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;

namespace CSfea.Tests;

/// <summary>
/// Секущий расчёт (Пикар) — проверочные задачи спеки 6, пп. 1–4: упругость за одну итерацию, ЖБ-балка и полоса
/// пластины против интегрирования кривизн стержневого НДМ, внецентренное сжатие с геометрической нелинейностью,
/// «нет пластилина» и запрет принимать несошедшийся шаг.
/// </summary>
[HarnessChecks]
public class SecantPicardTests
{
    // Сечение 1 м × 0,2 м: B25, A500, низ 10Ø10 (7,85 см²), верх 5Ø8 (2,5 см²), a = 30 мм.
    const double B = 1.0, H = 0.2, A = 0.03, AsBot = 7.85e-4, AsTop = 2.5e-4;
    const double Span = 6.0;

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Пикар: упругое сечение — одна итерация");
        RunElastic();

        TestHarness.Section("Пикар: ЖБ-балка (стержни) против интегрирования кривизн НДМ");
        double q = 8 * 30.0 / (Span * Span);   // M в пролёте 30 кН·м: ≈ 2,5·Mcrc, ниже текучести
        var reference = ReferenceDeflection(q);
        RunBeam(q, reference);

        TestHarness.Section("Пикар: полоса пластины 1 м против того же решения (арматура под 0/30/45°)");
        foreach (double phi in new[] { 0.0, 30.0, 45.0 }) RunStrip(q, phi, reference, PlateCrackRule.Section);
        RunStripLayerRule(q, reference);

        TestHarness.Section("Пикар: внецентренное сжатие, геометрическая нелинейность");
        RunEccentricColumn();

        TestHarness.Section("Пикар: нет «пластилина» — бетон за трещиной не держит растяжение");
        RunTieNoPlasticine();

        TestHarness.Section("Пикар: несошедшийся шаг не принимается");
        RunNotConverged(q);

        TestHarness.Section("Пикар: прогресс и отмена");
        RunProgress(q);
        RunCancel(q);
    }

    // ---------------- 1. упругость ----------------

    /// <summary>Неизменный закон: сошлось на первой итерации, перемещения — линейный расчёт.</summary>
    static void RunElastic()
    {
        var lam = new Laminate([new Ply(new OrthotropicMaterial(30e9, 30e9, 0.2, 12.5e9), 0.0, 0.2)]);
        var tangent = new LinearLaminateResponse(lam).Tangent([0, 0, 0], [0, 0, 0], [0, 0]);
        var model = Plate(4, 4, 3.0, 3.0, 0);
        var states = new ISecantShellState?[model.Shells.Count];
        var shells = new List<StructuralShell>();
        for (int e = 0; e < model.Shells.Count; e++)
        {
            var st = new ConstantShellState(tangent);
            states[e] = st;
            shells.Add(new StructuralShell(model.Shells[e], st.Response));
        }
        var mesh = new StructuralMesh(model.Nodes, shells, null);
        var bc = new BoundaryConditions(mesh);
        foreach (int n in model.Edge) bc.Fix([n], [0, 1, 2]);
        var f = new double[mesh.NDof];
        foreach (int n in Enumerable.Range(0, mesh.NNodes)) f[6 * n + 2] = -1e3;

        var res = new SecantPicardSolver(mesh, bc, states, Array.Empty<ISecantBeamState?>())
            .Run([new SecantLoadStage("q", f)]);
        var linear = new StructuralMesh(model.Nodes,
            model.Shells.Select(s => new StructuralShell(s, new LinearLaminateResponse(lam))).ToList(), null);
        var bcLin = new BoundaryConditions(linear);
        foreach (int n in model.Edge) bcLin.Fix([n], [0, 1, 2]);
        var uLin = linear.SolveLinear(f, bcLin);
        var step = res.Steps.Single();
        TestHarness.Check("сошлось за 1 итерацию", res.Completed && step.Converged && step.Iterations == 1,
            $"итераций {step.Iterations}");
        double err = MaxAbsDiff(step.U, uLin) / uLin.Max(Math.Abs);
        TestHarness.Check("перемещения = линейный расчёт", err < 1e-12, $"отн.={err:e2}");
        TestHarness.Check("истинная невязка ≈ 0", step.TrueResidual < 1e-9, $"{step.TrueResidual:e2}");
    }

    sealed class ConstantShellState(ShellTangent t) : ISecantShellState
    {
        public SecantShellResponse Response { get; } = new(t);
        public ShellTangent Initial => t;
        public SecantShellEvaluation Evaluate(double[] epsM, double[] kappa, double[] gamma) => new(t, default);
        public ShellForces TrueForces(double[] epsM, double[] kappa, double[] gamma) => Response.Forces(epsM, kappa, gamma);
        public void Commit() { }
        public void Revert() { }
    }

    // ---------------- 2. балка и эталон ----------------

    static CrossSection BeamSection() => RcTestMaterials.Rect(B, H, A, AsBot, AsTop, nBars: 5, nx: 4, ny: 40);

    /// <summary>
    /// Прогиб середины свободно опёртой балки по интегрированию кривизн: κ(x) — <see cref="TotalCurvatureSolver"/>
    /// (до трещины — с растяжением бетона, после — без него и с ψs), w = ∫ M̄·κ dx, Симпсон по 120 участкам.
    /// Знак момента — растянута нижняя грань (большая арматура).
    /// </summary>
    static double ReferenceDeflection(double qKn)
    {
        var section = BeamSection();
        var solver = new TotalCurvatureSolver(section, CalcType.N, CalcType.N, CalcType.N);
        double sign = BottomTensionSign(section);
        const int n = 120;
        double h = Span / n, w = 0.0;
        for (int i = 0; i <= n; i++)
        {
            double x = i * h;
            double m = qKn * x * (Span - x) / 2;
            double kappa = 0.0;
            if (m > 1e-9)
            {
                var r = solver.Compute(0, sign * m, 0, sign * m, 0);
                if (!r.AllConverged) throw new InvalidOperationException($"НДМ не сошёлся при M = {m:f3}");
                kappa = r.KFull;
            }
            double mBar = x <= Span / 2 ? x / 2 : (Span - x) / 2;
            double c = i == 0 || i == n ? 1 : i % 2 == 1 ? 4 : 2;
            w += c * mBar * kappa;
        }
        return w * h / 3;
    }

    /// <summary>Знак Mx CScore, при котором растянута нижняя грань (y &lt; 0).</summary>
    static double BottomTensionSign(CrossSection section)
    {
        var k = new Kurvature { e0 = 0, ky = 1e-4, kz = 0 };
        var f = section.Integral(k, CalcType.N, true, true);
        // ε = e0 + ky·Y: при ky > 0 растянута верхняя грань.
        return -Math.Sign(f.Mx);
    }

    /// <summary>
    /// Свободно опёртая балка из 24 стержневых КЭ с сечением CScore (12 КЭ: −1,6 % — у границы трещин средняя
    /// податливость КЭ огрубляет переход; 24 КЭ: +0,25 %).
    /// </summary>
    static void RunBeam(double qKn, double reference)
    {
        const int n = 24;
        var section = BeamSection();
        var rs = new RcBeamSection("ЖБ 1×0,2") { Cross = section, Calc = CalcType.N, TorsionGJ = 1e7 };
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, Span * i / n, 0, 0));
        // Локальная ось y — горизонтальная (глобальная Y), ось z КЭ = высота сечения (Y сечения CScore).
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(i, i, i + 1, rs, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b001111));
        m.Supports.Add(new RcSupport(n, 0b000110));
        var lc = new RcLoadCase(1, "q");
        foreach (var b in m.Beams) lc.Beams.Add(new RcBeamLoad(b.Id, [0, 0, -qKn * 1e3]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("q", [(1, 1.0)], 2));

        var run = RcSecantAnalysis.Run(m, new RcSecantOptions());
        var end = run.Result.StageEnd(0);
        TestHarness.Check("балка: расчёт сошёлся", run.Result.Completed && end != null, run.Result.Message ?? "");
        if (end == null) return;
        double w = -end.U[run.Build.Dof(n / 2, 2)];
        TestHarness.Check("балка: прогиб середины ±2 % к НДМ", Math.Abs(w / reference - 1) <= 0.02,
            $"Пикар {w * 1e3:f3} мм, НДМ {reference * 1e3:f3} мм ({(w / reference - 1) * 100:+0.00;-0.00} %), " +
            $"итераций {run.Result.Steps.Sum(s => s.Iterations)}, невязка {end.TrueResidual:e2}");
        int cracked = run.BeamStates.Count(s => s!.Response != null && ((CrossSectionSecantBeamState)s).Cracked.Any(c => c));
        TestHarness.Check("балка: трещины есть, но не по всей длине", cracked > 0 && cracked < n, $"КЭ с трещинами {cracked}/{n}");
    }

    // ---------------- 3. полоса пластины ----------------

    static PlateSection StripSection() => new()
    {
        H = H, NLayers = 40, TensionConcrete = true, PlateModel = "layered", PoissonUncracked = 0.0,
        RebarLayers =
        [
            new PlateRebarLayer { Asx = AsBot, Asy = AsTop, Zsx = -(H / 2 - A), Zsy = -(H / 2 - A - 0.01) },
            new PlateRebarLayer { Asx = AsTop, Asy = AsTop, Zsx = H / 2 - A, Zsy = H / 2 - A - 0.01 },
        ],
    };

    static PlateSectionMaterials StripMaterials() => new()
    {
        ConcreteDiagram = RcTestMaterials.ConcreteN(), RebarDiagram = RcTestMaterials.RebarN(), ConcreteE_MPa = 30000, Nu = 0.2,
    };

    /// <summary>
    /// Полоса 6 × 1 м, свободно опёртая по коротким кромкам (продольное перемещение — только у одной), 20 × 2 КЭ;
    /// полоса повёрнута в плане на φ, ось x КЭ — поперёк полосы (сечение повёрнуто в оси КЭ), ось сечения — вдоль.
    /// </summary>
    static void RunStrip(double qKn, double phiDeg, double reference, PlateCrackRule rule)
    {
        var m = StripModel(qKn, phiDeg, out int mid);
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            PlateCrackRule = rule,
        });
        var end = run.Result.StageEnd(0);
        string tag = $"φ = {phiDeg:0}°";
        TestHarness.Check($"полоса {tag}: расчёт сошёлся", run.Result.Completed && end != null, run.Result.Message ?? "");
        if (end == null) return;
        double w = -end.U[run.Build.Dof(mid, 2)];
        TestHarness.Check($"полоса {tag}: прогиб середины ±2 % к НДМ", Math.Abs(w / reference - 1) <= 0.02,
            $"Пикар {w * 1e3:f3} мм, НДМ {reference * 1e3:f3} мм ({(w / reference - 1) * 100:+0.00;-0.00} %), " +
            $"итераций {run.Result.Steps.Sum(s => s.Iterations)}, невязка {end.TrueResidual:e2}");
        TestHarness.Check($"полоса {tag}: сечения повёрнуты", run.Build.Mesh.Shells.All(s => s.Section is RotatedShellResponse));
    }

    /// <summary>
    /// Послойное правило трещины: нетреснувшие растянутые слои над трещиной работают по диаграмме, и вместе с ψs работа
    /// бетона между трещинами учитывается дважды — полоса жёстче, чем по стержневому НДМ.
    /// </summary>
    static void RunStripLayerRule(double qKn, double reference)
    {
        var m = StripModel(qKn, 0, out int mid);
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions { PlateCrackRule = PlateCrackRule.Layer });
        var end = run.Result.StageEnd(0);
        TestHarness.Check("полоса, послойное правило: расчёт сошёлся", run.Result.Completed && end != null, run.Result.Message ?? "");
        if (end == null) return;
        double w = -end.U[run.Build.Dof(mid, 2)];
        TestHarness.Check("полоса, послойное правило: жёстче стержневого НДМ", w < reference,
            $"Пикар {w * 1e3:f3} мм, НДМ {reference * 1e3:f3} мм ({(w / reference - 1) * 100:+0.00;-0.00} %)");
    }

    static RcStructuralModel StripModel(double qKn, double phiDeg, out int midNode, int steps = 2)
    {
        const int nx = 20, ny = 2;
        double c = Math.Cos(phiDeg * Math.PI / 180), s = Math.Sin(phiDeg * Math.PI / 180);
        var m = new RcStructuralModel();
        int Id(int i, int j) => i * (ny + 1) + j;
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++)
            {
                double x = Span * i / nx, y = B * j / ny;
                m.Nodes.Add(new RcNode(Id(i, j), c * x - s * y, s * x + c * y, 0));
            }
        var sec = new RcShellSection("полоса") { Plate = StripSection(), PlateMaterials = StripMaterials() };
        int e = 0;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                // Обход против часовой, начиная с (i, j+1): ребро 0 → 1 идёт поперёк полосы.
                m.Shells.Add(new RcShell(e++, [Id(i, j + 1), Id(i, j), Id(i + 1, j), Id(i + 1, j + 1)], sec, [c, s, 0]));
        for (int j = 0; j <= ny; j++)
        {
            m.Supports.Add(new RcSupport(Id(0, j), 0b000111));
            m.Supports.Add(new RcSupport(Id(nx, j), 0b000100));
        }
        var lc = new RcLoadCase(1, "q");
        foreach (var sh in m.Shells) lc.Shells.Add(new RcShellLoad(sh.Id, qKn * 1e3, [0, 0, -1]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("q", [(1, 1.0)], steps));
        midNode = Id(nx / 2, ny / 2);
        return m;
    }

    // ---------------- 4. внецентренное сжатие ----------------

    /// <summary>
    /// Шарнирная стойка, сила P = 0,5·P_cr с эксцентриситетом e на обоих концах (моменты P·e). Упругое сечение в
    /// секущей обёртке (геометрическая нелинейность с замороженной матрицей): совпадает с прямым CR-Ньютоном на той же
    /// сетке и близко к точному w = e·(sec(kL/2) − 1), k = √(P/EI) — CR-стержень сходится к нему по сетке
    /// (8/16/32 КЭ: −1,5/−0,6/−0,35 %).
    /// </summary>
    static void RunEccentricColumn()
    {
        const double l = 4.0, ei = 2e7, ea = 6e9, e = 0.05;
        const int n = 16;
        double pcr = Math.PI * Math.PI * ei / (l * l), p = 0.5 * pcr;
        var s = new[,] { { ea, 0, 0 }, { 0, ei, 0 }, { 0, 0, ei } };
        var nodes = Enumerable.Range(0, n + 1).Select(i => new[] { 0.0, 0.0, l * i / n }).ToArray();
        var states = new ISecantBeamState?[n];
        var beams = new List<StructuralBeam>();
        for (int i = 0; i < n; i++)
        {
            var st = new ConstantBeamState(s);
            states[i] = st;
            beams.Add(new StructuralBeam(i, i + 1, st.Response, [0.0, 1.0, 0.0]));
        }
        var mesh = new StructuralMesh(nodes, null, beams);
        var bc = new BoundaryConditions(mesh);
        bc.Fix([0], [0, 1, 2, 5]);
        bc.Fix([n], [0, 1, 5]);
        var f = new double[mesh.NDof];
        f[6 * n + 2] = -p;
        // Моменты P·e на концах изгибают стойку в плоскости xz (вокруг глобальной y).
        f[6 * 0 + 4] = -p * e;
        f[6 * n + 4] = p * e;
        var res = new SecantPicardSolver(mesh, bc, Array.Empty<ISecantShellState?>(), states,
            new SecantPicardOptions { Geometric = true, GeometricTolerance = 1e-8 }).Run([new SecantLoadStage("P", f, 5)]);
        var end = res.StageEnd(0);
        TestHarness.Check("стойка: расчёт сошёлся", res.Completed && end != null, res.Message ?? "");
        if (end == null) return;
        double w = Math.Abs(end.U[6 * (n / 2)]);

        var direct = new StructuralMesh(nodes, null, Enumerable.Range(0, n).Select(i => new StructuralBeam(i, i + 1,
            new LinearBeamResponse(new BeamSection(1.0, ea, ei, ei, 1e7, 1.0)), [0.0, 1.0, 0.0])).ToList());
        var bcDirect = new BoundaryConditions(direct);
        bcDirect.Fix([0], [0, 1, 2, 5]);
        bcDirect.Fix([n], [0, 1, 5]);
        var (uDirect, _) = direct.SolveNonlinear(f, bcDirect, nSteps: 5, tol: 1e-8, corotational: false);
        TestHarness.CheckRel("стойка: прогиб = прямой CR-Ньютон", w, Math.Abs(uDirect[6 * (n / 2)]), 1e-5);

        double k = Math.Sqrt(p / ei);
        double wExact = e * (1 / Math.Cos(k * l / 2) - 1);
        TestHarness.CheckRel("стойка: прогиб ≈ e·(sec(kL/2) − 1), 16 КЭ", w, wExact, 1e-2);
        double wLinear = p * e * l * l / (8 * ei);
        TestHarness.Check("стойка: прогиб усилен геометрией (≈ 2 раза при P = 0,5·P_cr)", w > 1.9 * wLinear,
            $"w = {w * 1e3:f2} мм, линейно {wLinear * 1e3:f2} мм");
    }

    sealed class ConstantBeamState(double[,] s) : ISecantBeamState
    {
        public SecantBeamResponse Response { get; } = new(s, 1e7);
        public double[,] Initial => s;
        public SecantBeamEvaluation Evaluate(IReadOnlyList<(double Eps0, double KappaY, double KappaZ)> strains,
            double gammaY, double gammaZ) => new(s, default);
        public BeamForces TrueForces(double xi, double eps0, double kappaY, double kappaZ) => Response.Forces(eps0, kappaY, kappaZ);
        public void Commit() { }
        public void Revert() { }
    }

    // ---------------- 5. нет пластилина ----------------

    /// <summary>
    /// Растянутый тяж: полоса пластины и стержень, N = 1,5·N_crc. После сходимости все слои/фибры бетона с трещиной
    /// без напряжений, усилие = только арматура.
    /// </summary>
    static void RunTieNoPlasticine()
    {
        // Полоса-тяж 1 × 1 м из 2 × 2 КЭ: Nx на правой кромке.
        var sec = StripSection();
        var mat = StripMaterials();
        double ncrc = 1.5e3 * H * 1.0;   // Rbt·A, кН → оценка сверху (Rbt,ser 1,55 МПа)
        var m = new RcStructuralModel();
        int Id(int i, int j) => i * 3 + j;
        for (int i = 0; i <= 2; i++)
            for (int j = 0; j <= 2; j++) m.Nodes.Add(new RcNode(Id(i, j), 0.5 * i, 0.5 * j, 0));
        var rs = new RcShellSection("тяж") { Plate = sec, PlateMaterials = mat };
        int e = 0;
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                m.Shells.Add(new RcShell(e++, [Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1)], rs, [1, 0, 0]));
        for (int j = 0; j <= 2; j++) m.Supports.Add(new RcSupport(Id(0, j), 0b111111));
        for (int i = 1; i <= 2; i++)
            for (int j = 0; j <= 2; j++) m.Supports.Add(new RcSupport(Id(i, j), 0b111110));   // только ux свободно
        var lc = new RcLoadCase(1, "N");
        double nTotal = 1.5 * ncrc * 1e3;   // Н на 1 м ширины
        lc.Nodal.Add(new RcNodalLoad(Id(2, 0), [nTotal / 4, 0, 0, 0, 0, 0]));
        lc.Nodal.Add(new RcNodalLoad(Id(2, 1), [nTotal / 2, 0, 0, 0, 0, 0]));
        lc.Nodal.Add(new RcNodalLoad(Id(2, 2), [nTotal / 4, 0, 0, 0, 0, 0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("N", [(1, 1.0)], 1));
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions());
        var end = run.Result.StageEnd(0);
        TestHarness.Check("тяж-пластина: расчёт сошёлся", run.Result.Completed && end != null, run.Result.Message ?? "");
        if (end == null) return;

        double maxSig = 0.0, maxConcreteN = 0.0, nErr = 0.0;
        int crackedLayers = 0, layers = 0;
        for (int k = 0; k < run.Build.Mesh.Shells.Count; k++)
        {
            var st = (PlateSecantShellState)run.ShellStates[k]!;
            var (eps, kappa, _) = ShellElementForces.CenterStrainsGlobal(run.Build.Mesh.ShellCoords(k),
                Gather(end.U, StructuralMesh.NodeDofs(run.Build.Mesh.Shells[k].Nodes)));
            var s = new ShellStrainState(eps[0], eps[1], eps[2], kappa[0], kappa[1], kappa[2]);
            for (int i = 0; i < sec.NLayers; i++)
            {
                var p = sec.EvaluateConcreteLayer(i, s, mat.ConcreteDiagram, null, st.Layers);
                layers++;
                if (p.Cracked) { crackedLayers++; maxSig = Math.Max(maxSig, Math.Max(p.Sig1, p.Sig2)); }
            }
            var r = sec.Compute(s, mat.ConcreteDiagram, mat.RebarDiagram, computeStiffness: false, layerState: st.Layers);
            maxConcreteN = Math.Max(maxConcreteN, Math.Abs(r.NxConcrete));
            nErr = Math.Max(nErr, Math.Abs(r.NxRebar * 1e3 - nTotal) / nTotal);
        }
        TestHarness.Check("тяж-пластина: все слои с трещиной", crackedLayers == layers, $"{crackedLayers}/{layers}");
        TestHarness.Check("тяж-пластина: σ бетона в слоях с трещиной ≤ 0", maxSig <= 0.0, $"max σ = {maxSig:e3} кПа");
        TestHarness.Check("тяж-пластина: Nx бетона = 0", maxConcreteN == 0.0, $"{maxConcreteN:e3} кН/м");
        TestHarness.Check("тяж-пластина: Nx = арматура", nErr < 2e-3, $"отн.={nErr:e2}");

        // Стержень-тяж: 2 КЭ, сила на конце.
        var section = BeamSection();
        var bs = new RcBeamSection("тяж") { Cross = section, Calc = CalcType.N, TorsionGJ = 1e7 };
        var mb = new RcStructuralModel();
        for (int i = 0; i <= 2; i++) mb.Nodes.Add(new RcNode(i, 0.5 * i, 0, 0));
        for (int i = 0; i < 2; i++) mb.Beams.Add(new RcBeam(i, i, i + 1, bs, [0.0, 1.0, 0.0]));
        mb.Supports.Add(new RcSupport(0, 0b111111));
        mb.Supports.Add(new RcSupport(1, 0b111110));
        mb.Supports.Add(new RcSupport(2, 0b111110));
        var lb = new RcLoadCase(1, "N");
        lb.Nodal.Add(new RcNodalLoad(2, [nTotal, 0, 0, 0, 0, 0]));
        mb.LoadCases.Add(lb);
        mb.Stages.Add(new RcStage("N", [(1, 1.0)], 1));
        var rb = RcSecantAnalysis.Run(mb, new RcSecantOptions());
        var eb = rb.Result.StageEnd(0);
        TestHarness.Check("тяж-стержень: расчёт сошёлся", rb.Result.Completed && eb != null, rb.Result.Message ?? "");
        if (eb == null) return;
        var bst = (CrossSectionSecantBeamState)rb.BeamStates[1]!;
        TestHarness.Check("тяж-стержень: трещина во всех точках", bst.Cracked.All(c => c));
        double e0 = (eb.U[6 * 2] - eb.U[6 * 1]) / 0.5;
        var force = bst.TrueForces(0.5, e0, 0, 0);
        double maxConcrete = section.Areas.Where(a => a.Material?.Type == MatType.Concrete)
            .SelectMany(a => a.Fibers).Max(f => f.Sig);
        TestHarness.Check("тяж-стержень: σ фибр бетона ≤ 0", maxConcrete <= 0.0, $"max σ = {maxConcrete:e3} кПа");
        TestHarness.CheckRel("тяж-стержень: N = нагрузка (только арматура)", force.N, nTotal, 2e-3);
    }

    // ---------------- 6. несошедшийся шаг ----------------

    /// <summary>Полоса за трещинообразованием при MaxIterations = 2 и без дробления: шаг помечен, стадия — предельная.</summary>
    static void RunNotConverged(double qKn)
    {
        var m = StripModel(qKn, 0, out _, steps: 1);
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            Solver = new SecantPicardOptions { MaxIterations = 2, MaxBisections = 0 },
        });
        var r = run.Result;
        TestHarness.Check("kmax = 2: расчёт не завершён", !r.Completed && r.LimitStage == 0, r.Message ?? "");
        TestHarness.Check("kmax = 2: шаг помечен несошедшимся",
            r.Steps.Count == 1 && !r.Steps[0].Converged && r.StageEnd(0) == null);
    }

    // ---------------- 6. прогресс и отмена ----------------

    /// <summary>Отчёт на каждую итерацию и каждый принятый шаг; доля монотонна и доходит до 1.</summary>
    static void RunProgress(double qKn)
    {
        var m = StripModel(qKn, 0, out _, steps: 2);
        var reports = new List<SecantProgress>();
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions(), new SyncProgress<SecantProgress>(reports.Add));
        var r = run.Result;
        int iterations = reports.Count(p => !p.StepDone), steps = reports.Count(p => p.StepDone);
        TestHarness.Check("прогресс: отчёт на каждую итерацию", r.Completed && iterations == r.Iterations.Count,
            $"отчётов {iterations}, итераций {r.Iterations.Count}");
        TestHarness.Check("прогресс: отчёт на каждый принятый шаг", steps == r.Steps.Count(s => s.Converged),
            $"отчётов {steps}, шагов {r.Steps.Count}");
        bool monotone = reports.Zip(reports.Skip(1), (a, b) => b.Fraction >= a.Fraction - 1e-12).All(x => x);
        var last = reports[^1];
        TestHarness.Check("прогресс: доля монотонна и доходит до 1",
            monotone && last.StepDone && Math.Abs(last.Fraction - 1) < 1e-12 && Math.Abs(last.LoadFactor - 1) < 1e-12,
            $"последняя доля {last.Fraction:0.###}, λ {last.LoadFactor:0.###}");
    }

    /// <summary>Отмена на второй итерации первого шага: исключение, третьей итерации нет, шаг не принят.</summary>
    static void RunCancel(double qKn)
    {
        var m = StripModel(qKn, 0, out _, steps: 2);
        using var cts = new CancellationTokenSource();
        var reports = new List<SecantProgress>();
        bool cancelled = false;
        try
        {
            RcSecantAnalysis.Run(m, new RcSecantOptions(), new SyncProgress<SecantProgress>(p =>
            {
                reports.Add(p);
                if (p.Iteration == 2) cts.Cancel();
            }), cts.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        TestHarness.Check("отмена: OperationCanceledException", cancelled);
        TestHarness.Check("отмена: после неё итераций нет, шаг не принят",
            reports.Count == 2 && reports.All(p => !p.StepDone && p.Step == 1),
            $"отчётов {reports.Count}");
    }

    /// <summary>Синхронный приёмник прогресса (<see cref="Progress{T}"/> шлёт отчёты асинхронно).</summary>
    sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    // ---------------- утилиты ----------------

    sealed record PlateModel(double[][] Nodes, List<int[]> Shells, List<int> Edge);

    static PlateModel Plate(int nx, int ny, double lx, double ly, double z)
    {
        var nodes = new List<double[]>();
        int Id(int i, int j) => i * (ny + 1) + j;
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++) nodes.Add([lx * i / nx, ly * j / ny, z]);
        var shells = new List<int[]>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++) shells.Add([Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1)]);
        var edge = new List<int>();
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++)
                if (i == 0 || j == 0 || i == nx || j == ny) edge.Add(Id(i, j));
        return new PlateModel(nodes.ToArray(), shells, edge);
    }

    static double MaxAbsDiff(double[] a, double[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Max();

    static double[] Gather(double[] u, int[] dofs) => dofs.Select(d => u[d]).ToArray();
}
