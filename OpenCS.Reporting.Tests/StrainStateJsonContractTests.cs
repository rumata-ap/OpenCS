using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

public sealed class StrainStateJsonContractTests
{
    [Fact]
    public void Parse_ReadsDiagnosticsWithFixedMatrixOrder()
    {
        var data = StrainStateReportData.Parse("""
            {
              "formula_version": "SP63.13330.2021/8.1",
              "stiffness": { "source": "mixed", "d11": 1, "d12": 2, "d13": 3, "d21": 2, "d22": 4, "d23": 5, "d31": 3, "d32": 5, "d33": 6 },
              "jacobian": { "rows": ["N", "Mx", "My"], "columns": ["e0", "ky", "kz"], "scheme": "central", "h": 0.0000001, "values": [[1,2,3],[4,5,6],[7,8,9]] },
              "equilibrium": { "n": 10, "mx": 11, "my": 12 },
              "extrema": { "eps_b_min": -0.001, "eps_b_max": 0.002, "eps_s_min": -0.003, "eps_s_max": 0.004 },
              "section": { "id": 7, "num": 3, "tag": "Колонна К-1", "description": "Прямоугольное сечение" },
              "rebar": [{ "num": 1, "x_mm": -120, "y_mm": -180, "eps": 0.0012, "sigma_mpa": 240, "e_sec_mpa": 200000, "area_mm2": 201.1, "diameter_mm": 16, "group": "Нижняя арматура", "material": "A500" }]
            }
            """);

        Assert.Equal("SP63.13330.2021/8.1", data.FormulaVersion);
        Assert.Equal("mixed", data.Stiffness.Source);
        Assert.Equal(1.0, data.Stiffness.D11);
        Assert.Equal(["N", "Mx", "My"], data.Jacobian.Rows);
        Assert.Equal(["e0", "ky", "kz"], data.Jacobian.Columns);
        Assert.Equal(9.0, data.Jacobian.Values[2][2]);
        Assert.Equal(10.0, data.Equilibrium.N);
        Assert.Equal(-0.001, data.Extrema.ConcreteMin, 12);
        Assert.Equal(7, data.Section?.Id);
        Assert.Equal("Колонна К-1", data.Section?.Tag);
        Assert.Single(data.Rebar);
        Assert.Equal(200000, data.Rebar[0].SecantModulusMpa);
    }
}
