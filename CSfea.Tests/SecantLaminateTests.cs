using CScore;
using CSfea.Core;
using CSfea.CScoreBridge;

namespace CSfea.Tests;

/// <summary>Секущая ABD слоистой пластины: точное воспроизведение усилий CScore.</summary>
[HarnessChecks]
public class SecantLaminateTests
{
    static readonly Diagramm Concrete = RcTestMaterials.ConcreteN();
    static readonly Diagramm Rebar = RcTestMaterials.RebarN();

    static PlateSection Slab(double nu) => new()
    {
        H = 0.2, NLayers = 20, TensionConcrete = true, PlateModel = "layered", PoissonUncracked = nu,
        RebarLayers =
        [
            new PlateRebarLayer { Asx = 5.65e-4, Asy = 3.93e-4, Zsx = -0.07, Zsy = -0.058 },
            new PlateRebarLayer { Asx = 3.93e-4, Asy = 5.65e-4, Zsx = 0.07, Zsy = 0.058 },
        ],
    };

    static readonly (string Name, ShellStrainState S)[] States =
    [
        ("упругое", new ShellStrainState(1e-6, -5e-7, 2e-7, 1e-5, 5e-6, -3e-6)),
        ("трещина снизу", new ShellStrainState(0, 0, 0, -2e-3, 0, 0)),
        ("трещина наискосок", new ShellStrainState(1e-4, -5e-5, 3e-4, -1e-3, 5e-4, 2e-3)),
        ("двухосное растяжение", new ShellStrainState(5e-4, 4e-4, 1e-4, 1e-3, -1e-3, 0)),
        ("текучесть", new ShellStrainState(2e-3, 0, 0, -3e-2, 0, 0)),
    ];

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Секущая ABD: ABD·(ε, κ) = PlateSection.Compute");
        foreach (double nu in new[] { 0.0, 0.2 })
            foreach (bool memory in new[] { false, true })
                foreach (var (name, s) in States)
                    CheckState($"{name}, ν={nu}, {(memory ? "с памятью трещин и ψs" : "без памяти")}", Slab(nu), s, memory);

        TestHarness.Section("Секущая ABD: упругий предел");
        RunElasticLimit();

