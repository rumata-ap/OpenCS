using CScore.Import;
using Xunit;

namespace CScore.Tests;

public class LiraUnitsTests
{
    [Fact]
    public void FromPreLines_RealFileHomoglyphs_ParsesDistributedAndStressUnits()
    {
        // Точные строки (с латинской "p" вместо кириллической "р") из реального экспорта ЛИРА.
        var preLines = new[]
        {
            "Единицы измеpения усилий: т",
            "Единицы измеpения напpяжений: т/м2",
            "Единицы измеpения моментов: т*м",
            "Единицы измеpения pаспpеделенных моментов: (т*м)/м",
            "Единицы измеpения pаспpеделенных пеpеpезывающих сил: т/м",
        };
        var units = LiraUnitScales.FromPreLines(preLines, tonFactor: 9.80665);

        Assert.Equal(9.80665, units.Force, 6);
        Assert.Equal(9.80665, units.Moment, 6);
        Assert.Equal(9.80665, units.ShellForce, 6);   // "расп...сил" — теперь распознаётся
        Assert.Equal(9.80665, units.ShellMoment, 6);   // "расп...момент" — теперь распознаётся
        Assert.Equal(9.80665, units.Stress, 6);        // "напpяжений: т/м2"
    }

    [Fact]
    public void FromPreLines_StressSwitchedToKn_ParsesDifferentlyFromForces()
    {
        var preLines = new[]
        {
            "Единицы измеpения усилий: т",
            "Единицы измеpения напpяжений: кН/м2",
        };
        var units = LiraUnitScales.FromPreLines(preLines, tonFactor: 9.80665);

        Assert.Equal(9.80665, units.Force, 6);
        Assert.Equal(1.0, units.Stress, 6);
    }

    [Fact]
    public void FromPreLines_NoStressLine_KeepsDefaultTonFactor()
    {
        var units = LiraUnitScales.FromPreLines(new[] { "Единицы измеpения усилий: т" }, tonFactor: 9.80665);
        Assert.Equal(9.80665, units.Stress, 6);
    }
}
