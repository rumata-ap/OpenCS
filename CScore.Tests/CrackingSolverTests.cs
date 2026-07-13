using Xunit;
using CScore;

namespace CScore.Tests;

public class CrackingSolverTests
{
    [Fact]
    public void CrackingMoment_PureBending_Converges()
    {
        var section = TestSections.RectWithBottomRebar();
        var solver = new CrackingSolver(section, CalcType.CL);

        var res = solver.CrackingMoment(N: 0.0, Mx: 1.0, My: 0.0);

        Assert.True(res.Converged);
        Assert.True(res.Mx > 0);
        Assert.Equal(0.0, res.My, 6);
    }

    [Fact]
    public void CrackingMoment_GrowsWithSectionHeight()
    {
        var small = TestSections.RectWithBottomRebar(h: 0.3);
        var large = TestSections.RectWithBottomRebar(h: 0.6);

        var mcrcSmall = new CrackingSolver(small, CalcType.CL).CrackingMoment(0, 1, 0).Mx;
        var mcrcLarge = new CrackingSolver(large, CalcType.CL).CrackingMoment(0, 1, 0).Mx;

        Assert.True(mcrcLarge > mcrcSmall);
    }
}
