using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>
/// Численная касательная CR-оболочек <see cref="ShellCorotational.ElementNumericalTangentCR"/> в
/// <see cref="StructuralMesh"/>: K_T = ∂F_int/∂u и квадратичный Ньютон стены, сжатой в своей плоскости (P-Δ).
/// </summary>
[HarnessChecks]
public class CrShellTangentTests
{
    private const double E = 30e9, Nu = 0.2, T = 0.2, B = 1.0, H = 6.0;
    private const int Nx = 4, Nz = 24;

    [Fact]
    public static void RunAll()
    {
        RunTangentMatchesForces();
        RunWallNewton();
        RunWallSecantPicard();
    }

    /// <summary>K_T·δ сетки = центральная разность F_int по направлению δ при больших поворотах и сжатии.</summary>
    private static void RunTangentMatchesForces()
    {
        TestHarness.Section("CR-оболочки: K_T = ∂F_int/∂u (сборка StructuralMesh)");
        var (mesh, _) = Wall();
        var rnd = new Random(11);
        var u = new double[mesh.NDof];
        for (int n = 0; n < mesh.NNodes; n++)
        {
            double z = mesh.Nodes[n][2];
            u[6 * n + 0] = 0.3 * (z / H) * (z / H) + 1e-3 * rnd.NextDouble();   // изгиб в плоскости
            u[6 * n + 1] = 0.05 * Math.Sin(Math.PI * z / H) + 1e-3 * rnd.NextDouble();
            u[6 * n + 2] = -2e-3 * z + 1e-4 * rnd.NextDouble();                 // сжатие
            u[6 * n + 3] = 0.05 * (rnd.NextDouble() - 0.5);
            u[6 * n + 4] = 0.6 * z / H + 0.05 * (rnd.NextDouble() - 0.5);       // большой поворот
            u[6 * n + 5] = 0.05 * (rnd.NextDouble() - 0.5);
        }
        var kt = mesh.AssembleKTangent(u).ToCsc();
        double worst = 0.0;
        for (int trial = 0; trial < 3; trial++)
        {
            var d = Enumerable.Range(0, mesh.NDof).Select(_ => rnd.NextDouble() - 0.5).ToArray();
            const double h = 1e-6;
            var fp = mesh.AssembleFInternal(Dense.AddV(u, Dense.ScaleV(d, h)));
            var fm = mesh.AssembleFInternal(Dense.AddV(u, Dense.ScaleV(d, -h)));
            var dfNum = Dense.ScaleV(Dense.SubV(fp, fm), 0.5 / h);
            // Штраф поворота вокруг нормали — и в F_int, и в K_T.
            double err = Dense.Norm(Dense.SubV(kt.Multiply(d), dfNum)) / Dense.Norm(dfNum);
            worst = Math.Max(worst, err);
        }
        TestHarness.Check("‖K_T·δ − ΔF/Δu‖/‖ΔF/Δu‖", worst < 1e-4, $"max={worst:e2}");
    }

    /// <summary>
    /// Стена 1×6 м, t = 0,2, 4×24 КЭ, заделка низа, из плоскости uy = 0; на верху сжатие P и сдвиг 0,01·P в плоскости.
    /// Ньютон за один шаг без line search: прежняя касательная CR (без изменения базиса) — 20/36 итераций при 0,5/0,7·P_cr,
    /// точная ∂F/∂u и LU — 4/5; симметризованная ∂F/∂u и Холецкий (как сейчас в SolveNonlinear) — 6/7. Прогиб верха — 46,0 / 100,5 мм (сетка Q4 чуть жёстче балки).
    /// </summary>
    private static void RunWallNewton()
    {
        TestHarness.Section("CR-оболочки: стена, сжатая в плоскости, — Ньютон с численной касательной");
        double pcr = Math.PI * Math.PI * E * (T * B * B * B / 12.0) / (4.0 * H * H);
        double uxPerP = 0.0;   // линейный прогиб от боковой силы 0,01·P на единицу P
        foreach (double ratio in new[] { 0.0, 0.5, 0.7 })
        {
            var (mesh, bc) = Wall();
            double p = (ratio > 0 ? ratio : 0.5) * pcr;
            var f = TopLoad(mesh, ratio > 0 ? -p : 0.0, 0.01 * p);
            var (u, hist) = mesh.SolveNonlinear(f, bc, nSteps: 1, tol: 1e-8, maxIter: 40, corotational: true, lineSearch: false);
            double ux = TopUx(mesh, u);
            int iters = hist.Count - 1;   // решений Ньютона (последняя запись — проверка сошедшейся невязки)
            Console.WriteLine($"  P = {ratio}·P_cr: ux = {ux * 1e3:f2} мм, итераций {iters}, невязка {hist[^1].Residual:e2}");
            if (ratio == 0.0)
            {
                uxPerP = ux / p;
                continue;
            }
            TestHarness.Check($"P = {ratio}·P_cr: сошлось за ≤ 7 решений", hist.AllConverged() && iters <= 7,
                $"итераций {iters}");
            // Консоль с боковой силой: δ/δ₀ = 3(tg u − u)/u³, u = (π/2)·√(P/P_cr) — с запасом на жёсткость сетки Q4.
            double k = Math.PI / 2 * Math.Sqrt(ratio);
            double amp = 3 * (Math.Tan(k) - k) / (k * k * k);
            TestHarness.Check($"P = {ratio}·P_cr: P-Δ в плоскости учтён", ux / (uxPerP * p) > 0.8 * amp && ux / (uxPerP * p) < 1.05 * amp,
                $"δ/δ₀ = {ux / (uxPerP * p):f3}, аналитика {amp:f3}");
        }
    }

