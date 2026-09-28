using CScore;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Единицы геометрического поля τ/(GΘ) в результате кручения: размерность длины, показ в мм.</summary>
public sealed class TorsionResultDataTests
{
    static TorsionResultData Parse(string json) =>
        TorsionResultData.FromCalcResult(new CalcResult { Status = "ok", DataJson = json });

    [Fact]
    public void TauUnitMaxIsShownInMillimeters()
    {
        var data = Parse("""{"method":"fem","It_m4":0.0037,"tau_unit_max":0.279,"node_x":[0.0],"node_y":[0.0],"tau_unit":[0.279]}""");
        Assert.Equal(279.0, data.TauUnitMaxMm, 6);
        Assert.Equal(279.0, data.FieldValue(0, TorsionFieldMode.TauUnit), 6);
    }

    [Fact]
    public void OldResultWithMm2KeyIsConvertedToMillimeters()
    {
        // Старые результаты хранили tau_unit_max_mm2 = tau_unit_max·10⁶.
        var data = Parse("""{"method":"bem","It_m4":0.0037,"tau_unit_max_mm2":279000.0}""");
        Assert.Equal(279.0, data.TauUnitMaxMm, 6);
    }
}
