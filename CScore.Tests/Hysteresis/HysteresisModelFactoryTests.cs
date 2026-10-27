using CScore.Hysteresis;
using Xunit;

namespace CScore.Tests.Hysteresis;

public class HysteresisModelFactoryTests
{
    static MaterialArea Bars(Diagramm? host = null)
    {
        var a500 = TestMaterials.Rebar("A500");
        var area = new MaterialArea
        {
            Category = AreaCategory.RebarGroup, Material = a500, MaterialId = a500.Id, DiagrammType = DiagrammType.L2,
            Fibers = [Fiber.CreatePoint(0.02, 0, -0.2)],
        };
        area.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return area;
    }

    [Fact]
    public void SteelParams_FromL2Diagram()
    {
        var d = Bars().Diagramms[CalcType.C];
        var (e, fyT, fyC, b) = HysteresisModelFactory.SteelParams(d);
        Assert.Equal(d.It.Y[1] / d.It.X[1], e, 1);
        Assert.Equal(d.It.Y[1], fyT, 1);
        Assert.Equal(-d.Ic.Y[^2], fyC, 1);
        Assert.InRange(b, 0, 1e-6);
    }

    [Fact]
    public void Create_Rebar_MenegottoPintoByDefault_RuptureWhenAsked()
    {
        var area = Bars();
        var m = HysteresisModelFactory.Create(area, CalcType.C, new HysteresisOptions(), null, 0.0005);
        Assert.IsType<MenegottoPintoSteel>(m.Main);
        Assert.False(m.IsConcrete);
        Assert.Null(m.Host);
        var r = HysteresisModelFactory.Create(area, CalcType.C, new HysteresisOptions { Steel = SteelHysteresisModel.Bilinear }, 0.025, 0.0005);
        Assert.IsType<RuptureDamage>(r.Main);
    }

    [Fact]
    public void Create_Concrete_KarsanJirsa()
    {
        var c = new MaterialArea
        {
            Category = AreaCategory.Region, Material = TestMaterials.Concrete("B25"), DiagrammType = DiagrammType.L3,
            Hull = new Contour([-0.1, 0.1, 0.1, -0.1, -0.1], [-0.1, -0.1, 0.1, 0.1, -0.1], "h"),
        };
        c.MaterialId = c.Material.Id; c.SetWKT();
        c.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        var m = HysteresisModelFactory.Create(c, CalcType.C, new HysteresisOptions(), null, 0.0005);
        Assert.IsType<KarsanJirsaConcrete>(m.Main);
        Assert.True(m.IsConcrete);
    }

    [Fact]
    public void Rupture_IsIrreversible()
    {
        var inner = new BilinearSteel(2e8, 400000, 400000, 0.01);
        var m = new RuptureDamage(inner, 0.01, 0.002);
        var s = m.Initial;
        m.Trial(s, 0.0125, true, out _, out s);                 // за пределом и за спадом
        Assert.Equal(0.0, m.Trial(s, 0.005, true, out _, out _), 12);
        Assert.Equal(0.0, m.Trial(s, -0.005, true, out _, out _), 12);
        var s2 = m.Initial;
        double half = m.Trial(s2, 0.011, true, out _, out s2);   // середина спада
        Assert.Equal(0.5 * inner.Trial(inner.Initial, 0.011, true, out _, out _), half, 1);
    }
}
