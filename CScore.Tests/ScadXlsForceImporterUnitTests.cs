using CScore.Import;
using Xunit;

namespace CScore.Tests;

public class ScadXlsForceImporterUnitTests
{
    [Fact]
    public void TryParseScadForceUnitFactor_FindsSilyLine_ReturnsKnFactor()
    {
        var cells = new List<List<string>>
        {
            new() { "Величины усилий" },
            new() { "Единицы измерения:" },
            new() { "- Силы: Т" },
            new() { "- Единицы длины для силовых факторов: м" },
        };
        double? f = ScadXlsForceImporter.TryParseScadForceUnitFactor(cells, tonFactor: 9.80665);
        Assert.Equal(9.80665, f!.Value, 6);
    }

    [Fact]
    public void TryParseScadForceUnitFactor_KnSetting_ReturnsOne()
    {
        var cells = new List<List<string>> { new() { "- Силы: кН" } };
        double? f = ScadXlsForceImporter.TryParseScadForceUnitFactor(cells, tonFactor: 9.80665);
        Assert.Equal(1.0, f!.Value, 6);
    }

    [Fact]
    public void TryParseScadForceUnitFactor_NoLine_ReturnsNull()
    {
        var cells = new List<List<string>> { new() { "Что-то другое" } };
        Assert.Null(ScadXlsForceImporter.TryParseScadForceUnitFactor(cells, tonFactor: 9.80665));
    }

    [Fact]
    public void TryParseScadLengthFactor_FindsLengthLine_ReturnsMetersFactor()
    {
        var cells = new List<List<string>>
        {
            new() { "- Силы: Т" },
            new() { "- Единицы длины для силовых факторов: см" },
        };
        double? f = ScadXlsForceImporter.TryParseScadLengthFactor(cells);
        Assert.Equal(0.01, f!.Value, 6);
    }

    [Fact]
    public void TryParseScadLengthFactor_NoLine_ReturnsNull()
    {
        var cells = new List<List<string>> { new() { "- Силы: Т" } };
        Assert.Null(ScadXlsForceImporter.TryParseScadLengthFactor(cells));
    }
}
