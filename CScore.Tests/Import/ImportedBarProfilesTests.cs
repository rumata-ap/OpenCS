using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Профили стержней по жёсткостям схемы-источника, нейтральные к программе.</summary>
public class ImportedBarProfilesTests
{
    static Dictionary<int, LiraStiffnessRecord> Stiffnesses(params LiraStiffnessRecord[] records) =>
        records.ToDictionary(r => r.Id);

    static LiraStiffnessRecord LiraBar(int num = 7) =>
        new(num, LiraStiffnessParams.BarRectKind, "Брус 30 X 50", "Ro:2.5 E:3e+06 B:30 H:50 BAR_END", 0.01);

    static LiraStiffnessRecord ScadBar(int num, string body, string name = "Колонны") =>
        new(num, ScadStiffnessParams.ScadKindCode, name, body, 0.01);

    [Fact]
    public void Lira_BarRect_ConcreteRectangleInMeters()
    {
        var (profile, reason) = ImportedBarProfiles.Resolve(Stiffnesses(LiraBar()), 7, scad: false);

        Assert.Null(reason);
        Assert.Equal(new ImportedBarProfile(7, ImportedBarMaterial.Concrete, ImportedBarShape.Rectangle,
            0.3, 0.5, "Брус 30 X 50"), profile);
    }

    [Fact]
    public void Scad_S0_ConcreteRectangleInMeters()
    {
        var (profile, reason) = ImportedBarProfiles.Resolve(
            Stiffnesses(ScadBar(6, "S0 900000 40 60 NU 0.2")), 6, scad: true);

        Assert.Null(reason);
        Assert.Equal(ImportedBarMaterial.Concrete, profile!.Material);
        Assert.Equal(ImportedBarShape.Rectangle, profile.Shape);
        Assert.Equal(0.4, profile.WidthM);
        Assert.Equal(0.6, profile.HeightM);
        Assert.Equal("Колонны", profile.SourceLabel);
    }

    [Fact]
    public void Scad_Stz_ShapeNotSupported()
    {
        var (profile, reason) = ImportedBarProfiles.Resolve(
            Stiffnesses(ScadBar(4, "STZ RUSSIAN pu_typep 13", "Швеллер")), 4, scad: true);

        Assert.Null(profile);
        Assert.Contains("форма сечения не поддерживается", reason);
        Assert.Contains("S0", reason);
    }

    [Fact]
    public void Lira_NotBar_ShapeNotSupported()
    {
        var (profile, reason) = ImportedBarProfiles.Resolve(
            Stiffnesses(LiraBar() with { KindCode = 11, Name = "Тавр" }), 7, scad: false);

        Assert.Null(profile);
        Assert.Contains("«Тавр»: форма сечения не поддерживается (только «Брус»)", reason);
    }

    [Fact]
    public void Lira_RecordOfScadIsNotBar()
    {
        // Запись SCAD в схеме ЛИРЫ не разбирается как брус ЛИРЫ — и наоборот.
        Assert.NotNull(ImportedBarProfiles.Resolve(Stiffnesses(ScadBar(6, "S0 900000 40 60")), 6, scad: false).Reason);
        Assert.NotNull(ImportedBarProfiles.Resolve(Stiffnesses(LiraBar(6)), 6, scad: true).Reason);
    }

    [Theory]
    [InlineData(false, "ЛИРЫ (API)")]
    [InlineData(true, null)]
    public void NoStiffnessNumber_OrMissingStiffness_Reason(bool scad, string? hint)
    {
        var none = ImportedBarProfiles.Resolve(Stiffnesses(LiraBar()), null, scad);
        var missing = ImportedBarProfiles.Resolve(Stiffnesses(LiraBar()), 99, scad);

        Assert.Null(none.Profile);
        Assert.StartsWith("у КЭ нет номера жёсткости", none.Reason);
        Assert.Null(missing.Profile);
        Assert.StartsWith("жёсткости 99 нет среди жёсткостей схемы", missing.Reason);
        if (hint != null) Assert.Contains(hint, missing.Reason);
        else Assert.DoesNotContain("ЛИРЫ", missing.Reason);
    }
}
