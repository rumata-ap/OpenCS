using CScore;

namespace CSfea.Tests;

public static class SteelStabilityTests
{
    const double Ry = 245e6; // С245, Па
    const double E = 210e9;
    const double gammaM = 1.025;

    public static void RunAll()
    {
        TestHarness.Section("SteelStabilityCheck: Проверки устойчивости (раздел 9 СП 16)");

        Chi_ShortMember_Returns1();
        Chi_LongMember_Decreases();
        Chi_DifferentCurves();
        ChiB_WorksCorrectly();
        Buckling_Passed();
        BucklingBending_CalculatesCorrectly();
        BucklingAxial_Passed();
        PlateBuckling_Passed();
        TorsionBuckling_Passed();
        BucklingBendingTorsion_Passed();
        LocalBuckling_Passed();
        BucklingReduced_Passed();
        Eta_InterpolatesCorrectly();
        ConventionalSlenderness_Calculates();
        EulerForce_Calculates();
    }

    static void Chi_ShortMember_Returns1()
    {
        double chi = SteelStabilityCheck.Chi(0.1, BucklingCurve.b);
        TestHarness.Check("Chi short member",
            Math.Abs(chi - 1.0) < 0.001,
            $"λ̄=0.1, χ={chi:F3}");
    }

    static void Chi_LongMember_Decreases()
    {
        double chi = SteelStabilityCheck.Chi(2.0, BucklingCurve.b);
        TestHarness.Check("Chi long member",
            chi < 1.0 && chi > 0,
            $"λ̄=2.0, χ={chi:F3}");
    }

    static void Chi_DifferentCurves()
    {
        double lambdaBar = 1.0;
        double chiA0 = SteelStabilityCheck.Chi(lambdaBar, BucklingCurve.a0);
        double chiD = SteelStabilityCheck.Chi(lambdaBar, BucklingCurve.d);
        TestHarness.Check("Chi a0 > Chi d",
            chiA0 > chiD,
            $"χ(a0)={chiA0:F3}, χ(d)={chiD:F3}");
    }

    static void ChiB_WorksCorrectly()
    {
        double chiB = SteelStabilityCheck.ChiB(1.5, BucklingCurve.b);
        TestHarness.Check("ChiB",
            chiB > 0 && chiB < 1.0,
            $"λ̄=1.5, χb={chiB:F3}");
    }

    static void Buckling_Passed()
    {
        double N = -500e3;
        double chi = 0.9;
        double A = 0.005;
        var result = SteelStabilityCheck.CheckBuckling(N, chi, A, Ry, gammaM);
        TestHarness.Check("Buckling 9.1.1",
            result.Passed && result.Formula == "9.1.1",
            $"N={N / 1e3:F0}kN, ratio={result.Ratio:F3}");
    }

    static void BucklingBending_CalculatesCorrectly()
    {
        var result = SteelStabilityCheck.CheckBucklingBending(
            -1000e3, 50e3, 0.9, 0.95, 0.01, 0.002, 5000e3, 1.5, 1.0, Ry, gammaM, "X");
        TestHarness.Check("BucklingBending 9.2.2",
            result.Formula == "9.2.2" && result.Variables.ContainsKey("η"),
            $"ratio={result.Ratio:F3}, η={result.Variables["η"]:F3}");
    }

    static void BucklingAxial_Passed()
    {
        var result = SteelStabilityCheck.CheckBucklingAxial(
            -500e3, 0.9, 0.005, Ry, gammaM);
        TestHarness.Check("BucklingAxial 9.1.2",
            result.Formula == "9.1.2",
            $"ratio={result.Ratio:F3}");
    }

    static void PlateBuckling_Passed()
    {
        var result = SteelStabilityCheck.CheckPlateBuckling(
            100e6, 0.8, Ry, gammaM);
        TestHarness.Check("PlateBuckling 9.2.1",
            result.Formula == "9.2.1",
            $"ratio={result.Ratio:F3}");
    }

    static void TorsionBuckling_Passed()
    {
        var result = SteelStabilityCheck.CheckTorsionBuckling(
            50e3, 0.9, 0.0005, Ry, gammaM);
        TestHarness.Check("TorsionBuckling 9.3.1",
            result.Formula == "9.3.1",
            $"ratio={result.Ratio:F3}");
    }

    static void BucklingBendingTorsion_Passed()
    {
        var result = SteelStabilityCheck.CheckBucklingBendingTorsion(
            -1000e3, 100e3, 50e3, 0.9, 0.95, 0.9, 0.01, 0.002, 0.0005, Ry, gammaM);
        TestHarness.Check("BucklingBendingTorsion 9.4.1",
            result.Formula == "9.4.1",
            $"ratio={result.Ratio:F3}");
    }

    static void LocalBuckling_Passed()
    {
        var result = SteelStabilityCheck.CheckLocalBuckling(
            100e6, 0.8, Ry, gammaM);
        TestHarness.Check("LocalBuckling 9.5",
            result.Formula == "9.5",
            $"ratio={result.Ratio:F3}");
    }

    static void BucklingReduced_Passed()
    {
        var result = SteelStabilityCheck.CheckBucklingReduced(
            -1000e3, 100e3, 0.9, 0.95, 0.008, 0.0015, Ry, gammaM, "X");
        TestHarness.Check("BucklingReduced 9.6",
            result.Formula == "9.6",
            $"ratio={result.Ratio:F3}");
    }

    static void Eta_InterpolatesCorrectly()
    {
        double eta0 = SteelStabilityCheck.Eta(0.0);
        double eta1 = SteelStabilityCheck.Eta(1.0);
        double eta2 = SteelStabilityCheck.Eta(2.0);
        TestHarness.Check("Eta interpolation",
            Math.Abs(eta0 - 1.0) < 0.001 && Math.Abs(eta1 - 1.0) < 0.001 && eta2 > 1.0,
            $"η(0)={eta0:F3}, η(1)={eta1:F3}, η(2)={eta2:F3}");
    }

    static void ConventionalSlenderness_Calculates()
    {
        double l0 = 3.0; // м
        double i = 0.05; // м
        double lambdaBar = SteelStabilityCheck.ConventionalSlenderness(l0, i, E, Ry);
        TestHarness.Check("ConventionalSlenderness",
            lambdaBar > 0 && lambdaBar < 10,
            $"l0={l0}m, i={i}m, λ̄={lambdaBar:F3}");
    }

    static void EulerForce_Calculates()
    {
        double EI = E * 0.0001; // I = 10000 см⁴
        double l0 = 3.0;
        double Ncr = SteelStabilityCheck.EulerForce(EI, l0);
        TestHarness.Check("EulerForce",
            Ncr > 0,
            $"EI={EI / 1e6:F0}MN·m², l0={l0}m, Ncr={Ncr / 1e3:F0}kN");
    }
}
