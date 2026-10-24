using CScore;
using CScore.LimitSurface;
using OpenCS.LimitSurface;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сетка балки ReportFixtures для тестов сцены: строится один раз.</summary>
public sealed class LimitSurfaceSceneFixture
{
    public LimitSurfaceResultData Data { get; }

    public LimitSurfaceSceneFixture()
    {
        var section = ReportFixtures.BuildSurfaceBeam();
        section.ResolveAndBuildDiagramms(CalcSettings.Default.Sp63DescEtaMin);
        var criterion = new UlsStrainCriterion(CalcType.C, ten: false);
        var mesh = new StrainDomainMesher(section, criterion, new LimitSurfaceOptions { InitialMeridians = 12, ThetaIntervals = 12 }).Build();
        Data = new LimitSurfaceResultData
        {
            Mesh = LimitSurfaceResultData.EncodeMesh(mesh),
            ControlPoints = criterion.BuildDomain(section).Points
                .Select(p => new LimitSurfaceControlPointRecord(p.Kind, p.X, p.Y, p.AreaIndex)).ToList(),
            Points =
            [
                new(1, "внутри", -100, -10, 0),
                new(2, "снаружи", -100, -10_000, 0),
            ],
            VertexCount = mesh.Vertices.Count,
            InitialMeridians = 12,
            ThetaIntervals = 12,
        };
    }
}

/// <summary>Модель сцены окна поверхности: Кисп точек, сводка, оси, категории ограничений.</summary>
public class LimitSurfaceSceneTests(LimitSurfaceSceneFixture fx) : IClassFixture<LimitSurfaceSceneFixture>
{
    [Fact]
    public void EvaluatePoints_InsideAndOutside()
    {
        var scene = new LimitSurfaceScene(fx.Data);
        var pts = scene.EvaluatePoints(UtilizationMode.All);
        Assert.Equal(PointState.Inside, pts[0].State);
        Assert.True(pts[0].Ratio < 1);
        Assert.Equal(PointState.Outside, pts[1].State);
        Assert.True(pts[1].Ratio > 1);
        var s = LimitSurfaceScene.Summarize(pts);
        Assert.Equal(pts[1].Ratio, s.MaxRatio);
        Assert.Equal(1, s.Outside);
        Assert.Equal(0, s.Ambiguous);
    }

    [Fact]
    public void PointOnSurface_RatioIsOne()
    {
        var scene = new LimitSurfaceScene(fx.Data);
        var v = scene.Mesh.Vertices[scene.Mesh.Vertices.Count / 3];
        var data = fx.Data;
        var onSurface = new LimitSurfaceScene(new LimitSurfaceResultData
        {
            Mesh = data.Mesh, ControlPoints = data.ControlPoints, VertexCount = data.VertexCount,
            Points = [new(1, "на границе", v.N, v.Mx, v.My)],
        }).EvaluatePoints(UtilizationMode.All)[0];
        Assert.Equal(1.0, onSurface.Ratio, 6);
    }

    [Fact]
    public void Relative_MapsBoundsToZeroAndOne()
    {
        var scene = new LimitSurfaceScene(fx.Data);
        var lo = scene.ToScene(scene.Min, AxisMode.Relative);
        var hi = scene.ToScene(scene.Max, AxisMode.Relative);
        Assert.Equal(new Vec3(0, 0, 0), lo);
        Assert.Equal(new Vec3(1, 1, 1), hi);
        var abs = scene.ToScene(new ForcePoint(-100, 5, 7), AxisMode.Absolute);
        Assert.Equal(new Vec3(5, 7, -100), abs);
    }

    [Fact]
    public void Category_ByKindAndSide()
    {
        var scene = new LimitSurfaceScene(fx.Data);
        int rebar = fx.Data.ControlPoints.FindIndex(p => p.Kind == StrainPointKind.Rebar);
        int concrete = fx.Data.ControlPoints.FindIndex(p => p.Kind == StrainPointKind.Concrete);
        Assert.Equal(ActiveCategory.RebarTension, scene.CategoryOf(new ActiveConstraint(rebar, +1, false)));
        Assert.Equal(ActiveCategory.ConcreteCompression, scene.CategoryOf(new ActiveConstraint(concrete, -1, false)));
        Assert.Equal(ActiveCategory.None, scene.CategoryOf(new ActiveConstraint(-1, 0, false)));
    }

