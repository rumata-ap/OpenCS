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
        Run("rect_200x300_R5", BuildRectSection(0.20, 0.30), durationMin: 5.0, meshStepM: 0.02);
        Run("rect_200x300_R5_fine", BuildRectSection(0.20, 0.30), durationMin: 5.0, meshStepM: 0.014);

        Console.WriteLine("  -- T3-fine vs T6-coarse --");
        RunVariant("T3_fine", BuildRectSection(0.20, 0.30), durationMin: 5.0, meshStepM: 0.014, elementType: "linear");
        RunVariant("T6_coarse", BuildRectSection(0.20, 0.30), durationMin: 5.0, meshStepM: 0.028, elementType: "quadratic");
    }

    static void RunVariant(string name, CrossSection section, double durationMin, double meshStepM, string elementType)
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
            MeshElementType = elementType,
            Edges = []
        };

        var sw = Stopwatch.StartNew();
        var result = FireThermalService.Run(def, section, "silicate");
        sw.Stop();

        // Контрольная точка — ближайший к центру узел.
        var mesh = result.MeshInfo.Mesh;
        double cx = 0.15, cy = 0.25;
        int nearest = 0; double best = double.MaxValue;
        for (int i = 0; i < mesh.X.Length; i++)
        {
            double dx = mesh.X[i] - cx, dy = mesh.Y[i] - cy;
            double d2 = dx * dx + dy * dy;
            if (d2 < best) { best = d2; nearest = i; }
        }
        double centerT = result.Snapshots[^1][nearest];
        int factorizations = result.ConvergenceLog.Sum(r => r.NPicardIter);
        Console.WriteLine($"  BENCH {name}: nodes={mesh.X.Length} factorizations={factorizations} " +
                          $"total_ms={sw.ElapsedMilliseconds} centerT={centerT:F2}");
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
