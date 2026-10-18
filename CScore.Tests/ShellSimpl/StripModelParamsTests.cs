using CScore.Fem;
using Xunit;

namespace CScore.Tests.ShellSimpl;

/// <summary>Модель полосы в параметрах проверки по КЭ: старые записи — формулы.</summary>
public sealed class StripModelParamsTests
{
    [Fact]
    public void PlateCheckParams_WithoutField_IsFormulas()
    {
        var p = PlateCheckParams.Parse("{\"Kind\":\"shell_simpl_wa_uls\",\"StepDeg\":10}");
        Assert.Equal(StripModel.Formulas, p.StripModel);
    }

    [Fact]
    public void PlateCheckParams_RoundTrip_KeepsNdm()
    {
        var json = new PlateCheckParams { Kind = "shell_simpl_capri_sls", StripModel = StripModel.Ndm }.ToJson();
        Assert.Equal(StripModel.Ndm, PlateCheckParams.Parse(json).StripModel);
    }
}
