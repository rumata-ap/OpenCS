using CScore.Import;
using Xunit;

namespace CScore.Tests;

/// <summary>Параметрические сечения SCAD (S0/S3/S6) и местные оси стержня по правилу SCAD.</summary>
public sealed class ScadBarSectionTests
{
    [Fact]
    public void S0_Rectangle()
    {
        var g = ScadBarSection.Parse("S0 3e+10 0.3 0.5 RO 25", 1)!;
        Assert.Equal(0.15, g.A, 12);
        Assert.Equal(0.3 * 0.125 / 12, g.Iy, 12);
        Assert.Equal(0.5 * 0.027 / 12, g.Iz, 12);
    }

    [Fact]
    public void S3_SymmetricIBeam_AgainstHandFormula()
    {
        // Стенка 6 мм, высота 307 мм, полки 217×3 мм (жёсткость «К» схемы 18x60x8h).
        var g = ScadBarSection.Parse("S3 2.0601e+11  0.006  0.307  0.217  0.003  0.217  0.003 NU 0.3", 1)!;
        double hw = 0.307 - 0.006;
        Assert.Equal(2 * 0.217 * 0.003 + 0.006 * hw, g.A, 12);
        double iy = 0.006 * hw * hw * hw / 12 + 2 * (0.217 * 0.003 * 0.003 * 0.003 / 12 + 0.217 * 0.003 * Math.Pow((0.307 - 0.003) / 2, 2));
        Assert.Equal(iy, g.Iy, 12);
        Assert.Equal((2 * 0.003 * Math.Pow(0.217, 3) + hw * Math.Pow(0.006, 3)) / 12, g.Iz, 12);
    }

    [Fact]
    public void S3_UnequalFlanges_CentroidShifted()
    {
        // Нижняя полка шире: центр тяжести ниже середины — Iy меньше, чем относительно середины высоты.
        var g = ScadBarSection.Parse("S3 2e11 0.01 0.4 0.3 0.02 0.1 0.01", 1)!;
        double aMid = 0.3 * 0.02 * Math.Pow(0.2 - 0.01, 2) + 0.1 * 0.01 * Math.Pow(0.2 - 0.005, 2);
        Assert.True(g.Iy > 0 && g.Iy < aMid + 0.01 * Math.Pow(0.37, 3) / 12);
    }

    [Fact]
    public void S6_Pipe_And_UnitScale()
    {
        var g = ScadBarSection.Parse("S6 2e11 17 0", 0.001)!; // мм → м, сплошной круг 17 мм
        Assert.Equal(Math.PI * 0.017 * 0.017 / 4, g.A, 12);
        Assert.Equal(2 * g.Iy, g.J, 15);
        Assert.Null(ScadBarSection.Parse("STZ RUSSIAN diam 403", 1));
    }

    [Theory]
    [InlineData(1, 0, 0, 0, 1, 0)]   // вдоль X: Z1 вверх, Y1 = Z × X = +Y
    [InlineData(0, 1, 0, -1, 0, 0)]  // вдоль Y: Y1 = Z × Y = −X
    [InlineData(0, 0, 1, 0, 1, 0)]   // вертикальный: Y1 вдоль глобальной Y
    public void LocalY_Default(double dx, double dy, double dz, double yx, double yy, double yz)
    {
        var y = ScadRodAxes.LocalY(null, (0, 0, 0), (dx, dy, dz))!;
        Assert.Equal(new[] { yx, yy, yz }, y.Select(v => Math.Round(v, 12) + 0.0));
    }

    [Fact]
    public void LocalY_AngleAndVector()
    {
        // Угол 90° против часовой с конца X1: Y1 переходит в Z1⁰ (вверх).
        var y = ScadRodAxes.LocalY(new ScadRodAxes(ScadRodAxes.AngleDeg, [90]), (0, 0, 0), (1, 0, 0))!;
        Assert.Equal(1, y[2], 12);
        // Тип 4: Y1 по вектору (проекция на плоскость сечения).
        y = ScadRodAxes.LocalY(new ScadRodAxes(4, [1, 0, 1]), (0, 0, 0), (0, 0, 1))!;
        Assert.Equal(new[] { 1.0, 0, 0 }, y.Select(v => Math.Round(v, 12) + 0.0));
        // Тип 6: Z1 по вектору +Y у вертикального стержня ⇒ Y1 = Z1 × X1 = +X.
        y = ScadRodAxes.LocalY(new ScadRodAxes(6, [0, 1, 0]), (0, 0, 0), (0, 0, 1))!;
        Assert.Equal(new[] { 1.0, 0, 0 }, y.Select(v => Math.Round(v, 12) + 0.0));
    }

    [Fact]
    public void SteelProfile_RoundAndWeldedIBeam()
    {
        var round = ImportedSteelElastic.Compute(new ImportedSteelShape(CScore.Sp16.SteelProfileKind.Round,
            CScore.Sp16.SteelFabrication.Rolled, 0.02, 0, 0, 0, 0, 0, 0, "ГОСТ 2590", "20"))!;
        Assert.Equal(Math.PI * 1e-4, round.A, 3e-7);       // многоугольник по 8 сегментов на четверть
        Assert.Equal(Math.PI * 1.6e-7 / 32, round.It, 15);

        // Сварной двутавр 300×200, стенка 6, полки 10: Iy вокруг горизонтальной оси (Y1), Iz — вокруг вертикальной.
        var ib = ImportedSteelElastic.Compute(new ImportedSteelShape(CScore.Sp16.SteelProfileKind.IBeam,
            CScore.Sp16.SteelFabrication.Welded, 0.3, 0.2, 0.006, 0.01, 0, 0, 0, "", "сварной"))!;
        double hw = 0.28;
        Assert.Equal(2 * 0.2 * 0.01 + 0.006 * hw, ib.A, 12);
        Assert.Equal(0.006 * hw * hw * hw / 12 + 2 * (0.2 * 1e-6 / 12 + 0.002 * 0.145 * 0.145), ib.Iy, 12);
        Assert.Equal(2 * 0.01 * 0.008 / 12 + hw * 0.006 * 0.006 * 0.006 / 12, ib.Iz, 12);
        Assert.True(ib.It > 0);
    }
}
