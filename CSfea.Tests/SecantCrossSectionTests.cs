using CScore;
using CSfea.CScoreBridge;

namespace CSfea.Tests;

/// <summary>Пофибровая секущая 3×3 стержневого сечения: точное воспроизведение усилий НДМ.</summary>
[HarnessChecks]
public class SecantCrossSectionTests
{
    const CalcType Calc = CalcType.N;

    static readonly (string Name, Kurvature K)[] States =
    [
        ("упругое", K(-1e-6, 1e-6, -5e-7)),
        ("трещина снизу", K(1e-5, -1e-3, 0)),
        ("косой изгиб", K(0, -1.5e-3, 2e-3)),
        ("внецентренное сжатие", K(-5e-4, -1e-3, 0)),
        ("текучесть", K(1e-3, -1.5e-2, 0)),
    ];

    static Kurvature K(double e0, double ky, double kz) => new() { e0 = e0, ky = ky, kz = kz };

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Секущая 3×3 стержня: S·(e0, ky, kz) = усилия НДМ");
        foreach (bool symmetric in new[] { true, false })
        {
            var section = symmetric
                ? RcTestMaterials.Rect(0.3, 0.5, 0.05, 6.03e-4, 6.03e-4)
                : RcTestMaterials.Rect(0.3, 0.5, 0.05, 9.42e-4, 4.02e-4);
            string tag = symmetric ? "симм." : "несимм.";
            foreach (var (name, k) in States)
            {
                CheckState($"{tag}, {name}, растяжение бетона", section, k, ten: true, psi: false);
                CheckState($"{tag}, {name}, без растяжения", section, k, ten: false, psi: false);
                // ψs — только для сечения с трещиной; без трещины (упругое, целиком сжатое) его нет.
                if (SecantCrossSectionBuilder.IsCracked(section, k, Calc))
                    CheckState($"{tag}, {name}, без растяжения + ψs", section, k, ten: false, psi: true);
                else
                    TestHarness.Check($"{tag}, {name}: трещины нет, ψs не применяется",
                        name is "упругое" or "внецентренное сжатие");
            }
        }

        TestHarness.Section("Секущая 3×3 стержня: упругий предел и связь N–M");
        RunElasticLimitAndCoupling();

        TestHarness.Section("Секущая 3×3 стержня: бетон без сетки (контурный путь)");
        RunContour();
    }

    static void CheckState(string name, CrossSection section, Kurvature k, bool ten, bool psi)
    {
        Dictionary<Fiber, double>? map = null;
        if (psi)
        {
            var f = section.Compute(k, Calc, ten, true, computeStiffness: false);
            map = SecantCrossSectionBuilder.EpsCrcAtCracking(section, Calc, Calc, f.N, f.Mx, f.My);
            TestHarness.Check($"{name}: εs,crc найдены", map != null && map.Values.Any(v => v > 0));
            if (map == null) return;
        }

        var reference = section.Integral(k, Calc, ten, true);
        if (map != null)
            reference = Curvature8232.ApplyPsiCorrection(section, k, reference, map, Calc);
        double[] r = [reference.N, reference.Mx, reference.My];

        var (s, load) = SecantCrossSectionBuilder.Build(section, k, Calc, ten, epsCrcByFiber: map);
        double[] kv = [k.e0, k.ky, k.kz];
        double[] f2 = [.. Enumerable.Range(0, 3).Select(i => s[i, 0] * kv[0] + s[i, 1] * kv[1] + s[i, 2] * kv[2])];

        // Моменты приводятся к силам делением на высоту сечения 0,5 м.
        const double h = 0.5;
        double scale = Math.Max(Math.Abs(r[0]), Math.Max(Math.Abs(r[1]), Math.Abs(r[2])) / h);
        double err = Math.Max(Math.Abs(f2[0] - r[0]), Math.Max(Math.Abs(f2[1] - r[1]), Math.Abs(f2[2] - r[2])) / h) / scale;
        bool same = load.N == reference.N && load.Mx == reference.Mx && load.My == reference.My;
        TestHarness.Check($"{name}: S·k = усилия", err < 1e-9 && same, $"отн.={err:e2}, N={r[0]:f2}, Mx={r[1]:f3}");

        double norm = s.Cast<double>().Max(Math.Abs);
        double asym = Math.Max(Math.Abs(s[0, 1] - s[1, 0]), Math.Max(Math.Abs(s[0, 2] - s[2, 0]), Math.Abs(s[1, 2] - s[2, 1])));
        double minEig = SecantLaminateTests.JacobiEigenvalues(s).Min();
        TestHarness.Check($"{name}: симметрична, λmin ≥ 0", asym == 0.0 && minEig >= -1e-12 * norm, $"λmin={minEig:e3}");
    }

    static void RunElasticLimitAndCoupling()
    {
        var section = RcTestMaterials.Rect(0.3, 0.5, 0.05, 6.03e-4, 6.03e-4);
        // Весь бетон слабо сжат: секущая и касательная — на одном начальном участке диаграммы.
        var k = K(-1e-6, 2e-7, -1e-7);
        var (s, _) = SecantCrossSectionBuilder.Build(section, k, Calc, ten: true);
        var t = section.Compute(k, Calc, true, true).Tangent!;
        double err = 0.0, norm = 0.0;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                err = Math.Max(err, Math.Abs(s[i, j] - t[i, j]));
                norm = Math.Max(norm, Math.Abs(t[i, j]));
            }
        TestHarness.Check("упругий предел = касательная НДМ", err / norm < 1e-6, $"отн.={err / norm:e2}");

        double Coupling(double[,] m) => Math.Abs(m[0, 1]) / Math.Sqrt(m[0, 0] * m[1, 1]);
        double c0 = Coupling(s);
        var (sc, _) = SecantCrossSectionBuilder.Build(section, K(1e-5, -1e-3, 0), Calc, ten: false);
        double c1 = Coupling(sc);
        TestHarness.Check("симметричное сечение: в упругой стадии S₁₂ ≈ 0", c0 < 1e-9, $"S₁₂/√(S₁₁S₂₂)={c0:e2}");
        TestHarness.Check("с трещиной S₁₂ ≠ 0 (нейтральная ось смещена)", c1 > 0.1, $"S₁₂/√(S₁₁S₂₂)={c1:f3}");
    }

    static void RunContour()
    {
        var section = RcTestMaterials.Rect(0.3, 0.5, 0.05, 9.42e-4, 4.02e-4, mesh: false);
        foreach (var (name, k) in States)
        {
            var reference = section.Integral(k, Calc, true, true);
            var (s, _) = SecantCrossSectionBuilder.Build(section, k, Calc, ten: true);
            double n = s[0, 0] * k.e0 + s[0, 1] * k.ky + s[0, 2] * k.kz;
            double mx = s[1, 0] * k.e0 + s[1, 1] * k.ky + s[1, 2] * k.kz;
            double scale = Math.Max(Math.Abs(reference.N), Math.Abs(reference.Mx) / 0.5);
            double err = Math.Max(Math.Abs(n - reference.N), Math.Abs(mx - reference.Mx) / 0.5) / scale;
            TestHarness.Check($"контур, {name}: S·k = усилия", err < 1e-9, $"отн.={err:e2}");
        }
    }
}
