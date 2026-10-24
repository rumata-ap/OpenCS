using CScore;
using CScore.LimitSurface;
using OpenCS.LimitSurface;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Переход из точки поверхности в одиночную задачу предельного усилия того же режима.</summary>
public class LimitSurfaceRowTaskFactoryTests
{
    static readonly CalcTask Surface = new() { Id = 5, Kind = "limit_surface", SectionId = 3, CalcType = CalcType.CL, ForceSetId = 2 };
    static readonly LimitSurfacePointRecord Point = new(4, "РСУ 4", -250, -30, 2);

    [Theory]
    [InlineData(UtilizationMode.All, "limit_force")]
    [InlineData(UtilizationMode.Moment, "limit_moment")]
    [InlineData(UtilizationMode.Axial, "limit_axial")]
    public void Create_KindSectionForces(UtilizationMode mode, string kind)
    {
        var t = LimitSurfaceRowTaskFactory.Create(Surface, Point, mode, "Балка");
        Assert.Equal(kind, t.Kind);
        Assert.Equal(3, t.SectionId);
        Assert.Equal(CalcType.CL, t.CalcType);
        Assert.Equal(0, t.ForceSetId);
        Assert.Equal(0, t.ForceItemId);
        var p = LimitForceParams.Parse(t.ParamsJson);
        Assert.Equal<(double?, double?, double?)>((-250.0, -30.0, 2.0), (p.N, p.Mx, p.My));
        Assert.Equal("Балка — РСУ 4", t.Tag);
    }

    [Fact]
    public void FindExisting_SameSurfaceRowAndMode()
    {
        var made = LimitSurfaceRowTaskFactory.Create(Surface, Point, UtilizationMode.Moment, "Балка");
        made.Id = 11;
        var tasks = new[] { made };
        Assert.Same(made, LimitSurfaceRowTaskFactory.FindExisting(tasks, Surface, 4, UtilizationMode.Moment));
        Assert.Null(LimitSurfaceRowTaskFactory.FindExisting(tasks, Surface, 4, UtilizationMode.All));
        Assert.Null(LimitSurfaceRowTaskFactory.FindExisting(tasks, Surface, 5, UtilizationMode.Moment));
    }

    [Fact]
    public void Supports_OnlyLimitSurface()
    {
        Assert.True(LimitSurfaceRowTaskFactory.Supports("limit_surface"));
        Assert.False(LimitSurfaceRowTaskFactory.Supports("crack_surface"));
    }
}
