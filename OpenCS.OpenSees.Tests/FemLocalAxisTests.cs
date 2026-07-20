using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public class FemLocalAxisTests
{
    static FemLinearNode N(int tag, double x, double y, double z) => new(tag, x, y, z, new bool[6]);

    [Fact]
    public void Vecxz_HorizontalBar_ReturnsGlobalZ()
    {
        var v = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 3, 0, 0));
        Assert.Equal((0, 0, 1), v);
    }

    [Fact]
    public void Vecxz_VerticalBar_ReturnsGlobalX()
    {
        var v = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 0, 0, 4));
        Assert.Equal((1, 0, 0), v);
    }

    [Fact]
    public void Vecxz_ZeroLength_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FemLocalAxis.Vecxz(N(1, 1, 1, 1), N(2, 1, 1, 1)));
    }

    /// <summary>
    /// Регрессия: стержень (0,0,0)→(0.01,0.01,4) отклонён от вертикали всего на ~0.2°
    /// (реальный случай — импортированная/введённая геометрия с шумом округления). При
    /// vecxz=(0,0,1) опорный вектор почти параллелен оси стержня → geomTransf вырождается,
    /// и локальные оси y/z поворачиваются на произвольный угол (в реальном прогоне OpenSees —
    /// ровно на 45°, из-за симметричного наклона по X и Y). Итог: чистый момент Mx в глобальных
    /// осях расщепляется поровну между My и Mz элемента вместо изгиба в одной плоскости.
    /// </summary>
    [Fact]
    public void Vecxz_NearVerticalBarWithTinyHorizontalOffset_ReturnsGlobalX()
    {
        var v = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 0.01, 0.01, 4));
        Assert.Equal((1, 0, 0), v);
    }

    /// <summary>
    /// Горизонтальный вдоль X стержень: база vecxz=(0,0,1). Поворот вокруг локальной X
    /// (=global X здесь) на 90° по правилу правой руки переводит (0,0,1) в (0,-1,0).
    /// </summary>
    [Fact]
    public void Vecxz_HorizontalBar_Rotated90_ReturnsMinusGlobalY()
    {
        var (x, y, z) = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 3, 0, 0), rotationDeg: 90);
        Assert.Equal(0, x, 9);
        Assert.Equal(-1, y, 9);
        Assert.Equal(0, z, 9);
    }

    [Fact]
    public void Vecxz_HorizontalBar_Rotated180_ReturnsMinusGlobalZ()
    {
        var (x, y, z) = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 3, 0, 0), rotationDeg: 180);
        Assert.Equal(0, x, 9);
        Assert.Equal(0, y, 9);
        Assert.Equal(-1, z, 9);
    }

    /// <summary>Вертикальный стержень: база vecxz=(1,0,0). Поворот на 90° вокруг локальной X
    /// (=global Z здесь) переводит (1,0,0) в (0,1,0).</summary>
    [Fact]
    public void Vecxz_VerticalBar_Rotated90_ReturnsGlobalY()
    {
        var (x, y, z) = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 0, 0, 4), rotationDeg: 90);
        Assert.Equal(0, x, 9);
        Assert.Equal(1, y, 9);
        Assert.Equal(0, z, 9);
    }

    [Fact]
    public void Vecxz_ZeroRotation_MatchesUnrotatedOverload()
    {
        var withDefault = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 3, 0, 0));
        var withZero = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 3, 0, 0), rotationDeg: 0);
        Assert.Equal(withDefault, withZero);
    }
}
