using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Армирование сечения проекта на весь КЭ по данным схемы: огибающая подбора, ТЗА, участок SCAD.</summary>
public sealed class ImportedBarRebarTests
{
    static readonly LiraBarProfile Profile = new(7, 0.3, 0.5, new Material { Id = 1 }, new Material { Id = 2 });

    static LiraAspBarAreas Areas(double au1 = 0, double as1 = 0, double as2 = 0, double asw1 = 0) =>
        new(au1, au1, 0, 0, as1, as2, 0, 0, 0, asw1, 0);

    static LiraAspBar AspBar(params LiraAspBarAreas[] sections) => new()
    {
        ElementId = 5, ConcreteClass = "B25", RebarClass = "A500", Covers = (4, 5, 3),
        Sections = [.. sections.Select(s => new LiraAspBarSection(s, Areas()))],
    };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(0.01, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.5000000001, 0.5)]
    [InlineData(2.26, 2.5)]
    public void CeilArea_UpToHalfCm2(double cm2, double expected) =>
        Assert.Equal(expected, ImportedBarRebar.CeilArea(cm2), 12);

    [Fact]
    public void LiraSelected_EnvelopeOverSections_RoundedUp()
    {
        // Сечение 1: углы снизу 1,2 см², низ 3,1; сечение 2: углы 0,4, низ 4,2, верх 0,3.
        var (layout, reason) = ImportedBarRebar.LiraSelected(AspBar(Areas(1.2, 3.1), Areas(0.4, 4.2, 0.3)), Profile);

        Assert.Null(reason);
        Assert.Equal("ASP", layout!.Label);
        // Углы 2 × 1,5 + низ 4,5 + верх 0,5 = 8,0 см².
        Assert.Equal(8.0, layout.TotalAreaCm2, 9);
        var corner = Assert.Single(layout.Bars, b => b.X < -0.11 && b.Y < -0.2);
        Assert.Equal(1.5e-4, corner.AreaM2, 12);
        Assert.Equal(-0.25 + 0.04, corner.Y, 12);   // a1 = 4 см
    }

    [Fact]
    public void LiraSelected_FailedSection_AndMissing_Reason()
    {
        Assert.Contains("сечении 2 не выполнен (код 274)",
            ImportedBarRebar.LiraSelected(AspBar(Areas(1, 2), Areas(asw1: -274)), Profile).Reason);
        Assert.Contains("ASP", ImportedBarRebar.LiraSelected(null, Profile).Reason);
        Assert.Contains("не требуется", ImportedBarRebar.LiraSelected(AspBar(Areas()), Profile).Reason);
    }

    [Fact]
    public void LiraAssigned_RowsOfTypes()
    {
        var rbt = new LiraRbtFile
        {
            BarTypes = new Dictionary<int, LiraBarReinforcementType>
            {
                [3] = new() { Id = 3, Face = LiraBarRebarFace.Bottom, Count = 3, DiameterMm = 16, BarAreaCm2 = 2.011, A = 4, ASide = 4 },
                [5] = new() { Id = 5, Face = LiraBarRebarFace.Top, Count = 2, DiameterMm = 12, BarAreaCm2 = 1.131, A = 4, ASide = 4 },
            },
        };

        var (layout, reason) = ImportedBarRebar.LiraAssigned(rbt, " 3,5 ", Profile);
        Assert.Null(reason);
        Assert.Equal("ТЗА 3,5", layout!.Label);
        Assert.Equal(5, layout.Bars.Count);
        Assert.Equal(3 * 2.011 + 2 * 1.131, layout.TotalAreaCm2, 9);

        Assert.Contains("RBT", ImportedBarRebar.LiraAssigned(null, "3", Profile).Reason);
        Assert.Contains("не назначены", ImportedBarRebar.LiraAssigned(rbt, "", Profile).Reason);
        Assert.Contains("ТЗА 9", ImportedBarRebar.LiraAssigned(rbt, "9", Profile).Reason);
    }

    static readonly ScadConcreteGroup Group =
        new(2, "ригели", 1, [0.04, 0.05, 0, 0], "B25", "A500", "A240", false, [0.4, 0.3], [5]);

    [Fact]
    public void ScadSelected_EnvelopeRoundedUp_CoversFromGroup()
    {
        var bar = new ScadSelectedBar(5,
        [
            new ScadSelectedBarSection(3.1, 0.2, 0, 0, null, null),
            new ScadSelectedBarSection(1.0, 2.6, 0.1, 0, null, null),
        ]);

        var (layout, reason) = ImportedBarRebar.ScadSelected(bar, Group, Profile);
        Assert.Null(reason);
        Assert.Equal("подбор SCAD", layout!.Label);
        Assert.Equal(3.5 + 3.0 + 0.5, layout.TotalAreaCm2, 9);
        Assert.All(layout.Bars.Where(b => b.Y < 0 && Math.Abs(b.X) < 0.11 && b.Y < -0.2),
            b => Assert.Equal(-0.25 + 0.04, b.Y, 12));

        Assert.Contains("сечении 2 не выполнен", ImportedBarRebar.ScadSelected(
            new ScadSelectedBar(5, [new ScadSelectedBarSection(1, 1, 0, 0, null, null), null]), Group, Profile).Reason);
        Assert.Contains("ЖБ-группах", ImportedBarRebar.ScadSelected(bar, null, Profile).Reason);
    }

    static ScadAssignedRodPart Part(int no, int n, int d) => new(no, 50,
        new ScadRodFace(new ScadBarSet(n, d), null, null, 0), new ScadRodFace(new ScadBarSet(2, 12), null, null, 0),
        null, null, null, null);

    [Fact]
    public void ScadAssigned_StrongestPart()
    {
        var rod = new ScadAssignedRod(4, "Ригели", [5], [Part(1, 2, 16), Part(2, 4, 20), Part(3, 2, 16)]);

        var (layout, reason) = ImportedBarRebar.ScadAssigned(rod, Group, Profile);
        Assert.Null(reason);
        Assert.Equal("SCAD «Ригели» уч.2", layout!.Label);
        Assert.Equal(4, layout.Bars.Count(b => b.DiameterM == 0.020));
        Assert.Same(rod.Parts[1], rod.Strongest);

        Assert.Contains("заданного армирования", ImportedBarRebar.ScadAssigned(null, Group, Profile).Reason);
    }
}
