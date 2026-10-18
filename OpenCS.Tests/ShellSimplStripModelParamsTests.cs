using CScore;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Поле strip_model задач shell_simpl_*: без поля — формулы, "ndm" — НДМ полосы.</summary>
public sealed class ShellSimplStripModelParamsTests
{
    [Fact]
    public void WithoutField_IsFormulas()
    {
        var p = ShellSimplParams.Parse("{\"mx\":10,\"step_deg\":10}");
        Assert.Equal(StripModel.Formulas, p.Model);
    }

    [Theory]
    [InlineData("ndm", StripModel.Ndm)]
    [InlineData("NDM", StripModel.Ndm)]
    [InlineData("formulas", StripModel.Formulas)]
    [InlineData("", StripModel.Formulas)]
    public void Parsed(string value, StripModel expected)
    {
        var p = ShellSimplParams.Parse($"{{\"strip_model\":\"{value}\"}}");
        Assert.Equal(expected, p.Model);
    }
}
