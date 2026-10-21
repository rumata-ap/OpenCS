using CScore.Fem.Fields;
using Xunit;

namespace CScore.Tests.Fem.Fields;

public class FieldAlignmentTests
{
    [Fact]
    public void Apply_ScaleMirrorRotateShift()
    {
        var a = new FieldAlignment(Scale: 0.01, MirrorX: true, AngleDeg: 90, Dx: 1, Dy: 2, Dz: 3);
        var p = a.Apply(new FieldVec3(100, 0, 50));   // 1 м → зеркало −1 → поворот 90° → (0, −1) → сдвиг
        Assert.Equal(1, p.X, 12);
        Assert.Equal(1, p.Y, 12);
        Assert.Equal(3.5, p.Z, 12);
    }

    [Fact]
    public void ByExtents_RecoversShift_LikeGtoExample()
    {
        var targets = Grid(-0.9, 65.639, -8.173, 32, -16.3);
        var source = targets.Select(t => new FieldVec3(t.X - 63.599, t.Y - 29.699, 132.6)).ToList();
        var fit = FieldAlignmentFit.ByExtents(new FieldAlignment(), source, targets, hasZ: true);
        Assert.Equal(63.599, fit.Alignment.Dx, 6);
        Assert.Equal(29.699, fit.Alignment.Dy, 6);
        Assert.Equal(-148.9, fit.Alignment.Dz, 6);
        Assert.True(fit.Max < 1e-6);
    }

    [Fact]
    public void ByPoints_RecoversShift_WithJitter_NoZ()
    {
        var rnd = new Random(3);
        var targets = Enumerable.Range(0, 60).Select(_ => new FieldVec3(rnd.NextDouble() * 60, rnd.NextDouble() * 35, -16.3)).ToList();
        var source = targets.Select(t => new FieldVec3(t.X - 63.6 + (rnd.NextDouble() - 0.5) * 0.4,
            t.Y - 29.7 + (rnd.NextDouble() - 0.5) * 0.4, 0)).ToList();
        var fit = FieldAlignmentFit.ByPoints(new FieldAlignment(), source, targets, hasZ: false);
        Assert.Equal(63.6, fit.Alignment.Dx, 1);
        Assert.Equal(29.7, fit.Alignment.Dy, 1);
        Assert.Equal(0, fit.Alignment.Dz);
        Assert.True(fit.Max <= 0.3);
        Assert.Equal(60, fit.Count);
    }

    static List<FieldVec3> Grid(double x0, double x1, double y0, double y1, double z)
    {
        var list = new List<FieldVec3>();
        for (int i = 0; i <= 20; i++)
            for (int j = 0; j <= 10; j++)
                list.Add(new(x0 + (x1 - x0) * i / 20, y0 + (y1 - y0) * j / 10, z));
        return list;
    }
}
