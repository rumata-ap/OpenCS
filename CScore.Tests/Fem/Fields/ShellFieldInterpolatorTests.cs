using CScore.Fem.Fields;
using Xunit;

namespace CScore.Tests.Fem.Fields;

public class ShellFieldInterpolatorTests
{
    static double F(FieldVec3 p) => 10 + 2 * p.X + 3 * p.Y + 0.5 * p.Z;

    /// <summary>Точки решёткой с шагом 0,7 по плоскости участка с запасом за края.</summary>
    static IEnumerable<FieldVec3> Lattice(FieldVec3 o, FieldVec3 du, FieldVec3 dv, double lu, double lv)
    {
        for (double a = -0.5; a <= lu + 0.5; a += 0.7)
            for (double b = -0.5; b <= lv + 0.5; b += 0.7)
                yield return o + du * a + dv * b;
    }

    [Fact]
    public void LinearField_ReproducedExactly_OnHorizontalVerticalAndInclined()
    {
        var mesh = FieldTestMeshes.Basement();
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var pts = Lattice(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), 4, 4)
            .Concat(Lattice(new(0, 0, -0.25), new(1, 0, 0), new(0, 0, -1), 4, 2.75))
            .Concat(Lattice(new(4, 0, 0), new(c, 0, -s), new(0, 1, 0), 3, 4))
            .Select(p => new FieldPoint(p, [F(p)])).ToList();
        // Допуск 0,05 м: ближайшие «чужие» точки решёток лежат в 0,1 м и дальше от соседних плоскостей — точка
        // с чужой плоскости, спроецированная на участок, исказила бы точную линейную интерполяцию.
        var result = ShellFieldInterpolator.Evaluate(pts, hasZ: true,
            mesh.Shells.Where(x => !x.Tag.StartsWith("riser") && !x.Tag.StartsWith("low")).ToList(),
            new FieldSettings(MaxDistance: 2, PlaneTolerance: 0.05));
        foreach (var t in result.Targets)
        {
            var shell = mesh.Shells.Single(x => x.Tag == t.Tag);
            Assert.Equal(FieldHow.Interpolated, t.How);
            Assert.Equal(F(shell.Centroid), t.Values![0], 9);
        }
    }

    [Fact]
    public void WallValues_DoNotLeakIntoSlab()
    {
        var mesh = FieldTestMeshes.Basement();
        var slab = Lattice(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), 4, 4).Select(p => new FieldPoint(p, [100.0]));
        var wall = Lattice(new(0, 0, -0.25), new(1, 0, 0), new(0, 0, -1), 4, 2.75).Select(p => new FieldPoint(p, [500.0]));
        var result = ShellFieldInterpolator.Evaluate(slab.Concat(wall).ToList(), true,
            mesh.Shells.Where(x => x.Tag.StartsWith("slab") || x.Tag.StartsWith("wall")).ToList(),
            new FieldSettings(MaxDistance: 2, PlaneTolerance: 0.1));
        Assert.All(result.Targets.Where(t => t.Tag.StartsWith("slab")), t => Assert.Equal(100, t.Values![0], 9));
        Assert.All(result.Targets.Where(t => t.Tag.StartsWith("wall")), t => Assert.Equal(500, t.Values![0], 9));
    }

    [Fact]
    public void LowStep_WithinPlaneTolerance_DoesNotLeak()
    {
        // Ступень 0,3 м — меньше допуска до плоскости 0,5 м: точки нижней плиты ближе к своей плоскости и верхнему
        // участку не достаются.
        var mesh = new FieldTestMeshes()
            .Grid("up", new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), 3, 3)
            .Grid("riser", new(0, 3, 0), new(1, 0, 0), new(0, 0, -0.3), 3, 1)
            .Grid("low", new(0, 3, -0.3), new(1, 0, 0), new(0, 1, 0), 3, 3);
        var up = Lattice(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), 3, 3).Select(p => new FieldPoint(p, [100.0]));
        var low = Lattice(new(0, 3, -0.3), new(1, 0, 0), new(0, 1, 0), 3, 3).Select(p => new FieldPoint(p, [500.0]));
        var r = ShellFieldInterpolator.Evaluate(up.Concat(low).ToList(), true,
            mesh.Shells.Where(x => !x.Tag.StartsWith("riser")).ToList(), new FieldSettings(MaxDistance: 2, PlaneTolerance: 0.5));
        Assert.All(r.Targets.Where(t => t.Tag.StartsWith("up")), t => Assert.Equal(100, t.Values![0], 9));
        Assert.All(r.Targets.Where(t => t.Tag.StartsWith("low")), t => Assert.Equal(500, t.Values![0], 9));
    }

    [Fact]
    public void SparseTable_SmallPatchInsideCell_Interpolated()
    {
        // Таблица с шагом 10 м и участок 2 × 2 м внутри ячейки: КЭ внутри оболочки облака — интерполяция, а не
        // «нет значения».
        var mesh = new FieldTestMeshes().Grid("p", new(4, 4, 0), new(1, 0, 0), new(0, 1, 0), 2, 2);
        var pts = new List<FieldPoint>();
        for (int i = 0; i <= 2; i++)
            for (int j = 0; j <= 2; j++)
            {
                var p = new FieldVec3(-5 + 10 * i, -5 + 10 * j, 0);
                pts.Add(new(p, [F(p)]));
            }
        var r = ShellFieldInterpolator.Evaluate(pts, true, mesh.Shells, new FieldSettings(MaxDistance: 1, PlaneTolerance: 0.5));
        Assert.All(r.Targets, t =>
        {
            Assert.Equal(FieldHow.Interpolated, t.How);
            Assert.Equal(F(mesh.Shells.Single(s => s.Tag == t.Tag).Centroid), t.Values![0], 9);
        });
    }

    [Fact]
    public void OutsideCloud_NearestWithinLimit_ElseNone()
    {
        var mesh = new FieldTestMeshes().Grid("p", new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), 4, 1);
        var pts = new List<FieldPoint>();
        for (double x = 0; x <= 2.001; x += 0.5)
            for (double y = 0; y <= 1.001; y += 0.5)
                pts.Add(new(new(x, y, 0), [x]));
        var r = ShellFieldInterpolator.Evaluate(pts, true, mesh.Shells, new FieldSettings(MaxDistance: 1, PlaneTolerance: 0.1));
        var byTag = r.Targets.ToDictionary(t => t.Tag);
        Assert.Equal(FieldHow.Interpolated, byTag["p1_0"].How);       // центр x = 1,5
        Assert.Equal(FieldHow.Nearest, byTag["p2_0"].How);            // центр x = 2,5 — 0,5 м от облака
        Assert.Equal(2, byTag["p2_0"].Values![0], 9);
        Assert.Equal(FieldHow.None, byTag["p3_0"].How);               // центр x = 3,5 — 1,5 м
        Assert.Contains(r.Diagnostics, d => d.Code == "field_no_value" && d.Tags.SequenceEqual(["p3_0"]));
        Assert.Contains(r.Diagnostics, d => d.Code == "field_nearest_outside" && d.Tags.SequenceEqual(["p2_0"]));
    }

    [Fact]
    public void NoZ_HorizontalGetsValues_WallReported()
    {
        var mesh = new FieldTestMeshes()
            .Grid("slab", new(0, 0, -16.3), new(1, 0, 0), new(0, 1, 0), 3, 3)
            .Grid("wall", new(0, 0, -16.3), new(1, 0, 0), new(0, 0, 1), 3, 2);
        var pts = Lattice(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), 3, 3).Select(p => new FieldPoint(p, [7.0])).ToList();
        var r = ShellFieldInterpolator.Evaluate(pts, hasZ: false, mesh.Shells, new FieldSettings(MaxDistance: 2, PlaneTolerance: 0.1));
        Assert.All(r.Targets.Where(t => t.Tag.StartsWith("slab")), t => Assert.Equal(7, t.Values![0], 9));
        Assert.All(r.Targets.Where(t => t.Tag.StartsWith("wall")), t => Assert.Equal(FieldHow.None, t.How));
        Assert.Contains(r.Diagnostics, d => d.Code == "field_no_z_vertical" && d.Tags.Count == 6);
    }

    [Fact]
    public void Idw_And_Nearest_Methods()
    {
        var mesh = new FieldTestMeshes().Grid("p", new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), 1, 1);
        var pts = new List<FieldPoint> { new(new(0, 0, 0), [0]), new(new(1, 1, 0), [10]) };
        var idw = ShellFieldInterpolator.Evaluate(pts, true, mesh.Shells, new FieldSettings(FieldMethod.Idw, 2, 0.1));
        Assert.Equal(5, idw.Targets[0].Values![0], 9);   // равные расстояния → среднее
        var near = ShellFieldInterpolator.Evaluate(pts, true, mesh.Shells, new FieldSettings(FieldMethod.Nearest, 2, 0.1));
        Assert.Equal(FieldHow.Nearest, near.Targets[0].How);
    }

    [Fact]
    public void MedianSize_IsSqrtOfMedianArea()
    {
        var mesh = new FieldTestMeshes().Grid("p", new(0, 0, 0), new(0.5, 0, 0), new(0, 0.5, 0), 3, 3);
        Assert.Equal(0.5, FieldDefaults.MedianSize(mesh.Shells), 12);
    }
}
