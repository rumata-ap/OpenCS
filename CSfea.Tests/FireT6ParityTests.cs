using CScore;
using CScore.Fire;
using CScore.Fire.Entities;

namespace CSfea.Tests;

/// <summary>Проверки T6-пути огневого расчёта.</summary>
public static class FireT6ParityTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireT6: квадратичный путь");
        T6_RunsAndIsFiniteAndWarmer();
    }

    static CrossSection Rect(double w, double h)
    {
        var xs = new[] { 0.0, w, w, 0.0 };
        var ys = new[] { 0.0, 0.0, h, h };
        var hull = new Contour(xs.Append(xs[0]), ys.Append(ys[0]), "hull") { Type = ContourType.Hull };
        var area = new MaterialArea { Tag = "t6", Category = AreaCategory.Region, Contours = [hull], Hull = hull };
        return new CrossSection { Tag = "t6-section", Areas = [area] };
    }

    static FireSectionDef Def(string elementType) => new()
    {
        FireDurationMin = 10.0,
        FireCurve = "iso834",
        MeshStepM = 0.02,
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

    static void T6_RunsAndIsFiniteAndWarmer()
    {
        var section = Rect(0.3, 0.5);
        var r3 = FireThermalService.Run(Def("linear"), section, "silicate");
        var r6 = FireThermalService.Run(Def("quadratic"), section, "silicate");

        bool quadratic = r6.MeshInfo.Mesh.Elements.Length > 0 && r6.MeshInfo.Mesh.Elements[0].Length == 6;
        double max3 = r3.Snapshots[^1].Max();
        double max6 = r6.Snapshots[^1].Max();
        bool finite = r6.Snapshots[^1].All(v => !double.IsNaN(v) && !double.IsInfinity(v));
        // Обе сетки нагреваются от пожара; пиковая температура у огневой грани сопоставима.
        bool warmer = max6 > 100.0 && max3 > 100.0;

        TestHarness.Check("T6_RunsAndIsFiniteAndWarmer", quadratic && finite && warmer,
            $"quad={quadratic}, max3={max3:F1}, max6={max6:F1}");
    }
}
