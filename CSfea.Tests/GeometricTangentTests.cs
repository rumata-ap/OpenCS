using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>
/// Касательные матрицы геометрической нелинейности при секущих сечениях: K_T = ∂F_int/∂u с геометрической частью
/// (стержень CR, оболочка фон Кармана) и квадратичный Ньютон сжатой стойки.
/// </summary>
[HarnessChecks]
public class GeometricTangentTests
{
    const double L = 3.0, Ea = 6e9, EIy = 1.2e8, EIz = 4e7, Gj = 2.5e7;

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Касательная CR-стержня с секущим сечением = ∂F_int/∂u");
        // Связанная S (внецентренное сечение, как после трещин) и сдвиг Тимошенко; продольная сила — около 2·EIz/L².
        const double e = 0.12;
        var s = new[,] { { Ea, Ea * e, 0.0 }, { Ea * e, EIy + Ea * e * e, 0.0 }, { 0.0, 0.0, EIz } };
        var sec = new SecantBeamResponse(s, Gj, new BeamShearStiffness(3e8, 5e8));
        double[][] coords = [[0.0, 0.0, 0.0], [L, 0.0, 0.0]];
        double[] refVec = [0.0, 0.0, 1.0];
        double p = 2.0 * EIz / (L * L);
        double[] u = [0, 0, 0, 0, 0, 0, -p * L / Ea, 0.03, -0.02, 0.004, 0.006, 0.01];
        var kt = BeamCorotational.Beam3dTangent(coords, sec, u, refVec);
        var kn = NumericJacobian(v => BeamCorotational.Beam3dInternalForce(coords, sec, v, refVec), u, 1e-5);
        // Поперечные перемещения конца (y, z): геометрическая часть здесь ~1/6 материальной — без неё ошибка ~0,17.
        int[] lateral = [1, 2, 7, 8];
        double errLat = BlockErr(kt, kn, lateral), errAll = BlockErr(kt, kn, Enumerable.Range(0, 12).ToArray());
        TestHarness.Check("поперечный блок K_T = ∂F/∂u", errLat < 1e-3, $"отн.={errLat:e2}");
        TestHarness.Check("вся K_T = ∂F/∂u", errAll < 1e-3, $"отн.={errAll:e2}");

        TestHarness.Section("Касательная оболочки фон Кармана с секущей ABD = ∂F_int/∂u");
        // Секущая ABD со связью мембраны и изгиба (B ≠ 0), мембранное сжатие и прогибы.
        var abd = new ShellTangent(
            new[,] { { 6e9, 1.2e9, 0.0 }, { 1.2e9, 5e9, 0.0 }, { 0.0, 0.0, 2e9 } },
            new[,] { { 2e7, 0.0, 0.0 }, { 0.0, -1e7, 0.0 }, { 0.0, 0.0, 5e6 } },
            new[,] { { 8e7, 1.6e7, 0.0 }, { 1.6e7, 6e7, 0.0 }, { 0.0, 0.0, 2.5e7 } },
            new[,] { { 1.5e9, 0.0 }, { 0.0, 1.5e9 } });
        var shell = new SecantShellResponse(abd);
        double[][] quad = [[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [1.0, 1.2, 0.0], [0.0, 1.2, 0.0]];
        var us = new double[24];
        var rnd = new Random(7);
        for (int i = 0; i < 4; i++)
        {
            us[6 * i + 0] = -2e-3 * quad[i][0] + 1e-4 * rnd.NextDouble();   // сжатие вдоль x
            us[6 * i + 1] = -1e-3 * quad[i][1] + 1e-4 * rnd.NextDouble();
            us[6 * i + 2] = 0.02 * Math.Sin(quad[i][0] + 2 * quad[i][1]);   // прогиб
            us[6 * i + 3] = 0.01 * (rnd.NextDouble() - 0.5);
            us[6 * i + 4] = 0.01 * (rnd.NextDouble() - 0.5);
        }
        var ks = ShellElementForces.ElementKTangentGlobal(quad, shell, us);
        var ksn = NumericJacobian(v => ShellElementForces.ElementFInternalGlobal(quad, shell, v), us, 1e-6);
        // Поворот вокруг нормали (drilling) — искусственная жёсткость только в K, в F_int её нет: сверяем без него.
        var noDrill = Enumerable.Range(0, 24).Where(i => i % 6 != 5).ToArray();
        double errShell = BlockErr(ks, ksn, noDrill);
        double errW = BlockErr(ks, ksn, Enumerable.Range(0, 4).Select(i => 6 * i + 2).ToArray());
        TestHarness.Check("оболочка: K_T = ∂F/∂u (без drilling)", errShell < 1e-5, $"отн.={errShell:e2}");
        TestHarness.Check("оболочка: блок прогибов (G·ᵀΣ·G)", errW < 1e-5, $"отн.={errW:e2}");

        TestHarness.Section("Ньютон сжатой стойки: секущее сечение сходится как упругое");
        foreach (double ratio in new[] { 0.5, 0.9 })
        {
            var lin = new LinearBeamResponse(new BeamSection(1.0, Ea, EIy, EIz, Gj, g: 1.0));
            var sd = new SecantBeamResponse(new[,] { { Ea, 0.0, 0.0 }, { 0.0, EIy, 0.0 }, { 0.0, 0.0, EIz } }, Gj);
            double pcr = Math.PI * Math.PI * Math.Min(EIy, EIz) / (4 * L * L);
            var (itLin, uxLin) = Column(lin, ratio * pcr);
            var (itSec, uxSec) = Column(sd, ratio * pcr);
            TestHarness.Check($"P/Pcr = {ratio}: итераций секущего ≤ упругого + 1", itSec > 0 && itSec <= itLin + 1,
                $"секущее {itSec}, упругое {itLin}");
            TestHarness.CheckRel($"P/Pcr = {ratio}: тот же прогиб", uxSec, uxLin, 1e-6);
        }
    }

