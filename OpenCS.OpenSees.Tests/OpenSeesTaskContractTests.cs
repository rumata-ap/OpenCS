using System.Text.Json;
using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.CScore;
using OpenCS.Tasks;

namespace OpenCS.OpenSees.Tests;

public sealed class OpenSeesTaskContractTests
{
    [Fact]
    public void ParamsJson_contract_parses_moment_curvature_options()
    {
        OpenSeesSectionParams parameters = OpenSeesSectionParams.Parse(
            "{\"maxCurvature\":0.02,\"increments\":40,\"axis\":\"My\",\"timeoutSeconds\":90,\"executablePath\":\"C:/OpenSees.exe\"}");

        Assert.Equal(0.02, parameters.MaxCurvature);
        Assert.Equal(40, parameters.Increments);
        Assert.Equal("My", parameters.Axis);
        Assert.Equal(90, parameters.TimeoutSeconds);
        Assert.Equal("C:/OpenSees.exe", parameters.ExecutablePath);
    }

    [Fact]
    public void ParamsJson_contract_uses_defaults_and_rejects_nonpositive_values()
    {
        OpenSeesSectionParams defaults = OpenSeesSectionParams.Parse("{}");

        Assert.True(defaults.MaxCurvature > 0);
        Assert.True(defaults.Increments > 0);
        Assert.True(defaults.TimeoutSeconds > 0);

        Assert.Throws<ArgumentException>(() => OpenSeesSectionParams.Parse("{\"increments\":0}"));
        Assert.Throws<ArgumentException>(() => OpenSeesSectionParams.Parse("{\"timeoutSeconds\":-1}"));
    }

    [Fact]
    public void TaskRunner_registers_the_OpenSees_kind()
    {
        Assert.Contains("opensees_section_moment_curvature", TaskRunner.KindList);
    }

    [Fact]
    public void ParamsJson_contract_parses_N_M_options_in_kilonewtons()
    {
        OpenSeesSectionInteractionParams parameters = OpenSeesSectionInteractionParams.Parse(
            "{\"axialForces\":[-1000,0,1000],\"maxCurvature\":0.02,\"increments\":40,\"axis\":\"My\",\"timeoutSeconds\":90,\"executablePath\":\"C:/OpenSees.exe\"}");

        Assert.Equal(new[] { -1000d, 0d, 1000d }, parameters.AxialForcesKn);
        Assert.Equal(0.02, parameters.MaxCurvature);
        Assert.Equal(40, parameters.Increments);
        Assert.Equal("My", parameters.Axis);
        Assert.Equal(90, parameters.TimeoutSeconds);
        Assert.Equal("C:/OpenSees.exe", parameters.ExecutablePath);
    }

    [Fact]
    public void Interaction_params_use_safe_defaults_and_reject_invalid_lists()
    {
        OpenSeesSectionInteractionParams defaults = OpenSeesSectionInteractionParams.Parse("{}");

        Assert.Equal(new[] { 0d }, defaults.AxialForcesKn);
        Assert.True(defaults.MaxCurvature > 0);
        Assert.True(defaults.Increments > 0);
        Assert.True(defaults.TimeoutSeconds > 0);

        Assert.Throws<ArgumentException>(() => OpenSeesSectionInteractionParams.Parse("{\"axialForces\":[]}"));
        Assert.Throws<ArgumentException>(() => OpenSeesSectionInteractionParams.Parse("{\"axialForces\":[0,0]}"));
        Assert.Throws<ArgumentException>(() => OpenSeesSectionInteractionParams.Parse("{\"increments\":0}"));
        Assert.Throws<ArgumentException>(() => OpenSeesSectionInteractionParams.Parse("{\"timeoutSeconds\":-1}"));
        Assert.Throws<ArgumentException>(() => OpenSeesSectionInteractionParams.Parse("{\"axis\":\"Mz\"}"));
    }

    [Fact]
    public void Interaction_params_convert_kilonewtons_to_newtons_and_serialize_result()
    {
        OpenSeesSectionInteractionParams parameters = OpenSeesSectionInteractionParams.Parse(
            "{\"axialForces\":[-1000,0,1000]}");
        double[] axialForcesN = parameters.AxialForcesKn
            .Select(CScoreUnitConverter.KiloNewtonsToNewtons)
            .ToArray();

        Assert.Equal(new[] { -1_000_000d, 0d, 1_000_000d }, axialForcesN);

        string jsonResult = JsonSerializer.Serialize(new SectionInteractionResult
        {
            Status = "ok",
            Points = [new SectionInteractionPoint { AxialForceN = 1_000, BendingMomentNm = 2_000 }]
        });

        Assert.Contains("\"AxialForceN\":1000", jsonResult);
    }
}
