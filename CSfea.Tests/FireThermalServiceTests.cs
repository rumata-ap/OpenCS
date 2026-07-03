using CScore;
using CScore.Fire;
using CScore.Fire.Entities;

namespace CSfea.Tests;

/// <summary>Тесты сервиса огневого теплового расчёта и бинарного blob-кодека.</summary>
public static class FireThermalServiceTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireThermalService: smoke + blob");
        var section = CreateRectSection(0.2, 0.4);
        var def = CreateSmokeDef();
        var result = FireThermalService.Run(def, section, aggregateType: "silicate");

        Run_RectangleThreeSidedFire(result);
        BlobCodec_RoundTrip(result);
    }

    private static void Run_RectangleThreeSidedFire(FireThermalResult result)
    {
        double maxTemp = result.Snapshots.SelectMany(s => s).DefaultIfEmpty(20.0).Max();

        TestHarness.Check("FireThermalService_SnapshotsCount", result.Snapshots.Length > 1, $"snap={result.Snapshots.Length}");
        TestHarness.Check("FireThermalService_HeatingObserved", maxTemp > 20.1, $"maxT={maxTemp:F3}");
    }

    private static void BlobCodec_RoundTrip(FireThermalResult source)
    {
        byte[] blob = FireThermalBlobCodec.Pack(source);
        FireThermalResult restored = FireThermalBlobCodec.Unpack(blob);

        bool countOk = restored.Snapshots.Length == source.Snapshots.Length &&
                       restored.TimesMin.Length == source.TimesMin.Length;

        int snapIdx = Math.Min(1, source.Snapshots.Length - 1);
        int nodeIdx = Math.Min(0, source.Snapshots[snapIdx].Length - 1);
        double t0 = source.Snapshots[snapIdx][nodeIdx];
        double t1 = restored.Snapshots[snapIdx][nodeIdx];
        bool probeOk = Math.Abs(t0 - t1) < 1e-9;

        TestHarness.Check("FireThermalBlob_CountRoundTrip", countOk, $"srcSnap={source.Snapshots.Length}, dstSnap={restored.Snapshots.Length}");
        TestHarness.Check("FireThermalBlob_ProbeRoundTrip", probeOk, $"src={t0:F6}, dst={t1:F6}");
    }

    /// <summary>Короткий smoke-прогон: 1 мин, крупный шаг по времени (не parity).</summary>
    private static FireSectionDef CreateSmokeDef()
        => new()
        {
            FireDurationMin = 1.0,
            FireCurve = "iso834",
            MeshStepM = 0.05,
            TimeStepS = 30.0,
            SnapshotStepMin = 0.5,
            Theta = 1.0,
            PicardTolCelsius = 0.5,
            PicardMaxIter = 20,
            BcPreset = "3-sided",
            HoleBcPreset = "adiabatic",
            Algorithm = "ruppert",
            SmoothIterTri = 2,
            Edges = []
        };

    private static CrossSection CreateRectSection(double width, double height)
    {
        var hull = new Contour(
            new[] { 0.0, width, width, 0.0, 0.0 },
            new[] { 0.0, 0.0, height, height, 0.0 },
            "rect")
        { Type = ContourType.Hull };

        var area = new MaterialArea
        {
            Tag = "concrete",
            Category = AreaCategory.Region,
            Contours = [hull]
        };
        area.Hull = hull;

        return new CrossSection
        {
            Tag = "fire-rect-thermal",
            Areas = [area]
        };
    }
}
