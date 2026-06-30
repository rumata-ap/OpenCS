using CScore;

namespace CSfea.Tests;

public static class SteelStrengthTests
{
    const double Ry = 245e6; // С245, Па
    const double gammaM = 1.025;

    public static void RunAll()
    {
        TestHarness.Section("SteelStrengthCheck: Проверки прочности (раздел 8 СП 16)");

        AxialTension_Passed();
        AxialCompression_Passed();
        Bending_Passed();
        CompressionBending_SinglePlane();
        CompressionBending_DoublePlane();
        TensionBending();
        Shear_Passed();
        Torsion_Passed();
        BendingTorsion();
        LateralBending();
        WebCrippling();
        ShearBuckling();
    }

    static void AxialTension_Passed()
    {
        double N = 500e3; // 500 кН
        double An = 0.005; // 50 см²
        var result = SteelStrengthCheck.CheckAxialTension(N, An, Ry, gammaM);
        TestHarness.Check("AxialTension 8.1.1",
            result.Passed && result.Formula == "8.1.1",
            $"N={N / 1e3:F0}kN, An={An * 1e4:F1}cm², ratio={result.Ratio:F3}");
    }

    static void AxialCompression_Passed()
    {
        double N = -500e3;
        double chi = 0.9;
        double Aeff = 0.005;
        var result = SteelStrengthCheck.CheckAxialCompression(N, chi, Aeff, Ry, gammaM);
        TestHarness.Check("AxialCompression 8.1.1",
            result.Passed && result.Formula == "8.1.1",
            $"N={N / 1e3:F0}kN, χ={chi:F2}, ratio={result.Ratio:F3}");
    }

    static void Bending_Passed()
    {
        double M = 100e3; // 100 кН·м
        double chiB = 1.0;
        double Weff = 0.001; // 1000 см³
        var result = SteelStrengthCheck.CheckBending(M, chiB, Weff, Ry, gammaM, "X");
        TestHarness.Check("Bending 8.1.2",
            result.Passed && result.Formula == "8.1.2",
            $"M={M / 1e3:F0}kNm, ratio={result.Ratio:F3}");
    }

    static void CompressionBending_SinglePlane()
    {
        var result = SteelStrengthCheck.CheckCompressionBendingSinglePlane(
            -1000e3, 50e3, 0.9, 0.95, 0.01, 0.002, Ry, gammaM, "X");
        TestHarness.Check("CompressionBending 8.1.3",
            result.Formula == "8.1.3",
            $"ratio={result.Ratio:F3}, formula={result.Formula}");
    }

    static void CompressionBending_DoublePlane()
    {
        var result = SteelStrengthCheck.CheckCompressionBendingDoublePlane(
            -1000e3, 50e3, 30e3, 0.9, 0.95, 0.95, 0.01, 0.002, 0.001, Ry, gammaM);
        TestHarness.Check("CompressionBending 8.1.4",
            result.Formula == "8.1.4",
            $"ratio={result.Ratio:F3}, formula={result.Formula}");
    }

    static void TensionBending()
    {
        var result = SteelStrengthCheck.CheckTensionBending(
            500e3, 50e3, 0, 0.005, 0.002, 0.001, Ry, gammaM);
        TestHarness.Check("TensionBending 8.1.5",
            result.Formula == "8.1.5",
            $"ratio={result.Ratio:F3}, formula={result.Formula}");
    }

    static void Shear_Passed()
    {
        double Q = 200e3;
        double Aw = 0.003;
        var result = SteelStrengthCheck.CheckShear(Q, Aw, Ry, gammaM);
        TestHarness.Check("Shear 8.6",
            result.Passed && result.Formula == "8.6",
            $"Q={Q / 1e3:F0}kN, ratio={result.Ratio:F3}");
    }

    static void Torsion_Passed()
    {
        double Mz = 50e3;
        double Wt = 0.0005;
        var result = SteelStrengthCheck.CheckTorsion(Mz, Wt, Ry, gammaM);
        TestHarness.Check("Torsion 8.8.1",
            result.Formula == "8.8.1",
            $"Mz={Mz / 1e3:F0}kNm, ratio={result.Ratio:F3}");
    }

    static void BendingTorsion()
    {
        var result = SteelStrengthCheck.CheckBendingTorsion(
            100e3, 50e3, 0.001, 0.0005, Ry, gammaM);
        TestHarness.Check("BendingTorsion 8.9.1",
            result.Formula == "8.9.1",
            $"ratio={result.Ratio:F3}, formula={result.Formula}");
    }

    static void LateralBending()
    {
        var result = SteelStrengthCheck.CheckLateralBending(
            -500e3, 100e3, 0.95, 0.005, 0.001, Ry, gammaM, "X");
        TestHarness.Check("LateralBending 8.7.1",
            result.Formula == "8.7.1",
            $"ratio={result.Ratio:F3}, formula={result.Formula}");
    }

    static void WebCrippling()
    {
        var result = SteelStrengthCheck.CheckWebCrippling(
            100e3, 0.1, 0.25, 0.01, Ry, gammaM);
        TestHarness.Check("WebCrippling 8.2.1",
            result.Formula == "8.2.1",
            $"ratio={result.Ratio:F3}, formula={result.Formula}");
    }

    static void ShearBuckling()
    {
        var result = SteelStrengthCheck.CheckShearBuckling(
            200e3, 0.003, 0.3, 0.01, 210e9, Ry, gammaM);
        TestHarness.Check("ShearBuckling 8.5",
            result.Formula == "8.5",
            $"ratio={result.Ratio:F3}, formula={result.Formula}");
    }
}
