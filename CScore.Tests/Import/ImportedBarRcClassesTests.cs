using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Классы бетона и арматуры стержневых КЭ: ЛИРА — из подбора ASP, SCAD — из ЖБ-групп.</summary>
public class ImportedBarRcClassesTests
{
    static readonly LiraAspFile Asp = new()
    {
        Bars = new Dictionary<int, LiraAspBar>
        {
            [5] = new() { ElementId = 5, ConcreteClass = "B25", RebarClass = "A500" },
        },
    };

    static readonly ScadConcreteGroupIndex Groups = new(
    [
        new ScadConcreteGroup(1, "Балки", 0, [0.04, 0.05], "B30", "A400", "A240", false, [], [7, 8]),
    ]);

    [Fact]
    public void Lira_FromAsp()
    {
        var lookup = ImportedBarRcClasses.Lookup(scad: false, Asp, Groups);
        Assert.Equal(new ImportedBarRcClasses("B25", "A500"), lookup(5));
        Assert.Null(lookup(7));
        Assert.True(ImportedBarRcClasses.Available(false, Asp, null));
        Assert.False(ImportedBarRcClasses.Available(false, null, Groups));
    }

    [Fact]
    public void Scad_FromConcreteGroup()
    {
        var lookup = ImportedBarRcClasses.Lookup(scad: true, Asp, Groups);
        Assert.Equal(new ImportedBarRcClasses("B30", "A400"), lookup(8));
        Assert.Null(lookup(5));
        Assert.True(ImportedBarRcClasses.Available(true, null, Groups));
        Assert.False(ImportedBarRcClasses.Available(true, Asp, null));
    }

    [Fact]
    public void NoData_Null()
    {
        Assert.Null(ImportedBarRcClasses.Lookup(false, null, null)(5));
        Assert.Null(ImportedBarRcClasses.Lookup(true, null, null)(8));
    }
}
