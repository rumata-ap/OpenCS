using Xunit;

namespace CScore.Tests.History;

public class StagedSectionTests
{
    [Fact]
    public void Initially_OnlyPartZeroIsActive()
    {
        var s = StagedTestSections.TwoPartBeam();
        Assert.Equal(1, s.ActivePartCount);
        var areas = s.EnumerateAreas(new Kurvature()).Select(p => p.area.Id).ToList();
        Assert.Equal([1, 2], areas);
        Assert.Equal(4, s.Areas.Count); // геометрия — все части
    }

    [Fact]
    public void ActivatedPart_SeesPlaneRelativeToActivation()
    {
        var s = StagedTestSections.TwoPartBeam();
        var k1 = new Kurvature { e0 = -1e-4, ky = 2e-4, kz = 0 };
        s.ActivatePart(1, k1);
        var k = new Kurvature { e0 = -3e-4, ky = 5e-4, kz = 1e-5 };
        var pairs = s.EnumerateAreas(k).ToDictionary(p => p.area.Id, p => p.k);
        Assert.Equal(k.e0, pairs[1].e0, 15);
        Assert.Equal(k.e0 - k1.e0, pairs[3].e0, 15);
        Assert.Equal(k.ky - k1.ky, pairs[4].ky, 15);
        Assert.Equal(k.kz - k1.kz, pairs[4].kz, 15);
    }

    [Fact]
    public void ActivatePart_OutOfOrder_Throws()
    {
        var s = StagedTestSections.TwoPartBeam();
        Assert.Throws<InvalidOperationException>(() => s.ActivatePart(0, new Kurvature()));
        s.ActivatePart(1, new Kurvature());
        Assert.Throws<InvalidOperationException>(() => s.ActivatePart(1, new Kurvature()));
    }

    [Fact]
    public void ResetHistory_ReturnsToPartZero()
    {
        var s = StagedTestSections.TwoPartBeam();
        s.ActivatePart(1, new Kurvature { e0 = 1e-4 });
        s.ResetHistory();
        Assert.Equal(1, s.ActivePartCount);
        Assert.Equal(0.0, s.ActivationPlanes[1].e0);
    }

    [Fact]
    public void RebarHost_FromAnotherPart_IsNotUsedForDifferentialDiagram()
    {
        var s = StagedTestSections.TwoPartBeam();
        var bars1 = s.Parts[1].Areas[1];
        bars1.HostAreaId = 1; // бетон части 0 — чужая часть
        s.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: true);
        Assert.Null(bars1.HostArea);
        Assert.Null(bars1.Diagramms[CalcType.C].Own); // не разностная
    }

    [Fact]
    public void CloneForCalc_CopiesPartsAndHistory()
    {
        var s = StagedTestSections.TwoPartBeam();
        s.ActivatePart(1, new Kurvature { ky = 1e-4 });
        var c = (StagedSection)s.CloneForCalc();
        Assert.Equal(2, c.Parts.Count);
        Assert.Equal(2, c.ActivePartCount);
        Assert.Equal(1e-4, c.ActivationPlanes[1].ky);
        Assert.NotSame(s.Parts[0].Areas[0], c.Parts[0].Areas[0]);
        Assert.Equal(4, c.Areas.Count);
    }
}
