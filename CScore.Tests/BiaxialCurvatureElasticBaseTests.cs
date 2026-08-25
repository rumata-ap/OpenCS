using System;
using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// База упругой жёсткости (Ea0/B0x/B0y), на которую нормируются коэффициенты снижения
/// жёсткости в результате задачи «кривизна-момент». Проверяет оба режима: приведённое
/// сечение (бетон + арматура) и голое бетонное сечение — последнее нужно для сопоставления
/// с упругим МКЭ, где линейные жёсткости стержня задаются без учёта арматуры.
/// </summary>
public class BiaxialCurvatureElasticBaseTests
{
    // Example47: прямоугольник 1150×300 из B15 (E = 24 ГПа), 6Ø14 снизу (As = 923 мм²).
    // Бетон нарезан сеткой SliceXY(nx: 24, ny: 12) — база жёсткости обязана этого НЕ
    // замечать: она считается контурным интегралом, а не суммой по фибрам.
    const double Width = 1.150;
    const double Height = 0.300;
    const double Eb = 24_000_000.0;

    static double ConcreteEa => Eb * Width * Height;
    static double ConcreteB0x => Eb * Width * Height * Height * Height / 12.0;
    static double ConcreteB0y => Eb * Height * Width * Width * Width / 12.0;

    [Fact]
    public void ElasticBaseWithoutRebar_MatchesBareConcreteSection()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            elasticBaseWithoutRebar: true);

        var result = solver.Compute(0.0, -60.0, -20.0, CurvatureNMode.Constant, usePsi: false);

        Assert.Equal(ConcreteEa, result.Ea0, ConcreteEa * 1e-6);
        Assert.Equal(ConcreteB0x, result.B0x, ConcreteB0x * 1e-6);
        Assert.Equal(ConcreteB0y, result.B0y, ConcreteB0y * 1e-6);
    }

    [Fact]
    public void ElasticBaseWithRebar_IsStifferThanBareConcrete()
    {
        var section = TestSections.Example47();
        var withRebar = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            elasticBaseWithoutRebar: false)
            .Compute(0.0, -60.0, -20.0, CurvatureNMode.Constant, usePsi: false);

        // Арматура снизу: осевая и изгибная (относительно оси X) жёсткости приведённого
        // сечения строго выше бетонных; относительно оси Y стержни разнесены по ширине,
        // поэтому B0y также выше.
        Assert.True(withRebar.Ea0 > ConcreteEa);
        Assert.True(withRebar.B0x > ConcreteB0x);
        Assert.True(withRebar.B0y > ConcreteB0y);
    }
}