    [Theory]
    [InlineData(SurfaceColoring.Active)]
    [InlineData(SurfaceColoring.SingleSign)]
    [InlineData(SurfaceColoring.ByN)]
    [InlineData(SurfaceColoring.Mono)]
    public void SurfaceGroups_CoverEveryTriangleOnce(SurfaceColoring coloring)
    {
        var scene = new LimitSurfaceScene(fx.Data);
        var all = scene.SurfaceGroups(coloring).SelectMany(g => g.Triangles).OrderBy(t => t).ToList();
        Assert.Equal(Enumerable.Range(0, scene.Mesh.Triangles.Count), all);
    }

    [Fact]
    public void ActiveGroups_MatchVertexCategory()
    {
        var scene = new LimitSurfaceScene(fx.Data);
        var groups = scene.SurfaceGroups(SurfaceColoring.Active);
        Assert.True(groups.Count >= 2, "ожидаются хотя бы бетон-сжатие и арматура-растяжение");
        foreach (var g in groups)
        foreach (int t in g.Triangles.Take(20))
        {
            var cat = LimitSurfaceScene.Majority(scene.Mesh.Triangles[t], v => scene.CategoryOf(scene.Mesh.Active[v]));
            Assert.Equal($"LimitSurfaceLegend_{cat}", g.LegendKey);
        }
    }

    [Fact]
    public void GridLines_BaseMeridiansAndParallels()
    {
        var scene = new LimitSurfaceScene(fx.Data);
        var lines = scene.GridLines();
        Assert.Equal(12, lines.Meridians.Count);
        Assert.Equal(11, lines.Parallels.Count);
        var v = scene.Mesh.Vertices;
        foreach (var m in lines.Meridians)
        {
            Assert.Equal(v[0], m[0]);           // от полюса растяжения
            Assert.Equal(v[1], m[^1]);          // к полюсу сжатия
        }
        // параллель замкнута и проходит по одному узлу на каждом меридиане
        Assert.All(lines.Parallels, p => Assert.Equal(scene.Mesh.MeridianCount + 1, p.Count));
        Assert.All(lines.Parallels, p => Assert.Equal(p[0], p[^1]));
    }

    [Fact]
    public void GridLines_EmptyWithoutAngles()
    {
        var mesh = new LimitSurfaceScene(fx.Data).Mesh;
        var bare = new LimitSurfaceMesh(mesh.Vertices, mesh.Triangles, mesh.StrainPlanes, mesh.Active, meridianOf: mesh.MeridianOf);
        var scene = new LimitSurfaceScene(new LimitSurfaceResultData
        {
            Mesh = LimitSurfaceResultData.EncodeMesh(bare), ControlPoints = fx.Data.ControlPoints,
            InitialMeridians = 12, ThetaIntervals = 12,
        });
        Assert.False(scene.HasGridLines);
        Assert.Empty(scene.GridLines().Meridians);
    }

    [Fact]
    public void SliceThroughBoundaryVertex_PassesThroughIt()
    {
        var scene = new LimitSurfaceScene(fx.Data);
        var v = scene.Mesh.Vertices[scene.Mesh.Vertices.Count / 2];
        var p = new LimitSurfacePointRecord(1, "", v.N, v.Mx, v.My);
        foreach (var kind in new[] { SliceKind.AtN, SliceKind.AtMx, SliceKind.AtMy, SliceKind.NM })
        {
            double level = LimitSurfaceScene.LevelThrough(kind, p);
            var (px, py) = scene.Project(kind, level, v);
            var loops = scene.Slice(kind, [level])[0].Loops;
            double d = loops.SelectMany(l => l).Min(q => Math.Sqrt(Math.Pow(q.X - px, 2) + Math.Pow(q.Y - py, 2)));
            double size = Math.Max(scene.Max.Mx - scene.Min.Mx, scene.Max.N - scene.Min.N);
            Assert.True(d <= 0.02 * size, $"{kind}: {d} при размере {size}");
        }
    }
}
