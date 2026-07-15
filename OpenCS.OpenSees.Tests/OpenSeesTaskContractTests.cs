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
}
