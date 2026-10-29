using CScore;
using OpenCS.ViewModels;
using Xunit;
using static OpenCS.ViewModels.LimitForceBatchVM;

namespace OpenCS.Tests;

/// <summary>Предельные усилия и вердикт по Кисп в пакетном расчёте предельного нагружения.</summary>
public sealed class LimitForceBatchVMTests
{
    [Theory]
    [InlineData(0.9056, RowVerdict.Pass)]
    [InlineData(0.9899, RowVerdict.Pass)]
    [InlineData(0.9951, RowVerdict.NearLimit)]
    [InlineData(1.00004, RowVerdict.NearLimit)] // в таблице 1,0000
    [InlineData(1.0001, RowVerdict.Fail)]
    [InlineData(1.0597, RowVerdict.Fail)]
    public void Classify_ByUtilization(double util, RowVerdict expected)
        => Assert.Equal(expected, Classify(true, util));

    [Fact]
    public void Classify_InfiniteUtilization_Fails()
        => Assert.Equal(RowVerdict.Fail, Classify(true, null));

    [Fact]
    public void Classify_NotConverged_IsSeparateVerdict()
        => Assert.Equal(RowVerdict.NotConverged, Classify(false, 0.5));

    [Fact]
    public void Rows_ShowLimitForcesAndVerdict()
    {
        const string json = """
        {
          "all_converged": true, "converged_count": 2, "total": 2, "solver": "fast",
          "rows": [
            { "num": 3, "label": "3", "N": 0, "Mx": -98.1, "My": 0, "factor": 1.1043, "utilization": 0.9056,
              "governing": "concrete", "N_limit": 0, "Mx_limit": -108.3318, "My_limit": 0,
              "iterations": 5, "status": "ok" },
            { "num": 7, "label": "7", "N": -98.1, "Mx": -98.1, "My": -98.1, "factor": 0.9437, "utilization": 1.0597,
              "governing": "concrete", "N_limit": -92.5770, "Mx_limit": -92.577, "My_limit": -92.577,
              "iterations": 4, "status": "ok" }
          ]
        }
        """;

        var vm = new LimitForceBatchVM(new CalcResult { Status = "ok", DataJson = json });

        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(RowVerdict.Pass, vm.Rows[0].Verdict);
        Assert.Equal((-108.3318).ToString("0.0000"), vm.Rows[0].MxLimText);
        Assert.Equal(RowVerdict.Fail, vm.Rows[1].Verdict);
        Assert.Equal((-92.577).ToString("0.0000"), vm.Rows[1].NLimText);
    }

    [Fact]
    public void Rows_NotConverged_HidesLimitForces()
    {
        const string json = """
        {
          "all_converged": false, "converged_count": 0, "total": 1, "solver": "bisection",
          "rows": [
            { "num": 1, "label": "1", "N": 0, "Mx": 10, "My": 0, "factor": 0, "utilization": null,
              "governing": "", "N_limit": 0, "Mx_limit": 0, "My_limit": 0,
              "iterations": 40, "status": "not_converged" }
          ]
        }
        """;

        var vm = new LimitForceBatchVM(new CalcResult { Status = "partial", DataJson = json });

        Assert.Equal(RowVerdict.NotConverged, vm.Rows[0].Verdict);
        Assert.Equal("—", vm.Rows[0].MxLimText);
    }
}
