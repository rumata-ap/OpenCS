using CScore.Fire;

namespace CSfea.Tests;

/// <summary>Приведение эпюры температуры к линейной по п. 8.44а СП 468.</summary>
public static class FireTemperatureProfileTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireTemperatureProfile: приведение эпюры");
        LinearField_IsReproducedExactly();
        Reduction_PreservesAreaAndFirstMoment();
        UniformField_IsFlaggedAsUniform();
    }

    // Аналитическая проверка формул приведения без построения сечения.
    static (double THot, double TCold) Reduce(double[] s, double[] t)
        => FireTemperatureProfile.ReduceToLinear(s, t);

    static void LinearField_IsReproducedExactly()
    {
        // T(s) = 800 - 700*s/h при h = 0,4 м.
        double h = 0.4;
        int n = 41;
        var s = new double[n];
        var t = new double[n];
        for (int i = 0; i < n; i++)
        {
            s[i] = h * i / (n - 1);
            t[i] = 800.0 - 700.0 * s[i] / h;
        }

        var (hot, cold) = Reduce(s, t);
        TestHarness.Check("FireProfile_LinearExact",
            Math.Abs(hot - 800.0) < 1e-6 && Math.Abs(cold - 100.0) < 1e-6,
            $"hot={hot:F3}, cold={cold:F3}");
    }

    static void Reduction_PreservesAreaAndFirstMoment()
    {
        // Криволинейная эпюра: T(s) = 20 + 780*exp(-8*s/h).
        double h = 0.4;
        int n = 81;
        var s = new double[n];
        var t = new double[n];
        for (int i = 0; i < n; i++)
        {
            s[i] = h * i / (n - 1);
            t[i] = 20.0 + 780.0 * Math.Exp(-8.0 * s[i] / h);
        }

        var (hot, cold) = Reduce(s, t);

        double areaActual = Trapz(s, t);
        double momentActual = TrapzMoment(s, t);

        double areaLinear = h * (hot + cold) / 2.0;
        double momentLinear = h * h * (hot / 6.0 + cold / 3.0);

        TestHarness.CheckRel("FireProfile_AreaPreserved", areaLinear, areaActual, 1e-6);
        TestHarness.CheckRel("FireProfile_MomentPreserved", momentLinear, momentActual, 1e-6);
    }

    static void UniformField_IsFlaggedAsUniform()
    {
        double h = 0.4;
        int n = 21;
        var s = new double[n];
        var t = new double[n];
        for (int i = 0; i < n; i++) { s[i] = h * i / (n - 1); t[i] = 300.0; }

        var (hot, cold) = Reduce(s, t);
        TestHarness.Check("FireProfile_UniformDetected",
            Math.Abs(hot - cold) < 5.0, $"hot={hot:F3}, cold={cold:F3}");
    }

    static double Trapz(double[] x, double[] y)
    {
        double sum = 0.0;
        for (int i = 1; i < x.Length; i++)
            sum += 0.5 * (y[i] + y[i - 1]) * (x[i] - x[i - 1]);
        return sum;
    }

    static double TrapzMoment(double[] x, double[] y)
    {
        double sum = 0.0;
        for (int i = 1; i < x.Length; i++)
            sum += 0.5 * (y[i] * x[i] + y[i - 1] * x[i - 1]) * (x[i] - x[i - 1]);
        return sum;
    }
}
