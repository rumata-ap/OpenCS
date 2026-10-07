using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Стержневой КЭ со связанной матрицей сечения и средняя податливость по Лобатто.</summary>
[HarnessChecks]
public class SecantBeamTests
{
    const double L = 3.0, Ea = 6e9, EIy = 1.2e8, EIz = 4e7, Gj = 2.5e7;

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Связанная матрица сечения в КЭ стержня");

        var sDiag = new[,] { { Ea, 0.0, 0.0 }, { 0.0, EIy, 0.0 }, { 0.0, 0.0, EIz } };
        var kOld = BeamElements.Beam3dKLocal(
            new LinearBeamResponse(new BeamSection(e: 1.0, a: Ea, iy: EIy, iz: EIz, j: Gj, g: 1.0)), L);
        var kNew = BeamElements.Beam3dKLocal(sDiag, Gj, L);
        TestHarness.Check("диагональная S = прежний КЭ", Rel(kNew, kOld) < 1e-12, $"отн.={Rel(kNew, kOld):e2}");

        // Внецентренное сечение: его центр тяжести на z = e от оси узлов КЭ (S₀₁ = EA·e).
        const double e = 0.12, p = 1e4;
        var sOff = new[,] { { Ea, Ea * e, 0.0 }, { Ea * e, EIy + Ea * e * e, 0.0 }, { 0.0, 0.0, EIz } };
        var kCoupled = BeamElements.Beam3dKLocal(sOff, Gj, L);
        TestHarness.Check("связанная K симметрична", Asym(kCoupled) < 1e-14 * MaxAbs(kCoupled));

        // Консоль одним КЭ, сила P по z на свободном конце. Продольной силы нет, N(x) ≡ 0, поэтому
        // изгиб идёт вокруг центра тяжести: w = P·L³/(3·EI_c), ε₀ = −e·κ, u = −e·P·L²/(2·EI_c).
        var free = new[] { 6, 7, 8, 9, 10, 11 };
        var kff = new double[6, 6];
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
                kff[i, j] = kCoupled[free[i], free[j]];
        var dFree = Solve(kff, [0, 0, p, 0, 0, 0]);
        var d = new double[12];
        for (int i = 0; i < 6; i++) d[free[i]] = dFree[i];
        TestHarness.CheckRel("внецентренная консоль, 1 КЭ: прогиб = P·L³/(3·EI_c)", d[8], p * L * L * L / (3 * EIy), 1e-10);
        TestHarness.CheckRel("внецентренная консоль, 1 КЭ: |u| = e·P·L²/(2·EI_c)", Math.Abs(d[6]), e * p * L * L / (2 * EIy), 1e-10);
        double nMax = 0.0, mRoot = 0.0;
        foreach (double xi in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var (e0, ky, kz) = BeamElements.Beam3dCoupledStrains(sOff, L, d, xi);
            nMax = Math.Max(nMax, Math.Abs(sOff[0, 0] * e0 + sOff[0, 1] * ky + sOff[0, 2] * kz));
            if (xi == 0.0) mRoot = sOff[1, 0] * e0 + sOff[1, 1] * ky;
        }
        TestHarness.Check("внецентренная консоль: N ≡ 0 по длине", nMax < 1e-9 * p, $"max|N|={nMax:e2} Н");
        TestHarness.CheckRel("внецентренная консоль: |M| в заделке = P·L", Math.Abs(mRoot), p * L, 1e-10);

        var resp = new SecantBeamResponse(sOff, Gj);
        TestHarness.Check("SecantBeamResponse → связанный КЭ",
            Rel(BeamElements.Beam3dKLocal(resp, L), kCoupled) == 0.0);
        var f = resp.Forces(1e-4, 2e-3, -1e-3);
        TestHarness.CheckRel("SecantBeamResponse: N = S·ε", f.N, Ea * 1e-4 + Ea * e * 2e-3, 1e-14);

        TestHarness.Section("Сдвиговая податливость (Тимошенко)");
        TestHarness.Check("без сдвига — прежний связанный КЭ",
            Rel(BeamElements.Beam3dKLocal(sOff, Gj, L, BeamShearStiffness.Rigid), kCoupled) == 0.0);
        const double gaY = 3e7, gaZ = 5e7;
        var kT = BeamElements.Beam3dKLocal(sOff, Gj, L, new BeamShearStiffness(gaY, gaZ));
        TestHarness.Check("КЭ Тимошенко: K симметрична", Asym(kT) < 1e-12 * MaxAbs(kT), $"асимм.={Asym(kT):e2}");
        var rigid = new double[12];
        for (int i = 0; i < 2; i++) { rigid[6 * i + 1] = 0.3 + 0.2 * i * L; rigid[6 * i + 2] = -0.1 - 0.4 * i * L; }
        rigid[5] = rigid[11] = 0.2;
        rigid[4] = rigid[10] = 0.4;
        double fRigid = CSfea.Sparse.Dense.MaxAbs(CSfea.Sparse.Dense.MatVec(kT, rigid));
        TestHarness.Check("КЭ Тимошенко: жёсткое смещение без усилий", fRigid < 1e-9 * MaxAbs(kT), $"max|F|={fRigid:e2}");

