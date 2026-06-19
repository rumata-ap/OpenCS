using System.Diagnostics;
using CScore;
using CScore.Fire;
using CScore.Fire.Entities;

namespace CSfea.Tests;

/// <summary>Замер времени нестационарного теплового расчёта. Включается CSFEA_BENCH=1.</summary>
public static class ThermalBenchmark
{
    public static bool Enabled
    {
        get
        {
            string? v = Environment.GetEnvironmentVariable("CSFEA_BENCH");
            return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void RunAll()
    {
        if (!Enabled)
        {
            Console.WriteLine("  [SKIP] ThermalBenchmark — CSFEA_BENCH=1 для замеров");
            return;
        }

        TestHarness.Section("ThermalBenchmark");
        Run("rect_300x500_R15", BuildRectSection(0.30, 0.50), durationMin: 15.0, meshStepM: 0.012);
        Run("rect_300x500_R15_fine", BuildRectSection(0.30, 0.50), durationMin: 15.0, meshStepM: 0.008);
    }

    static void Run(string name, CrossSection section, double durationMin, double meshStepM)
    {
        var def = new FireSectionDef
        {
            FireDurationMin = durationMin,
            FireCurve = "iso834",
            MeshStepM = meshStepM,
            TimeStepS = 30.0,
            SnapshotStepMin = 5.0,
            Theta = 1.0,
            PicardTolCelsius = 0.5,
            PicardMaxIter = 20,
            BcPreset = "3-sided",
            HoleBcPreset = "adiabatic",
            Algorithm = "ruppert",
            SmoothIterTri = 2,
            MeshElementType = "linear",
            Edges = []
        };

        var sw = Stopwatch.StartNew();
        var result = FireThermalService.Run(def, section, "silicate");
        sw.Stop();

        int nodes = result.MeshInfo.Mesh.X.Length;
        int elems = result.MeshInfo.Mesh.Elements.Length;
        int factorizations = result.ConvergenceLog.Sum(r => r.NPicardIter);
        Console.WriteLine($"  BENCH {name}: nodes={nodes} elems={elems} " +
                          $"factorizations={factorizations} total_ms={sw.ElapsedMilliseconds}");
    }

    static CrossSection BuildRectSection(double w, double h)
    {
        var xs = new[] { 0.0, w, w, 0.0 };
        var ys = new[] { 0.0, 0.0, h, h };
        var hull = new Contour(xs.Append(xs[0]), ys.Append(ys[0]), "hull")
        {
            Type = ContourType.Hull
        };
        var area = new MaterialArea
        {
            Tag = "bench",
            Category = AreaCategory.Region,
            Contours = [hull],
            Hull = hull
        };
        return new CrossSection { Tag = "bench-section", Areas = [area] };
    }
}