        TestHarness.Section("SecantShellResponse: единицы CSfea и As");
        RunResponse();
    }

    static void CheckState(string name, PlateSection section, ShellStrainState s, bool memory)
    {
        PlateLayerState? st = null;
        if (memory)
        {
            st = PlateLayerState.For(section);
            var upd = section.UpdateCracks(st, s, Concrete);
            if (upd.PendingPsi.Count > 0)
            {
                var f = section.Compute(s, Concrete, Rebar, computeStiffness: false, layerState: st);
                section.ActivatePsi(st, upd.PendingPsi, [f.Nx, f.Ny, f.Nxy, f.Mx, f.My, f.Mxy], Concrete, Rebar);
            }
        }

        var r = section.Compute(s, Concrete, Rebar, computeStiffness: false, layerState: st);
        var abd = SecantLaminateBuilder.Build(section, s, Concrete, Rebar, layerState: st);
        double[] e = [s.Eps0x, s.Eps0y, s.Gamma0xy], k = [s.Kx, s.Ky, s.Kxy];
        double[] n = Add(Mul(abd.A, e), Mul(abd.B, k)), m = Add(Mul(abd.B, e), Mul(abd.D, k));
        double[] n0 = [r.Nx, r.Ny, r.Nxy], m0 = [r.Mx, r.My, r.Mxy];

        // Общий масштаб: моменты приводятся к силам делением на толщину.
        double h = section.H;
        double scale = Math.Max(MaxAbs(n0), MaxAbs(m0) / h);
        double err = 0.0;
        for (int i = 0; i < 3; i++)
            err = Math.Max(err, Math.Max(Math.Abs(n[i] - n0[i]), Math.Abs(m[i] - m0[i]) / h));
        err /= scale;
        string cracks = st == null ? "" : $", трещин {st.CrackedCount}";
        TestHarness.Check($"{name}: усилия совпадают", err < 1e-9, $"отн.={err:e2}{cracks}");

        var full = Full(abd);
        double asym = 0.0, norm = MaxAbs(full);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
                asym = Math.Max(asym, Math.Abs(full[i, j] - full[j, i]));
        TestHarness.Check($"{name}: симметрична", asym <= 1e-14 * norm, $"асимм.={asym:e2}");
        double minEig = JacobiEigenvalues(full).Min();
        TestHarness.Check($"{name}: λmin ≥ −1e-12·‖ABD‖", minEig >= -1e-12 * norm, $"λmin={minEig:e3}, ‖ABD‖={norm:e3}");
    }

    static void RunElasticLimit()
    {
        // Весь бетон слабо сжат и на первом участке диаграммы: секущие = начальным модулям,
        // ν = 0 → слой изотропен с G = E/2.
        var section = Slab(0.0);
        var s = new ShellStrainState(-1e-7, -1e-7, 0, 1e-7, -5e-8, 2e-8);
        var abd = SecantLaminateBuilder.Build(section, s, Concrete, Rebar);

        double ec = -Concrete.Sig(-1e-7, out _, tenB: false) / 1e-7;
        double es = Rebar.Sig(1e-7, out _) / 1e-7;
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        int nl = section.NLayers;
        double dz = section.H / nl;
        for (int i = 0; i < nl; i++)
        {
            double z = -section.H / 2 + dz / 2 + i * dz;
            double[] diag = [ec, ec, ec / 2];
            for (int j = 0; j < 3; j++)
            {
                a[j, j] += diag[j] * dz; b[j, j] += diag[j] * dz * z; d[j, j] += diag[j] * dz * z * z;
            }
        }
        foreach (var rl in section.RebarLayers)
        {
            a[0, 0] += es * rl.Asx; b[0, 0] += es * rl.Asx * rl.Zsx; d[0, 0] += es * rl.Asx * rl.Zsx * rl.Zsx;
            a[1, 1] += es * rl.Asy; b[1, 1] += es * rl.Asy * rl.Zsy; d[1, 1] += es * rl.Asy * rl.Zsy * rl.Zsy;
        }
        double err = Math.Max(Math.Max(RelDiff(abd.A, a), RelDiff(abd.D, d)), MaxAbsDiff(abd.B, b) / MaxAbs(a) / section.H);
        TestHarness.Check("упругая ABD (ν = 0, начальные модули)", err < 1e-9, $"отн.={err:e2}, Eb={ec:e4} кПа");
    }

    static void RunResponse()
    {
        var section = Slab(0.0);
        var mats = new PlateSectionMaterials { ConcreteDiagram = Concrete, RebarDiagram = Rebar, ConcreteE_MPa = 30000 };
        var s = States[2].S;
        double[] e = [s.Eps0x, s.Eps0y, s.Gamma0xy], k = [s.Kx, s.Ky, s.Kxy], g = [1e-4, -2e-4];

        var secant = new SecantShellResponse(SecantLaminateBuilder.BuildTangent(section, s, mats));
        var direct = new PlateSectionShellResponse(section, mats);
        var fs = secant.Forces(e, k, g);
        var fd = direct.Forces(e, k, g);
        double err = Math.Max(Math.Max(RelV(fs.N, fd.N), RelV(fs.M, fd.M)), RelV(fs.Q, fd.Q));
        TestHarness.Check("Forces = PlateSectionShellResponse (Н, Н·м)", err < 1e-9, $"отн.={err:e2}");
        TestHarness.Check("Tangent = замороженная матрица",
            ReferenceEquals(secant.Tangent(e, k, g).A, secant.Matrix.A));

        var other = SecantLaminateBuilder.BuildTangent(section, States[4].S, mats);
        secant.Update(other);
        TestHarness.Check("Update заменяет матрицу", ReferenceEquals(secant.Matrix.D, other.D));
    }

    static double[,] Full(SecantAbd abd)
    {
        var f = new double[6, 6];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                f[i, j] = abd.A[i, j];
                f[i, j + 3] = abd.B[i, j];
                f[i + 3, j] = abd.B[j, i];
                f[i + 3, j + 3] = abd.D[i, j];
            }
        return f;
    }

    // Собственные числа симметричной матрицы — циклический метод Якоби.
    internal static double[] JacobiEigenvalues(double[,] m0)
    {
        int n = m0.GetLength(0);
        var m = (double[,])m0.Clone();
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0.0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += m[p, q] * m[p, q];
            if (off < 1e-30 * (1.0 + MaxAbs(m))) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    if (m[p, q] == 0.0) continue;
                    double theta = (m[q, q] - m[p, p]) / (2.0 * m[p, q]);
                    double t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                    double c = 1.0 / Math.Sqrt(t * t + 1.0), s = t * c;
                    for (int r = 0; r < n; r++)
                    {
                        double mrp = m[r, p], mrq = m[r, q];
                        m[r, p] = c * mrp - s * mrq;
                        m[r, q] = s * mrp + c * mrq;
                    }
                    for (int r = 0; r < n; r++)
                    {
                        double mpr = m[p, r], mqr = m[q, r];
                        m[p, r] = c * mpr - s * mqr;
                        m[q, r] = s * mpr + c * mqr;
                    }
                }
        }
        return Enumerable.Range(0, n).Select(i => m[i, i]).ToArray();
    }

    static double[] Mul(double[,] a, double[] v) =>
        Enumerable.Range(0, 3).Select(i => a[i, 0] * v[0] + a[i, 1] * v[1] + a[i, 2] * v[2]).ToArray();

    static double[] Add(double[] a, double[] b) => a.Zip(b, (x, y) => x + y).ToArray();

    static double MaxAbs(double[] v) => v.Max(Math.Abs);

    static double MaxAbs(double[,] m)
    {
        double r = 0.0;
        foreach (double v in m) r = Math.Max(r, Math.Abs(v));
        return r;
    }

    static double MaxAbsDiff(double[,] a, double[,] b)
    {
        double r = 0.0;
        for (int i = 0; i < a.GetLength(0); i++)
            for (int j = 0; j < a.GetLength(1); j++)
                r = Math.Max(r, Math.Abs(a[i, j] - b[i, j]));
        return r;
    }

    static double RelDiff(double[,] a, double[,] b) => MaxAbsDiff(a, b) / MaxAbs(b);

    static double RelV(double[] a, double[] b)
    {
        double s = Math.Max(MaxAbs(b), 1e-300), d = 0.0;
        for (int i = 0; i < a.Length; i++) d = Math.Max(d, Math.Abs(a[i] - b[i]));
        return d / s;
    }
}