        // Консоль одним КЭ, силы P по y и z на конце: w = P·L³/(3·EI) + P·L/GA — точно (Q постоянна).
        var kTff = new double[6, 6];
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
                kTff[i, j] = kT[free[i], free[j]];
        var dT = Solve(kTff, [0, p, p, 0, 0, 0]);
        var dTl = new double[12];
        for (int i = 0; i < 6; i++) dTl[free[i]] = dT[i];
        TestHarness.CheckRel("консоль Тимошенко: v = P·L³/(3·EI_z) + P·L/GA_y", dTl[7], p * L * L * L / (3 * EIz) + p * L / gaY, 1e-10);
        TestHarness.CheckRel("консоль Тимошенко: w = P·L³/(3·EI_c) + P·L/GA_z", dTl[8], p * L * L * L / (3 * EIy) + p * L / gaZ, 1e-10);
        TestHarness.CheckRel("консоль Тимошенко: θ конца — только изгиб", -dTl[10], p * L * L / (2 * EIy), 1e-10);

        var (bend, gy, gz) = BeamElements.Beam3dShearSplit(sOff, Gj, L, new BeamShearStiffness(gaY, gaZ), dTl);
        TestHarness.CheckRel("разделение: γ_y = P/GA_y", gy, p / gaY, 1e-10);
        TestHarness.CheckRel("разделение: γ_z = P/GA_z", gz, p / gaZ, 1e-10);
        var (_, kyRoot, kzRoot) = BeamElements.Beam3dCoupledStrains(sOff, L, bend, 0.0);
        TestHarness.CheckRel("разделение: M_z в заделке по изгибной части = P·L", Math.Abs(EIz * kzRoot), p * L, 1e-10);
        TestHarness.CheckRel("разделение: |κ_y| в заделке = P·L/EI_c", Math.Abs(kyRoot), p * L / EIy, 1e-10);

        var respT = new SecantBeamResponse(sOff, Gj, new BeamShearStiffness(gaY, gaZ));
        TestHarness.Check("SecantBeamResponse со сдвигом → КЭ Тимошенко", Rel(BeamElements.Beam3dKLocal(respT, L), kT) == 0.0);

        TestHarness.Section("Средняя податливость по трём сечениям Лобатто");
        var same = BeamElements.MeanCompliance(sOff, sOff, sOff);
        TestHarness.Check("одинаковые сечения → то же S", Rel(same, sOff) < 1e-12, $"отн.={Rel(same, sOff):e2}");

        var s0 = new[,] { { Ea, 0.0, 0.0 }, { 0.0, EIy, 0.0 }, { 0.0, 0.0, EIz } };
        var s1 = new[,] { { Ea / 2, 0.0, 0.0 }, { 0.0, EIy / 5, 0.0 }, { 0.0, 0.0, EIz } };
        var mean = BeamElements.MeanCompliance(s1, s0, s0);
        double eiExp = 1.0 / (1.0 / 6.0 * 5.0 / EIy + 5.0 / 6.0 / EIy);
        TestHarness.CheckRel("диагональ: гармоническое среднее EI (вес 1/6 у конца)", mean[1, 1], eiExp, 1e-12);
        TestHarness.Check("жёсткость ниже средней арифметической",
            mean[1, 1] < (EIy / 5 + 4 * EIy + EIy) / 6.0, $"EI={mean[1, 1]:e4}");
    }

    // Гаусс с выбором главного элемента для малой плотной системы.
    static double[] Solve(double[,] a0, double[] b0)
    {
        int n = b0.Length;
        var a = (double[,])a0.Clone();
        var b = (double[])b0.Clone();
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int r = c + 1; r < n; r++)
                if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
            for (int j = 0; j < n; j++) (a[c, j], a[piv, j]) = (a[piv, j], a[c, j]);
            (b[c], b[piv]) = (b[piv], b[c]);
            for (int r = c + 1; r < n; r++)
            {
                double f = a[r, c] / a[c, c];
                for (int j = c; j < n; j++) a[r, j] -= f * a[c, j];
                b[r] -= f * b[c];
            }
        }
        var x = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double s = b[r];
            for (int j = r + 1; j < n; j++) s -= a[r, j] * x[j];
            x[r] = s / a[r, r];
        }
        return x;
    }

    static double MaxAbs(double[,] m) => m.Cast<double>().Max(Math.Abs);

    static double Rel(double[,] a, double[,] b)
    {
        double d = 0.0;
        for (int i = 0; i < a.GetLength(0); i++)
            for (int j = 0; j < a.GetLength(1); j++)
                d = Math.Max(d, Math.Abs(a[i, j] - b[i, j]));
        return d / MaxAbs(b);
    }

    static double Asym(double[,] m)
    {
        double d = 0.0;
        for (int i = 0; i < m.GetLength(0); i++)
            for (int j = 0; j < m.GetLength(1); j++)
                d = Math.Max(d, Math.Abs(m[i, j] - m[j, i]));
        return d;
    }
}
