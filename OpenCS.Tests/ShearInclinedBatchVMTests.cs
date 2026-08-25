using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Разбор сводки пакетного расчёта наклонных сечений.</summary>
public sealed class ShearInclinedBatchVMTests
{
    const string Json = """
    {
      "sectionTag": "Б-1",
      "forceSetTag": "РСУ-1",
      "utilization": 1.24,
      "utilizationStatus": "ok",
      "rows": [
        { "num": 1, "label": "оп. A", "vy": 150.0, "vx": 0.0, "utilization": 1.24, "status": "failed", "worstFormula": "8.56" },
        { "num": 2, "label": "оп. B", "vy": 90.0, "vx": 0.0, "utilization": 0.62, "status": "ok", "worstFormula": "8.60" }
      ],
      "warnings": [ "Конструктивные требования раздела 10.3 не подтверждены." ]
    }
    """;

    [Fact]
    public void Constructor_ParsesHeaderAndRows()
    {
        var vm = new ShearInclinedBatchVM(Json);

        Assert.Equal("Б-1", vm.SectionTag);
        Assert.Equal("РСУ-1", vm.ForceSetTag);
        Assert.Equal(1.24, vm.Utilization, 6);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("оп. A", vm.Rows[0].Label);
        Assert.Equal("8.56", vm.Rows[0].WorstFormula);
    }

    [Fact]
    public void Row_NullUtilizationWithFailedStatus_IsInfinityNotMissingValue()
    {
        string json = Json.Replace(
            "\"utilization\": 1.24, \"status\": \"failed\"",
            "\"utilization\": null, \"status\": \"failed\"");

        var vm = new ShearInclinedBatchVM(json);

        Assert.True(double.IsPositiveInfinity(vm.Rows[0].Utilization));
        Assert.Equal("∞", vm.Rows[0].UtilizationText);
    }

    [Fact]
    public void Row_ErrorStatus_ShowsDashInsteadOfNumber()
    {
        string json = Json.Replace(
            "\"utilization\": 0.62, \"status\": \"ok\"",
            "\"utilization\": null, \"status\": \"error\"");

        var vm = new ShearInclinedBatchVM(json);

        Assert.True(double.IsNaN(vm.Rows[1].Utilization));
        Assert.Equal("—", vm.Rows[1].UtilizationText);
    }

    [Fact]
    public void Constructor_ExposesCautions()
    {
        var vm = new ShearInclinedBatchVM(Json);

        Assert.Contains(vm.Cautions, c => c.Contains("10.3"));
    }

    [Fact]
    public void Constructor_ErrorJson_DoesNotThrow()
    {
        var vm = new ShearInclinedBatchVM("""{ "error": "нет набора усилий" }""");

        Assert.Empty(vm.Rows);
        Assert.Contains("нет набора усилий", vm.ErrorText);
    }
}
