using System.Text.Json;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Deflection;
using OpenCS.ViewModels;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class Sp63DeflectionResultViewModelTests
{
    [Fact]
    public void ViewModel_CalculatedResultShowsIndependentDeflectionVerdict()
    {
        var vm = new Sp63DeflectionResultVM(JsonSerializer.Serialize(new Sp63DeflectionResult
        {
            Status = Sp63DeflectionStatus.Calculated,
            Curvature = new Sp63CurvatureResult { Total = 0.002 },
            DeflectionMm = 18, DeflectionLimitMm = 20, Utilization = 0.9,
            DeflectionPassed = true, CoefficientS = 5.0 / 48.0, SpanM = 6
        }));

        Assert.Equal(Loc.S("Sp63Deflection_VerdictPassed"), vm.VerdictText);
        Assert.True(vm.HasDeflection);
        Assert.True(vm.HasVerdict);
    }

    [Theory]
    [InlineData(Sp63DeflectionStatus.NotApplicable)]
    [InlineData(Sp63DeflectionStatus.InvalidInput)]
    public void ViewModel_NonCalculatedResultHasNoPassFailVerdict(Sp63DeflectionStatus status)
    {
        var vm = new Sp63DeflectionResultVM(JsonSerializer.Serialize(new Sp63DeflectionResult
        {
            Status = status,
            DeflectionPassed = null
        }));

        Assert.False(vm.HasDeflection);
        Assert.False(vm.HasVerdict);
    }
}
