using CScore.Fem;
using CScore.Fem.Fragment;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentLoadFixtures;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Колонна C1 0…6 м режется полосой 1,5…4,5 и плитой на 3: «C1 · низ» = 1,5…3, «C1 · верх» = 3…4,5.</summary>
public sealed class StoreyFragmentBarLoadTests
{
    static FemMemberLoad One(StoreyFragmentDraft d, string part) => Assert.Single(d.MemberLoads, m => m.MemberTag == part).Load;

    static void Q(FemMemberLoad l, double s0, double e0, double qx0, double qx1)
    {
        Assert.Equal(s0, l.StartOffsetM, 9);
        Assert.Equal(e0, l.EndOffsetM, 9);
        Assert.Equal(qx0, l.QxStart, 6);
        Assert.Equal(qx1, l.QxEnd, 6);
    }

    [Fact]
    public void MemberTrapezoid_SplitAtSlab_ClippedByBand()
    {
        var d = Build(Snapshot(memberLoads:
            [new FemMemberLoad { LoadCaseId = 7, MemberId = 1, CoordinateSystem = "global", DistributionType = "trapezoidal", QxStart = 1000, QxEnd = 7000 }]));

        Assert.False(d.HasErrors);
        var low = One(d, "C1 · низ");
        Assert.Equal(("trapezoidal", "global", 7), (low.DistributionType, low.CoordinateSystem, low.LoadCaseId));
        Q(low, 0, 0, 2500, 4000);
        Q(One(d, "C1 · верх"), 0, 0, 4000, 5500);
    }

    [Fact]
    public void MemberUniformLocalWithOffset_AndPoint()
    {
        var d = Build(Snapshot(memberLoads:
        [
            new FemMemberLoad { LoadCaseId = 7, MemberId = 1, CoordinateSystem = "local", DistributionType = "uniform", StartOffsetM = 2, QyStart = 200, QyEnd = 200 },
            new FemMemberLoad { LoadCaseId = 7, MemberId = 1, CoordinateSystem = "global", DistributionType = "point", StartOffsetM = 4, QzStart = -500 },
        ]));

        var low = One(d, "C1 · низ");
        Assert.Equal(("uniform", "local", 200.0), (low.DistributionType, low.CoordinateSystem, low.QyStart));
        Assert.Equal(0.5, low.StartOffsetM, 9);
        Assert.Equal(0, low.EndOffsetM, 9);
        var up = d.MemberLoads.Where(m => m.MemberTag == "C1 · верх").Select(m => m.Load).ToList();
        Assert.Contains(up, l => l.DistributionType == "uniform" && l.CoordinateSystem == "local" && Math.Abs(l.StartOffsetM) < 1e-9 && Math.Abs(l.EndOffsetM) < 1e-9);
        var point = Assert.Single(up, l => l.DistributionType == "point");
        Assert.Equal(1.0, point.StartOffsetM, 9);
        Assert.Equal(-500, point.QzStart);
    }

    [Fact]
    public void ElementUniform_MergedPerPart_ElementTrapezoid_Mapped()
    {
        // КЭ колонны: 501 (0…1,5), 502 (1,5…3), 503 (3…4,5), 504 (4,5…6).
        var d = Build(Snapshot(loadCases: [Lc(7, "П"), Lc(8, "В")], elementLoads:
        [
            ElementLoad(7, "uniform", "x", [500], "501", "502", "503", "504"),
            ElementLoad(8, "trapezoid", "x", [0, 0, 1500, 1.5], "503"),
        ]));

        var low = One(d, "C1 · низ");
        Assert.Equal(("uniform", "global"), (low.DistributionType, low.CoordinateSystem));
        Q(low, 0, 0, 500, 500);
        Q(Assert.Single(d.MemberLoads, m => m.MemberTag == "C1 · верх" && m.Load.LoadCaseId == 7).Load, 0, 0, 500, 500);
        var up8 = Assert.Single(d.MemberLoads, m => m.MemberTag == "C1 · верх" && m.Load.LoadCaseId == 8).Load;
        Assert.Equal("trapezoidal", up8.DistributionType);
        Q(up8, 0, 0, 0, 1500);
        Assert.DoesNotContain(d.Diagnostics, x => x.Code == "fragment_load_lost");
    }

    [Fact]
    public void ElementLocalLoad_StaysLocal_WhenAxesMatch()
    {
        var load = ElementLoad(7, "uniform", "y", [300], "502");
        load.CoordinateSystem = "local";

        var low = One(Build(Snapshot(elementLoads: [load])), "C1 · низ");

        Assert.Equal(("local", 300.0, 0.0), (low.CoordinateSystem, low.QyStart, low.QxStart));
    }
}
