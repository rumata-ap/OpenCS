using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

public class Sp16CheckerTests
{
    static Sp16Member Member() => Sp16Member.Create(
        new PolygonSection(TemplatePoints.IBeamPoints(.3, .15, .008, .012)),
        new SteelMaterialProps(240000, 360000, 245000, 370000, 2.06e8), new());

    [Theory]
    [InlineData(100, 0, 0, Sp16TaskKind.CentralTension, "7.1.1")]
    [InlineData(-100, 0, 0, Sp16TaskKind.CentralCompression, "7.1.3")]
    [InlineData(0, 10, 0, Sp16TaskKind.Bending, "8.2.1")]
    [InlineData(-100, 10, 0, Sp16TaskKind.CompressionBending, "9.1.1")]
    [InlineData(100, 10, 0, Sp16TaskKind.TensionBending, "9.1.1")]
    [InlineData(0, 0, 10, Sp16TaskKind.Shear, "8.2.1")]
    public void AutoRoutesByForces(double n, double mx, double qy, Sp16TaskKind kind, string clause)
    {
        var r = Sp16Checker.Run(Member(), new(n, mx, 0, 0, qy), Sp16TaskKind.Auto);
        Assert.Null(r.Error);
        Assert.Equal(kind, r.Kind);
        Assert.Contains(r.Results, x => x.Clause == clause);
    }

    [Fact]
    public void ExplicitCompressionRejectsTension()
    {
        var r = Sp16Checker.Run(Member(), new(100, 0, 0, 0, 0), Sp16TaskKind.CentralCompression);
        Assert.NotNull(r.Error);
        Assert.False(r.Passed);
        Assert.Empty(r.Results);
    }

    [Fact]
    public void NoPerformedChecksIsNotPassed()
    {
        Assert.False(new Sp16Report().Passed);
        Assert.False(new Sp16Report { Results = [Sp16CheckResult.NotApplicableFor("", "", "", "unsupported")] }.Passed);
    }

    [Fact]
    public void NonFiniteForcesAreRejected()
    {
        var r = Sp16Checker.Run(Member(), new(double.NaN, 0, 0, 0, 0), Sp16TaskKind.Auto);
        Assert.NotNull(r.Error);
        Assert.False(r.Passed);
    }

    [Fact]
    public void FemWorstDetailSkipsNotApplicableChecks()
    {
        var result = CScore.Fem.FemCheckRunner.ExtractWorstDetail("""
            {"details":[{"formula":"skipped","status":"NotApplicable","ratio":0},
            {"formula":"(5)","status":"Ok","ratio":0}]}
            """);
        Assert.Equal("(5)", result.formula);
    }
}
