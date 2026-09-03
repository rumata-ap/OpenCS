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
              "extrema": { "eps_b_min": -0.001, "eps_b_max": 0.002, "eps_s_min": -0.003, "eps_s_max": 0.004 }
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
    }
}
