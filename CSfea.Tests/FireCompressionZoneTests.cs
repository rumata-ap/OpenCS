using CScore.Fire;

namespace CSfea.Tests;

/// <summary>Проверки высоты сжатой зоны для температурной кривизны по СП 468.</summary>
public static class FireCompressionZoneTests
{
    /// <summary>Запустить проверки формулы (8.11) и её границ применимости.</summary>
    public static void RunAll()
    {
        TestHarness.Section("FireCompressionZone: x_t и ξ_R");

        XiR_ColdMatchesClassicFormula();
        XiR_DropsWithTemperature();
        Formula811_MatchesHandCalc();
        Formula811_CapsAtXiR();
        WidthUndefined_IsNotSupported();
    }

    static void XiR_ColdMatchesClassicFormula()
    {
        double actual = FireCompressionZone.XiR(435.0, 200_000.0, 1.15, 1.0, 0.0035);
        double expected = 0.8 / (1.0 + (1.15 * 435.0) / (1.0 * 200_000.0) / 0.0035);
        TestHarness.CheckRel(nameof(XiR_ColdMatchesClassicFormula), actual, expected, 1e-12);
    }

    static void XiR_DropsWithTemperature()
    {
        double cold = FireCompressionZone.XiR(435.0, 200_000.0, 1.15, 1.0, 0.0035);
        double hot = FireCompressionZone.XiR(250.0, 120_000.0, 1.15, 1.0, 0.0025);
        TestHarness.Check(nameof(XiR_DropsWithTemperature), hot < cold && hot > 0.0,
            $"cold={cold:e4}, hot={hot:e4}");
    }

    static void Formula811_MatchesHandCalc()
    {
        double expected = 435e6 * 1e-3 / (17e6 * 0.3);
        var result = FireCompressionZone.ByFormula811(435e6, 1e-3, 17e6, 0.3, 0.5, 0.8);
        TestHarness.CheckRel(nameof(Formula811_MatchesHandCalc), result.XtM, expected, 1e-12);
        TestHarness.Check(nameof(Formula811_MatchesHandCalc) + ".supported", result.Supported);
        TestHarness.Check(nameof(Formula811_MatchesHandCalc) + ".not-capped", !result.XiCapped);
    }

    static void Formula811_CapsAtXiR()
    {
        var result = FireCompressionZone.ByFormula811(435e6, 0.02, 17e6, 0.3, 0.5, 0.8);
        TestHarness.Check(nameof(Formula811_CapsAtXiR), result.XiCapped &&
            Math.Abs(result.XtM - 0.4) < 1e-12,
            $"xt={result.XtM:e4}, xiR={result.XiR:e4}");
    }

    static void WidthUndefined_IsNotSupported()
    {
        var result = FireCompressionZone.ByFormula811(435e6, 1e-3, 17e6, 0.0, 0.5, 0.8);
        TestHarness.Check(nameof(WidthUndefined_IsNotSupported),
            !result.Supported && result.UnsupportedReasonKey == "FireCurvature_WidthUndefined");
    }
}
