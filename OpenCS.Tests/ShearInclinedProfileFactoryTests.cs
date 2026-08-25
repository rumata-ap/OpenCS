using CScore;
using CScore.Sp63Shear;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сборка профиля усилий для задачи наклонных сечений.</summary>
public sealed class ShearInclinedProfileFactoryTests
{
    [Fact]
    public void Build_ConstantSource_UsesLoadItemValues()
    {
        var item = new LoadItem { N = -60.0, Mx = -140.0, Vy = 180.0 };
        var parameters = new ShearInclinedParams { ForceSource = "constant" };

        var result = ShearInclinedProfileFactory.Build(parameters, item, ShearPlane.Vy, null, null);

        Assert.Null(result.Error);
        Assert.Equal(180.0, result.Profile!.Q(0.0), 9);
        Assert.Equal(-140.0, result.Profile.M(0.0), 9);
        Assert.Equal(-60.0, result.Profile.N(0.0), 9);
    }

    [Fact]
    public void Build_ConstantSourceHorizontalPlane_TakesVxAndMy()
    {
        var item = new LoadItem { Vx = 75.0, My = 33.0 };
        var parameters = new ShearInclinedParams { ForceSource = "constant" };

        var result = ShearInclinedProfileFactory.Build(parameters, item, ShearPlane.Vx, null, null);

        Assert.Equal(75.0, result.Profile!.Q(0.0), 9);
        Assert.Equal(33.0, result.Profile.M(0.0), 9);
    }

    [Fact]
    public void Build_UniformLoad_UsesDistributedLoadAndSpan()
    {
        var item = new LoadItem { Vy = 120.0, Mx = 0.0 };
        var parameters = new ShearInclinedParams
        {
            ForceSource = "uniform_load",
            DistributedLoad = 30.0,
            DistanceToSupport = 4.0
        };

        var result = ShearInclinedProfileFactory.Build(parameters, item, ShearPlane.Vy, null, null);

        Assert.Equal(60.0, result.Profile!.Q(2.0), 9);
        Assert.Equal(4.0, result.Profile.Length, 9);
    }

    [Fact]
    public void Build_UniformLoadWithoutSpan_ReturnsError()
    {
        var parameters = new ShearInclinedParams
        {
            ForceSource = "uniform_load",
            DistributedLoad = 30.0,
            DistanceToSupport = 0.0
        };

        var result = ShearInclinedProfileFactory.Build(
            parameters, new LoadItem { Vy = 100.0 }, ShearPlane.Vy, null, null);

        Assert.NotNull(result.Error);
        Assert.Null(result.Profile);
    }

    [Fact]
    public void Build_UniformLoadWithoutFarSupport_DoesNotInventOne()
    {
        // Консоль: опора только в начале участка
        var parameters = new ShearInclinedParams
        {
            ForceSource = "uniform_load",
            DistributedLoad = 30.0,
            DistanceToSupport = 4.0,
            SupportAtEnd = false
        };

        var result = ShearInclinedProfileFactory.Build(
            parameters, new LoadItem { Vy = 120.0 }, ShearPlane.Vy, null, null);

        Assert.False(result.Profile!.HasSupport(+1));
        Assert.True(result.Profile.HasSupport(-1));
    }

    [Fact]
    public void Build_FemProfileWithManualForceSet_ReturnsError()
    {
        var parameters = new ShearInclinedParams { ForceSource = "fem_profile" };
        var manualSet = new ForceSet { Id = 3, Kind = "bar", SourceType = null };

        var result = ShearInclinedProfileFactory.Build(
            parameters, new LoadItem { Vy = 100.0 }, ShearPlane.Vy, manualSet, null);

        Assert.NotNull(result.Error);
        Assert.Contains("FEM", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_FemProfileWithoutDatabase_ReturnsError()
    {
        var parameters = new ShearInclinedParams { ForceSource = "fem_profile" };
        var femSet = new ForceSet
        {
            Id = 3, Kind = "bar", SourceType = "fea", SourceSchemaId = 1, SourceMemberId = 2
        };

        var result = ShearInclinedProfileFactory.Build(
            parameters, new LoadItem { Vy = 100.0 }, ShearPlane.Vy, femSet, null);

        Assert.NotNull(result.Error);
    }

    [Fact]
    public void FromSamples_BuildsMonotonicSampledProfile()
    {
        var rows = new List<(double S, double Q, double M, double N)>
        {
            (2.0, 60.0, 180.0, -10.0),
            (0.0, 120.0, 0.0, -10.0),
            (4.0, 0.0, 240.0, -10.0)
        };

        var profile = ShearInclinedProfileFactory.FromSamples(
            rows.Select(r => new ForceSample(r.S, r.Q, r.M, r.N)).ToList());

        Assert.Equal(0.0, profile.Samples[0].S, 12);
        Assert.Equal(4.0, profile.Samples[^1].S, 12);
        Assert.Equal(4.0, profile.Length, 12);
    }
}
