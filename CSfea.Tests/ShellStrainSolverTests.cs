using CScore;

namespace CSfea.Tests;

/// <summary>Тесты обратной задачи пластины: солвер, клон, выборка по толщине.</summary>
public static class ShellStrainSolverTests
{
    // Линейная диаграмма σ=E·ε (как в PlateModelTests).
    static Diagramm LinearConcrete(double e_MPa)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = e_MPa, Ry = 600, Ru = 600, Ft = 600, Fc = -600,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = e_MPa, Type = MatType.ReSteelF, Tag = "lin" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.C];
    }

    static PlateSection Plate(string model, double h, int nLayers) => new()
    {
        H = h, NLayers = nLayers, TensionConcrete = true,
        SofteningModel = "", PlateModel = model,
    };

    public static void RunAll()
    {
        TestHarness.Section("Пластина: глубокий клон CloneForCalc");
        RunClone();

        TestHarness.Section("Пластина: обратная задача — round-trip и аналитика");
        RunSolveRoundTrip();
        RunSolveLinearAnalytic();
        RunForwardVsCentral();
        RunSolveMany();

        TestHarness.Section("Пластина: выборка эпюр по толщине");
        RunSample();
    }

    static void RunSample()
    {
        double e = 30_000, h = 0.2;
        var cd = LinearConcrete(e);
        var plate = Plate("layered", h, 40);
        var st = new ShellStrainState(0, 0, 0, 1e-3, 0, 0); // чистый изгиб κx
        var s = plate.SampleThroughThickness(st, cd, cd, null, 11);
        TestHarness.Check("выборка: 11 точек", s.Z.Length == 11 && s.SigX.Length == 11);
        TestHarness.CheckRel("выборка: z[0]=-h/2", s.Z[0], -h / 2.0, 1e-9);
        TestHarness.CheckRel("выборка: z[10]=+h/2", s.Z[10], h / 2.0, 1e-9);
        // При κx>0: εx(z)=κx·z → σx меняет знак; на нижней грани сжатие, на верхней растяжение
        TestHarness.Check("выборка: σx антисимметрична по знаку",
            s.SigX[0] * s.SigX[10] < 0, $"низ={s.SigX[0]:f3}, верх={s.SigX[10]:f3}");
        TestHarness.CheckRel("выборка: σx(z=0)≈0", s.SigX[5], 0.0, 1e-6);
    }

    static void RunSolveMany()
    {
        double e = 30_000, h = 0.2;
        var cd = LinearConcrete(e);
        var plate = Plate("layered", h, 40);
        var targets = new List<double[]>
        {
            new double[] { 50, -20, 0, 8, -4, 0 },
            new double[] { 55, -22, 0, 9, -4.5, 0 },
            new double[] { 60, -24, 0, 10, -5, 0 },
        };
        var results = new ShellStrainSolver(plate, cd, cd, null).SolveMany(targets);
        TestHarness.Check("SolveMany: 3 результата", results.Count == 3);
        TestHarness.Check("SolveMany: все сошлись",
            results[0].Converged && results[1].Converged && results[2].Converged);
    }

    static void RunSolveRoundTrip()
    {
        double e = 30_000, h = 0.2;
        var cd = LinearConcrete(e);
        var plate = Plate("layered", h, 40);

        // Эталонное НДС → усилия (прямая задача)
        var state0 = new ShellStrainState(1.2e-4, -0.6e-4, 0.0, 1.5e-3, -0.8e-3, 0.0);
        var f0 = plate.Compute(state0, cd, cd, null, false);
        double[] target = { f0.Nx, f0.Ny, f0.Nxy, f0.Mx, f0.My, f0.Mxy };

        var solver = new ShellStrainSolver(plate, cd, cd, null, tolRes: 1e-4, maxIter: 50);
        var res = solver.Solve(target);

        TestHarness.Check("round-trip: сошлось", res.Converged, $"iter={res.Iterations}, r={res.Residual:e3}");
        TestHarness.CheckRel("round-trip: ε₀x", res.StrainState.Eps0x, state0.Eps0x, 1e-3);
        TestHarness.CheckRel("round-trip: κx", res.StrainState.Kx, state0.Kx, 1e-3);
        TestHarness.CheckRel("round-trip: κy", res.StrainState.Ky, state0.Ky, 1e-3);
    }

    static void RunSolveLinearAnalytic()
    {
        double e = 30_000, h = 0.2;
        var cd = LinearConcrete(e);
        var plate = Plate("char1d_axial", h, 1);
        // Чистая мембрана по x: Nx = E·ε·h·1000 → ε = Nx/(E·h·1000)
        double nx = 100.0; // кН/м
        double[] target = { nx, 0, 0, 0, 0, 0 };
        var res = new ShellStrainSolver(plate, cd, cd, null, tolRes: 1e-6, maxIter: 60).Solve(target);
        double epsRef = nx / (e * h * 1000.0);
        TestHarness.Check("аналитика: сошлось", res.Converged, $"r={res.Residual:e3}");
        TestHarness.CheckRel("аналитика: ε₀x = Nx/(E·h·1000)", res.StrainState.Eps0x, epsRef, 1e-3);
    }

    static void RunForwardVsCentral()
    {
        double e = 30_000, h = 0.2;
        var cd = LinearConcrete(e);
        var plate = Plate("layered", h, 40);
        double[] target = { 80.0, -30.0, 0.0, 12.0, -6.0, 0.0 };
        var fwd = new ShellStrainSolver(plate, cd, cd, null, centralJacobian: false).Solve(target);
        var ctr = new ShellStrainSolver(plate, cd, cd, null, centralJacobian: true).Solve(target);
        TestHarness.Check("forward сошёлся", fwd.Converged);
        TestHarness.Check("central сошёлся", ctr.Converged);
        TestHarness.CheckRel("forward==central: ε₀x", fwd.StrainState.Eps0x, ctr.StrainState.Eps0x, 1e-4);
        TestHarness.CheckRel("forward==central: κx", fwd.StrainState.Kx, ctr.StrainState.Kx, 1e-4);
    }

    static void RunClone()
    {
        var p = Plate("layered", 0.25, 12);
        p.Id = 7; p.Num = 3; p.Tag = "orig";
        p.ConcreteMaterialId = 11; p.RebarMaterialId = 22;
        p.SofteningModel = "vecchio_collins"; p.SofteningEpsC2 = 0.0021;
        p.RebarLayers.Add(new PlateRebarLayer { Name = "низ", Asx = 5e-4, Asy = 4e-4,
            Zsx = -0.1, Zsy = -0.09, InputMode = "direct", MaterialId = 33 });

        var c = p.CloneForCalc();
        // Идентичность значений
        TestHarness.Check("клон: H совпадает", c.H == p.H, $"{c.H}");
        TestHarness.Check("клон: PlateModel совпадает", c.PlateModel == p.PlateModel);
        TestHarness.Check("клон: слой скопирован", c.RebarLayers.Count == 1 && c.RebarLayers[0].Asx == 5e-4);
        // Независимость
        c.H = 0.99; c.RebarLayers[0].Asx = 1e-9; c.RebarLayers.Add(new PlateRebarLayer());
        TestHarness.Check("клон: H независим", p.H == 0.25, $"orig={p.H}");
        TestHarness.Check("клон: слой независим", p.RebarLayers[0].Asx == 5e-4 && p.RebarLayers.Count == 1);
    }
}
