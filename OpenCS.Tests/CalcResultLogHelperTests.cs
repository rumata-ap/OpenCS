using CScore;
using OpenCS.Services;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Статус результата → ключ и уровень записи журнала; непрохождение проверки — не ошибка.</summary>
public sealed class CalcResultLogHelperTests
{
    [Theory]
    [InlineData("ok", "CalcResultOk")]
    [InlineData("not_passed", "CalcResultNotPassed")]
    [InlineData("fail", "CalcResultNotPassed")]
    [InlineData("partial", "CalcResultPartial")]
    [InlineData("incomplete", "CalcResultIncomplete")]
    [InlineData("not_converged", "CalcResultNotConverged")]
    [InlineData("not_applicable", "CalcResultNotApplicable")]
    [InlineData("error", "CalcResultError")]
    [InlineData("что-то", "CalcResultError")]
    public void ResolveKey(string status, string key)
        => Assert.Equal(key, CalcResultLogHelper.ResolveKey(status));

    [Theory]
    [InlineData("not_passed", LogLevel.Info)]
    [InlineData("fail", LogLevel.Info)]
    [InlineData("incomplete", LogLevel.Warning)]
    [InlineData("partial", LogLevel.Warning)]
    [InlineData("not_converged", LogLevel.Warning)]
    [InlineData("error", LogLevel.Error)]
    public void ResolveLevel(string status, LogLevel level)
        => Assert.Equal(level, CalcResultLogHelper.ResolveLevel(new CalcResult { Status = status }));
}