    /// <summary>
    /// Та же стена в секущем расчёте Пикара с геометрической нелинейностью (упругое сечение в секущей обёртке):
    /// прогиб = прямой CR-Ньютон, т. е. P-Δ в плоскости стены учтён (фон Карман давал линейные 24,4 мм).
    /// </summary>
    private static void RunWallSecantPicard()
    {
        TestHarness.Section("CR-оболочки: секущий Пикар с геомнелином — стена, сжатая в плоскости");
        double pcr = Math.PI * Math.PI * E * (T * B * B * B / 12.0) / (4.0 * H * H);
        var abd = new LinearLaminateResponse(PlateBuilder.Plate(E, Nu, T)).Tangent(new double[3], new double[3], new double[2]);
        foreach (double ratio in new[] { 0.5, 0.7 })
        {
            double p = ratio * pcr;
            var (direct, bcDirect) = Wall();
            var f = TopLoad(direct, -p, 0.01 * p);
            var (uDirect, _) = direct.SolveNonlinear(f, bcDirect, nSteps: 1, tol: 1e-10, maxIter: 40, corotational: true,
                lineSearch: false);

            var (wall, _) = Wall();
            var states = new ISecantShellState?[wall.Shells.Count];
            var shells = new List<StructuralShell>();
            for (int e = 0; e < wall.Shells.Count; e++)
            {
                var st = new ConstantShellState(abd);
                states[e] = st;
                shells.Add(new StructuralShell(wall.Shells[e].Nodes, st.Response));
            }
            var mesh = new StructuralMesh(wall.Nodes, shells, null);
            var (_, bc) = WallBc(mesh);
            var res = new SecantPicardSolver(mesh, bc, states, Array.Empty<ISecantBeamState?>(),
                new SecantPicardOptions { Geometric = true, GeometricTolerance = 1e-10 }).Run([new SecantLoadStage("P", f, 2)]);
            var end = res.StageEnd(0);
            TestHarness.Check($"P = {ratio}·P_cr: расчёт сошёлся", res.Completed && end != null, res.Message ?? "");
            if (end == null) continue;
            TestHarness.CheckRel($"P = {ratio}·P_cr: ux = прямой CR-Ньютон", TopUx(mesh, end.U), TopUx(direct, uDirect), 1e-5);
        }
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
    // Узлы сетки: i — по ширине (x), j — по высоте (z); стена в плоскости XZ.
    private static int Node(int i, int j) => j * (Nx + 1) + i;

    private static (StructuralMesh Mesh, BoundaryConditions Bc) Wall()
    {
        var nodes = new double[(Nx + 1) * (Nz + 1)][];
        for (int j = 0; j <= Nz; j++)
            for (int i = 0; i <= Nx; i++)
                nodes[Node(i, j)] = [i * B / Nx, 0.0, j * H / Nz];
        var section = new LinearLaminateResponse(PlateBuilder.Plate(E, Nu, T));
        var shells = new List<StructuralShell>();
        for (int j = 0; j < Nz; j++)
            for (int i = 0; i < Nx; i++)
                shells.Add(new StructuralShell([Node(i, j), Node(i + 1, j), Node(i + 1, j + 1), Node(i, j + 1)], section));
        return WallBc(new StructuralMesh(nodes, shells, null));
    }

    // Заделка низа, из плоскости uy = 0 во всех узлах.
    private static (StructuralMesh Mesh, BoundaryConditions Bc) WallBc(StructuralMesh mesh)
    {
        var fixedDofs = new SortedSet<int>();
        for (int i = 0; i <= Nx; i++)
            for (int c = 0; c < 6; c++) fixedDofs.Add(6 * Node(i, 0) + c);
        for (int n = 0; n < mesh.NNodes; n++) fixedDofs.Add(6 * n + 1);
        return (mesh, BoundaryConditions.FromArrays(mesh, fixedDofs.ToArray()));
    }

    // Нагрузка на верхнюю кромку: сила по z и по x, распределённая по кромке согласованно (крайним узлам — половина).
    private static double[] TopLoad(StructuralMesh mesh, double fz, double fx)
    {
        var f = new double[mesh.NDof];
        for (int i = 0; i <= Nx; i++)
        {
            double w = (i == 0 || i == Nx ? 0.5 : 1.0) / Nx;
            f[6 * Node(i, Nz) + 2] += w * fz;
            f[6 * Node(i, Nz) + 0] += w * fx;
        }
        return f;
    }

    private static double TopUx(StructuralMesh mesh, double[] u)
        => Enumerable.Range(0, Nx + 1).Average(i => u[6 * Node(i, Nz)]);
}
