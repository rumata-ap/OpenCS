using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>Область поперечного армирования не должна влиять на продольное НДС.</summary>
public sealed class StirrupCalcExclusionTests
{
    [Fact]
    public void Integral_IsUnchangedByStirrupArea()
    {
        var k = new Kurvature { e0 = -0.0005, ky = 0.002, kz = 0.0 };

        var without = TestSections.RectWithBottomRebar();
        var with = TestSections.RectWithBottomRebar();
        with.Areas.Add(StirrupArea());
        with.ResolveAndBuildDiagramms();
        without.ResolveAndBuildDiagramms();

        var a = without.Integral(k, CalcType.C);
        var b = with.Integral(k, CalcType.C);

        Assert.Equal(a.N, b.N, 9);
        Assert.Equal(a.Mx, b.Mx, 9);
        Assert.Equal(a.My, b.My, 9);
    }

    [Fact]
    public void EnumerateAreas_SkipsStirrupArea()
    {
        var section = TestSections.RectWithBottomRebar();
        int before = section.EnumerateAreas(new Kurvature()).Count();
        section.Areas.Add(StirrupArea());

        Assert.Equal(before, section.EnumerateAreas(new Kurvature()).Count());
    }

    [Fact]
    public void TwoStageSection_EnumerateAreas_SkipsStirrupAreaInBothStages()
    {
        var section = TestSections.TwoStageRectWithFlange();
        int before = section.EnumerateAreas(new Kurvature()).Count();
        section.Stage1.Areas.Add(StirrupArea());
        section.Areas.Add(StirrupArea());

        Assert.Equal(before, section.EnumerateAreas(new Kurvature()).Count());
    }

    static MaterialArea StirrupArea() => new()
    {
        Id = 99,
        Tag = "хомуты",
        Category = AreaCategory.Stirrups,
        MaterialId = 17
    };
}