    /// <summary>Консоль 4 КЭ по z, сжатие P и боковая сила 0,01·P; Ньютон от нуля, ‖r‖/‖F‖ &lt; 1e-8.</summary>
    private static (int It, double Ux) Column(IBeamSectionResponse s, double p)
    {
        const int nEl = 4;
        int nd = 6 * (nEl + 1);
        var nodes = Enumerable.Range(0, nEl + 1).Select(i => new[] { 0.0, 0.0, i * L / nEl }).ToArray();
        double[] refVec = [1.0, 0.0, 0.0];
        var f = new double[nd];
        f[6 * nEl + 2] = -p;
        f[6 * nEl + 0] = 0.01 * p;
        var u = new double[nd];
        int[] free = Enumerable.Range(6, nd - 6).ToArray();
        double fn = Math.Sqrt(f.Sum(x => x * x));
        for (int it = 0; it < 60; it++)
        {
            var fi = new double[nd];
            var k = new double[nd, nd];
            for (int el = 0; el < nEl; el++)
            {
                double[][] c = [nodes[el], nodes[el + 1]];
                var ue = u.Skip(6 * el).Take(12).ToArray();
                var fe = BeamCorotational.Beam3dInternalForce(c, s, ue, refVec);
                var ke = BeamCorotational.Beam3dTangent(c, s, ue, refVec);
                for (int i = 0; i < 12; i++)
                {
                    fi[6 * el + i] += fe[i];
                    for (int j = 0; j < 12; j++) k[6 * el + i, 6 * el + j] += ke[i, j];
                }
            }
            var r = free.Select(i => f[i] - fi[i]).ToArray();
            if (Math.Sqrt(r.Sum(x => x * x)) / fn < 1e-8) return (it, u[6 * nEl]);
            var kf = new double[free.Length, free.Length];
            for (int i = 0; i < free.Length; i++)
                for (int j = 0; j < free.Length; j++) kf[i, j] = k[free[i], free[j]];
            var du = DenseLinAlg.Solve(kf, r);
            for (int i = 0; i < free.Length; i++) u[free[i]] += du[i];
        }
        return (-1, u[6 * nEl]);
    }

    private static double[,] NumericJacobian(Func<double[], double[]> fInt, double[] u, double h)
    {
        int n = u.Length;
        var k = new double[n, n];
        for (int j = 0; j < n; j++)
        {
            var up = (double[])u.Clone(); up[j] += h;
            var um = (double[])u.Clone(); um[j] -= h;
            var fp = fInt(up);
            var fm = fInt(um);
            for (int i = 0; i < n; i++) k[i, j] = (fp[i] - fm[i]) / (2 * h);
        }
        return k;
    }

    // max|K − K_num| / max|K_num| по блоку idx × idx.
    private static double BlockErr(double[,] k, double[,] kn, int[] idx)
    {
        double err = 0.0, max = 0.0;
        foreach (int i in idx)
            foreach (int j in idx)
            {
                err = Math.Max(err, Math.Abs(k[i, j] - kn[i, j]));
                max = Math.Max(max, Math.Abs(kn[i, j]));
            }
        return err / max;
    }
}
