using System.Globalization;
using CScore;
using CScore.Fire;
using CScore.Fire.Entities;

namespace CSfea.Tests;

/// <summary>Канонизация и чувствительность снимка входных данных теплового расчёта.</summary>
public static class FireInputSnapshotTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireThermalInputSnapshot: идентичность входа");
        Hash_IsStableForSameInput();
        Hash_IgnoresEdgeOrder();
        Hash_IgnoresTagAndNum();
        Hash_ChangesWithFireCurve();
        Hash_ChangesWithMeshStep();
        Hash_ChangesWithGeometry();
        Hash_ChangesWithHull();
        Hash_ChangesWithEffectiveAggregate();
        Hash_IsCultureIndependent();
        NaN_IsSerializedSafely();
        FirstDifference_NamesTheChangedGroup();
    }

    static FireSectionDef Def() => new()
    {
        Id = 1,
        Num = 1,
        Tag = "Огневое 1",
        SectionId = 1,
        FireDurationMin = 60,
        FireCurve = "iso834",
        MeshStepM = 0.02,
        TimeStepS = 5,
        Edges =
        [
            new FireBoundaryEdgeDef { EdgeIndex = 0, BcType = "fire", ContourType = "outer" },
            new FireBoundaryEdgeDef { EdgeIndex = 1, BcType = "ambient", ContourType = "outer" }
        ]
    };

    static CrossSection Section() => FireFiberSectionTests.CreateSectionForTests();

    static void Hash_IsStableForSameInput()
    {
        var a = FireThermalInputSnapshot.Build(Def(), Section(), "silicate");
        var b = FireThermalInputSnapshot.Build(Def(), Section(), "silicate");
        TestHarness.Check("FireInputSnapshot_Stable", a.Hash == b.Hash, $"hash={a.Hash}");
    }

    static void Hash_IgnoresEdgeOrder()
    {
        var d1 = Def();
        var d2 = Def();
        d2.Edges.Reverse();

        var a = FireThermalInputSnapshot.Build(d1, Section(), "silicate");
        var b = FireThermalInputSnapshot.Build(d2, Section(), "silicate");
        TestHarness.Check("FireInputSnapshot_EdgeOrderIgnored", a.Hash == b.Hash);
    }

    static void Hash_IgnoresTagAndNum()
    {
        var d = Def();
        d.Tag = "Переименовано";
        d.Num = 42;

        var a = FireThermalInputSnapshot.Build(Def(), Section(), "silicate");
        var b = FireThermalInputSnapshot.Build(d, Section(), "silicate");
        TestHarness.Check("FireInputSnapshot_TagAndNumIgnored", a.Hash == b.Hash);
    }

    static void Hash_ChangesWithFireCurve()
    {
        var d = Def();
        d.FireCurve = "hydrocarbon";

        var a = FireThermalInputSnapshot.Build(Def(), Section(), "silicate");
        var b = FireThermalInputSnapshot.Build(d, Section(), "silicate");
        TestHarness.Check("FireInputSnapshot_FireCurveMatters", a.Hash != b.Hash);
    }

    static void Hash_ChangesWithMeshStep()
    {
        var d = Def();
        d.MeshStepM = 0.015;

        var a = FireThermalInputSnapshot.Build(Def(), Section(), "silicate");
        var b = FireThermalInputSnapshot.Build(d, Section(), "silicate");
        TestHarness.Check("FireInputSnapshot_MeshStepMatters", a.Hash != b.Hash);
    }

    static void Hash_ChangesWithGeometry()
    {
        var s = Section();
        foreach (var area in s.Areas)
            foreach (var f in area.Fibers)
                if (f.TypeFiber == FiberType.point) { f.X += 0.01; break; }

        var a = FireThermalInputSnapshot.Build(Def(), Section(), "silicate");
        var b = FireThermalInputSnapshot.Build(Def(), s, "silicate");
        TestHarness.Check("FireInputSnapshot_GeometryMatters", a.Hash != b.Hash);
    }

    static void Hash_ChangesWithHull()
    {
        var s = Section();
        s.Areas[0].Hull!.X[0] += 0.01;
        // WKT намеренно не обновляем: снимок должен зависеть от геометрии,
        // которую фактически использует FireMeshBuilder.

        var a = FireThermalInputSnapshot.Build(Def(), Section(), "silicate");
        var b = FireThermalInputSnapshot.Build(Def(), s, "silicate");
        TestHarness.Check("FireInputSnapshot_HullMatters", a.Hash != b.Hash);
    }

    static void Hash_ChangesWithEffectiveAggregate()
    {
        var a = FireThermalInputSnapshot.Build(Def(), Section(), "silicate");
        var b = FireThermalInputSnapshot.Build(Def(), Section(), "carbonate");
        TestHarness.Check("FireInputSnapshot_AggregateMatters", a.Hash != b.Hash);
    }

    static void Hash_IsCultureIndependent()
    {
        CultureInfo old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var ru = FireThermalInputSnapshot.Build(Def(), Section(), "silicate");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var en = FireThermalInputSnapshot.Build(Def(), Section(), "silicate");
            TestHarness.Check("FireInputSnapshot_CultureIndependent", ru.Hash == en.Hash);
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    static void NaN_IsSerializedSafely()
    {
        var s = Section();
        s.Areas[^1].Fibers[0].X = double.NaN;
        var input = FireThermalInputSnapshot.Build(Def(), s, "silicate");
        TestHarness.Check("FireInputSnapshot_NaNIsStable",
            input.Json.Contains("NaN", StringComparison.Ordinal) && input.Hash.Length == 16,
            $"hash={input.Hash}");
    }

    static void FirstDifference_NamesTheChangedGroup()
    {
        var d = Def();
        d.MeshStepM = 0.015;

        var a = FireThermalInputSnapshot.Build(Def(), Section(), "silicate");
        var b = FireThermalInputSnapshot.Build(d, Section(), "silicate");

        string? reason = FireThermalInputSnapshot.FirstDifference(a.Json, b.Json);
        TestHarness.Check("FireInputSnapshot_ReasonIsMesh",
            reason == "FireStale_Mesh", $"reason={reason}");
    }
}
