using CScore;
using OpenCS.Tasks;

namespace OpenCS.OpenSees.Tests;

public sealed class OpenSeesSpatialTaskContractTests
{
    [Fact]
    public void ParamsJson_contract_parses_spatial_options()
    {
        OpenSeesSpatialInteractionParams parameters = OpenSeesSpatialInteractionParams.Parse(
            "{\"angleStepDegrees\":45,\"maxCurvature\":0.01,\"increments\":20,\"timeoutSeconds\":300,\"executablePath\":\"C:/OpenSees.exe\"}");

        Assert.Equal(45, parameters.AngleStepDegrees);
        Assert.Equal(0.01, parameters.MaxCurvature);
        Assert.Equal(20, parameters.Increments);
        Assert.Equal(300, parameters.TimeoutSeconds);
        Assert.Equal("C:/OpenSees.exe", parameters.ExecutablePath);
    }

    [Fact]
    public void ParamsJson_contract_uses_safe_defaults_and_rejects_invalid_values()
    {
        OpenSeesSpatialInteractionParams defaults = OpenSeesSpatialInteractionParams.Parse("{}");

        Assert.Equal(45, defaults.AngleStepDegrees);
        Assert.True(defaults.MaxCurvature > 0);
        Assert.True(defaults.Increments > 0);
        Assert.True(defaults.TimeoutSeconds > 0);

        Assert.Throws<ArgumentException>(() => OpenSeesSpatialInteractionParams.Parse("{\"angleStepDegrees\":7}"));
        Assert.Throws<ArgumentException>(() => OpenSeesSpatialInteractionParams.Parse("{\"angleStepDegrees\":0}"));
        Assert.Throws<ArgumentException>(() => OpenSeesSpatialInteractionParams.Parse("{\"maxCurvature\":0}"));
        Assert.Throws<ArgumentException>(() => OpenSeesSpatialInteractionParams.Parse("{\"increments\":0}"));
        Assert.Throws<ArgumentException>(() => OpenSeesSpatialInteractionParams.Parse("{\"timeoutSeconds\":-1}"));
    }

    [Fact]
    public void TaskRunner_registers_the_spatial_OpenSees_kind()
    {
        Assert.Contains("opensees_section_interaction_n_mx_my", TaskRunner.KindList);
    }

    [Fact]
    public void ForceSet_resolver_preserves_first_seen_unique_axial_forces_and_converts_to_newtons()
    {
        ForceSet forceSet = new()
        {
            Kind = "bar",
            Items =
            [
                new LoadItem { N = -1000 },
                new LoadItem { N = 0 },
                new LoadItem { N = 1000 },
                new LoadItem { N = 0 }
            ]
        };

        IReadOnlyList<double> forcesKn = OpenSeesSpatialInteractionParams.ExtractAxialForcesKn(forceSet);
        double[] forcesN = forcesKn.Select(force => force * 1000).ToArray();

        Assert.Equal(new[] { -1000d, 0d, 1000d }, forcesKn);
        Assert.Equal(new[] { -1_000_000d, 0d, 1_000_000d }, forcesN);
    }

    [Fact]
    public void ForceSet_resolver_rejects_empty_or_nonfinite_axial_values()
    {
        Assert.Throws<ArgumentException>(() => OpenSeesSpatialInteractionParams.ExtractAxialForcesKn(
            new ForceSet { Kind = "bar", Items = [] }));
        Assert.Throws<ArgumentException>(() => OpenSeesSpatialInteractionParams.ExtractAxialForcesKn(
            new ForceSet { Kind = "bar", Items = [new LoadItem { N = double.NaN }] }));
    }

    [Fact]
    public void ParamsJson_serialization_contains_only_spatial_analysis_fields()
    {
        string json = new OpenSeesSpatialInteractionParams
        {
            AngleStepDegrees = 90,
            MaxCurvature = 0.02,
            Increments = 40,
            TimeoutSeconds = 90,
            ExecutablePath = "C:/OpenSees.exe"
        }.ToJson();

        Assert.Contains("\"angleStepDegrees\":90", json);
        Assert.Contains("\"maxCurvature\":0.02", json);
        Assert.DoesNotContain("axialForces", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("forceSet", json, StringComparison.OrdinalIgnoreCase);
    }
}
