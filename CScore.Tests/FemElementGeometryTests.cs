using CScore.Fem.Loads;
using CScore.Planar;
using Xunit;

namespace CScore.Tests;

public sealed class FemElementGeometryTests
{
    /// <summary>Четырёхугольник свободной сетки Gmsh (узлы в порядке хранения «1 2 4 3»), z = 3.</summary>
    static FemElementGeometry Quad() => FemElementGeometry.FromPoints(
    [
        new(3.438, 1.662, 3), new(3.081, 1.583, 3), new(3.495, 1.239, 3), new(3.117, 1.238, 3),
    ])!;

    static PlanarVector3 Map(FemElementGeometry g, double[] shape) =>
        g.Points.Select((p, k) => p * shape[k]).Aggregate(PlanarVector3.Zero, (s, p) => s + p);

    [Fact]
    public void ShellShapeAt_PointInside_ReproducesPoint()
    {
        var g = Quad();
        var x = new PlanarVector3(3.3, 1.45, 3);
        var shape = g.ShellShapeAt(x);
        Assert.NotNull(shape);
        Assert.Equal(1, shape.Sum(), 12);
        Assert.True((Map(g, shape) - x).Length < 1e-9);
    }

    /// <summary>
    /// Точки за КЭ дальше допуска — null. Второй КЭ — из круговой проверки фрагмента: точка в 2 м от него, Ньютон
    /// обратного отображения не сходится за 30 итераций и останавливается на ξ, η в пределах [−1, 1] — несошедшееся
    /// решение давало функции формы чужой точки (интерполяция прогиба по чужому КЭ).
    /// </summary>
    [Fact]
    public void ShellShapeAt_PointOutsideBeyondTolerance_Null()
    {
        var g = Quad();
        Assert.Null(g.ShellShapeAt(new PlanarVector3(3.236, 1.868, 3)));
        var far = FemElementGeometry.FromPoints(
        [
            new(0.6337460895610083, 1.683123293150108, 3), new(1.157635366383677, 1.435250830260078, 3),
            new(1.0254952093049, 2.056294453871005, 3), new(1.37124089018412, 1.809709978159311, 3),
        ])!;
        Assert.Null(far.ShellShapeAt(new PlanarVector3(3.235642738002212, 1.868363501891439, 3)));
    }
}
