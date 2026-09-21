using CScore;

using CSmath;

using Xunit;

namespace CScore.Tests;

/// <summary>
/// Группа стали по табл. В.9 СП 16 определяется по Ry в МПа, хотя характеристики
/// OpenCS хранятся в кПа (регрессия: раньше любая сталь попадала в группу 5).
/// </summary>
public sealed class SteelDsp16GroupTests
{
    [Theory]
    [InlineData(240_000.0, 141.6, 1.653)] // С245, группа 1
    [InlineData(345_000.0, 88.3, 1.415)]  // группа 2
    [InlineData(440_000.0, 67.1, 1.345)]  // группа 3
    [InlineData(480_000.0, 49.6, 1.33)]   // группа 4
    [InlineData(590_000.0, 26.2, 1.16)]   // группа 5
    public void DSP16_GroupFromRyInMpa(double ryKpa, double epsUOverEpsY, double sigUOverRy)
    {
        var chars = new MaterialChars(CalcType.C)
        {
            Type = MatType.Steel,
            Ry = ryKpa,
            E = 206_000_000.0,
        };

        var tension = (LSpline)chars.DSP16().It;
        double epsY = ryKpa / 206_000_000.0;

        // Точки ветви растяжения: 0, пц, Ry, конец площадки, σu, σt → σu предпоследняя.
        int iu = tension.X.Length - 2;
        Assert.Equal(epsUOverEpsY * epsY, tension.X[iu], 12);
        Assert.Equal(sigUOverRy * ryKpa, tension.Y[iu], 6);
    }
}
