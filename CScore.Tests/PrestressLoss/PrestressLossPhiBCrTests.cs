using CScore.PrestressLoss;
using Xunit;

namespace CScore.Tests.PrestressLoss;

/// <summary>
/// φb,cr в потерях от ползучести (ф. 9.9) — по таблице 6.12 СП 63.13330.2018 (сверено с HTML
/// нормы 29.09.2026). Раньше расчёт потерь держал собственную ошибочную копию таблицы.
/// </summary>
public sealed class PrestressLossPhiBCrTests
{
    [Theory]
    [InlineData(HumidityClass.Above75, 20, 2.0)]
    [InlineData(HumidityClass.Above75, 60, 1.0)]
    [InlineData(HumidityClass.H40_75, 25, 2.5)]
    [InlineData(HumidityClass.H40_75, 40, 1.9)]
    [InlineData(HumidityClass.Below40, 30, 3.2)]
    [InlineData(HumidityClass.Below40, 15, 4.8)]
    [InlineData(HumidityClass.Below40, 80, 2.0)]   // столбец B60–B100
    [InlineData(HumidityClass.H40_75, 5, 3.9)]     // ниже B10 — как B10
    public void PhiBCr_MatchesTable612(HumidityClass humidity, double concreteClass, double expected) =>
        Assert.Equal(expected, PrestressLossCalc.PhiBCr(humidity, concreteClass), 9);
}
