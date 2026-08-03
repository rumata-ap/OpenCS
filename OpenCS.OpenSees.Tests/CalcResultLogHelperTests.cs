using CScore;
using OpenCS.Services;
using OpenCS.Tasks;

namespace OpenCS.OpenSees.Tests;

public sealed class CalcResultLogHelperTests
{
    [Fact]
    public void Error_result_is_logged_as_error_with_json_detail()
    {
        CalcResult result = new()
        {
            Status = "error",
            DataJson = "{\"error\":\"Фибровая сетка не подготовлена\"}"
        };

        Assert.Equal(LogLevel.Error, CalcResultLogHelper.ResolveLevel(result));
        Assert.Equal("Фибровая сетка не подготовлена", CalcResultLogHelper.ExtractDetail(result));
    }
}
