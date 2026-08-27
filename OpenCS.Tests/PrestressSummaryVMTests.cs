using CScore;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверяет подключение блока преднапряжения к специализированным сводкам.</summary>
public sealed class PrestressSummaryVMTests
{
    [Fact]
    public void CrackingSummary_ReadsPrestressBlock()
    {
        var summary = new CrackingSummaryVM(Result("cracking"));

        Assert.Single(summary.Prestress.PrestressRows);
        Assert.True(summary.Prestress.HasPrestress);
    }

    [Fact]
    public void CrackWidthSummary_ReadsPrestressBlock()
    {
        var summary = new CrackWidthSummaryVM(Result("crack_width"), section: null);

        Assert.Single(summary.Prestress.PrestressRows);
        Assert.True(summary.Prestress.HasPrestress);
    }

    [Fact]
    public void TotalCurvatureSummary_ReadsPrestressBlock()
    {
        var summary = new TotalCurvatureSummaryVM(Result("total_curvature"));

        Assert.Single(summary.Prestress.PrestressRows);
        Assert.True(summary.Prestress.HasPrestress);
    }

    static CalcResult Result(string kind) => new()
    {
        TaskKind = kind,
        Status = "ok",
        DataJson = """
        {
          "prestress": {
            "reference": { "x_m": 0.0, "y_m": 0.0 },
            "nominal": { "N_kN": -100.0, "Mx_kNm": 10.0, "My_kNm": 0.0 },
            "effective": { "N_kN": -90.0, "Mx_kNm": 9.0, "My_kNm": 0.0 },
            "actual": { "N_kN": -80.0, "Mx_kNm": 8.0, "My_kNm": 0.0 },
            "hasGroupsAboveStrength": false,
            "groups": [
              {
                "tag": "strand",
                "area_m2": 0.0001,
                "sigSp_MPa": 900.0,
                "gammaSp": 1.0,
                "nominal": { "N_kN": -100.0, "Mx_kNm": 10.0, "My_kNm": 0.0 },
                "effective": { "N_kN": -90.0, "Mx_kNm": 9.0, "My_kNm": 0.0 },
                "actual": { "N_kN": -80.0, "Mx_kNm": 8.0, "My_kNm": 0.0 },
                "sigActual_MPa": 800.0,
                "sigLimit_MPa": 870.0,
                "exceedsStrength": false
              }
            ]
          }
        }
        """
    };
}
