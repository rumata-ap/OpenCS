using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public class FemLocalAxisTests
{
    static FemLinearNode N(int tag, double x, double y, double z) => new(tag, x, y, z, new bool[6]);

    [Fact]
    public void Vecxz_HorizontalBar_UsesGlobalZAsLocalY()
    {
        var v = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 3, 0, 0));
        Assert.Equal((0, -1, 0), v);
    }

    [Fact]
    public void Vecxz_HorizontalBarPointingNegativeX_StillUsesGlobalZAsLocalY()
    {
        var v = FemLocalAxis.Vecxz(N(1, 3, 0, 0), N(2, 0, 0, 0));
        Assert.Equal((0, 1, 0), v);
    }

    [Fact]
    public void Vecxz_VerticalBar_UsesGlobalXAsLocalY()
    {
        var v = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 0, 0, 4));
        Assert.Equal((0, 1, 0), v);
    }

    [Fact]
    public void Vecxz_VerticalBarPointingNegativeZ_UsesGlobalXAsLocalY()
    {
        var v = FemLocalAxis.Vecxz(N(1, 0, 0, 4), N(2, 0, 0, 0));
        Assert.Equal((0, -1, 0), v);
    }

    [Fact]
    public void Vecxz_ZeroLength_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FemLocalAxis.Vecxz(N(1, 1, 1, 1), N(2, 1, 1, 1)));
    }

    /// <summary>
    /// Регрессия: стержень (0,0,0)→(0.01,0.01,4) отклонён от вертикали всего на ~0.2°
    /// (реальный случай — импортированная/введённая геометрия с шумом округления).
    /// Проекция глобальной Z почти вырождается, поэтому используется fallback глобальной X
    /// как локальной Y; vecxz при этом совпадает с global Y.
    /// </summary>
    [Fact]
    public void Vecxz_NearVerticalBarWithTinyHorizontalOffset_UsesVerticalFallback()
    {
        var (x, y, z) = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 0.01, 0.01, 4));
        Assert.Equal(0, x, 2);
        Assert.Equal(1, y, 5);
        Assert.Equal(0, z, 2);
    }

    /// <summary>
    /// Горизонтальный вдоль X стержень: при β=0 локальная Y совпадает с глобальной Z.
    /// Поворот на 90° по правилу правой руки переводит локальную Y в -global Y,
    /// поэтому vecxz (локальная Z) становится -global Z.
    /// </summary>
    [Fact]
    public void Vecxz_HorizontalBar_Rotated90_ReturnsMinusGlobalZ()
    {
        var (x, y, z) = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 3, 0, 0), rotationDeg: 90);
        Assert.Equal(0, x, 9);
        Assert.Equal(0, y, 9);
        Assert.Equal(-1, z, 9);
    }

    [Fact]
    public void Vecxz_HorizontalBar_Rotated180_ReturnsGlobalY()
    {
        var (x, y, z) = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 3, 0, 0), rotationDeg: 180);
        Assert.Equal(0, x, 9);
        Assert.Equal(1, y, 9);
        Assert.Equal(0, z, 9);
    }

    /// <summary>Вертикальный стержень: при β=0 локальная Y совпадает с global X.
    /// Поворот на 90° вокруг global Z переводит её в global Y.</summary>
    [Fact]
    public void Vecxz_VerticalBar_Rotated90_ReturnsMinusGlobalX()
    {
        var (x, y, z) = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 0, 0, 4), rotationDeg: 90);
        Assert.Equal(-1, x, 9);
        Assert.Equal(0, y, 9);
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
