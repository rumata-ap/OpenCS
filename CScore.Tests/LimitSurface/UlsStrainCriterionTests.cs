using CScore.LimitSurface;
using Xunit;

namespace CScore.Tests.LimitSurface;

public class UlsStrainCriterionTests
{
    [Fact]
    public void Rect_CollectsFourCornersAndTwoBars()
    {
        var section = TestSections.RectWithBottomRebar();
        var dom = new UlsStrainCriterion(CalcType.C).BuildDomain(section);

        var conc = dom.Points.Where(p => p.Kind == StrainPointKind.Concrete).ToList();
        var bars = dom.Points.Where(p => p.Kind == StrainPointKind.Rebar).ToList();
        Assert.Equal(4, conc.Count);   // замыкающая точка контура не дублируется
        Assert.Equal(2, bars.Count);

        var cc = section.Areas[0].Material!.GetChars(CalcType.C)!;
        Assert.All(conc, p => { Assert.Equal(cc.Ec0, p.EpsB0); Assert.Equal(cc.Ec2, p.EpsB2); });
        var rc = section.Areas[1].Material!.GetChars(CalcType.C)!;
        double epsSu = double.IsFinite(rc.Et2) && rc.Et2 > 0 ? rc.Et2 : 0.025;
        Assert.All(bars, p => Assert.Equal(epsSu, p.EpsMax, 12));
    }

    [Fact]
    public void Prestress_ShiftsRebarLimitByEpsP()
    {
        var section = TestSections.RectWithCentralPrestressedRebar(sigSp: 500.0);
        var dom = new UlsStrainCriterion(CalcType.C).BuildDomain(section);
        var strand = section.Areas[1];
        var rc = strand.Material!.GetChars(CalcType.C)!;
        double epsSu = double.IsFinite(rc.Et2) && rc.Et2 > 0 ? rc.Et2 : 0.025;
        var fib = strand.Fibers[0];
        Assert.True(fib.Eps_p > 0);
        var p = dom.Points.Single(q => q.Kind == StrainPointKind.Rebar && q.X == fib.X && q.Y == fib.Y);
        Assert.Equal(epsSu - fib.Eps_p, p.EpsMax, 12);
    }

    [Fact]
    public void NoConcrete_Throws()
    {
        var section = TestSections.RectWithBottomRebar();
        section.Areas.RemoveAt(0);
        Assert.Throws<ArgumentException>(() => new UlsStrainCriterion().BuildDomain(section));
    }
}
