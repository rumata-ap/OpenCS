using CScore.Fem.Fields;
using Xunit;

namespace CScore.Tests.Fem.Fields;

public class PlanarPatchSplitterTests
{
    [Fact]
    public void Basement_SplitsIntoFivePatches()
    {
        var mesh = FieldTestMeshes.Basement();
        var patches = PlanarPatchSplitter.Split(mesh.Shells, angleTolDeg: 5, distanceTol: 0.05);
        Assert.Equal(5, patches.Count);
        foreach (var prefix in new[] { "slab", "wall", "ramp", "riser", "low" })
        {
            var patch = Assert.Single(patches, p => p.Shells.Any(s => s.Tag.StartsWith(prefix)));
            Assert.All(patch.Shells, s => Assert.StartsWith(prefix, s.Tag));
        }
        var slab = patches.Single(p => p.Shells[0].Tag.StartsWith("slab"));
        Assert.Equal(1, Math.Abs(slab.Normal.Z), 9);
        Assert.False(slab.IsSteep);
        Assert.True(patches.Single(p => p.Shells[0].Tag.StartsWith("wall")).IsSteep);
        var ramp = patches.Single(p => p.Shells[0].Tag.StartsWith("ramp"));
        Assert.Equal(Math.Cos(Math.PI / 6), Math.Abs(ramp.Normal.Z), 9);
    }

    [Fact]
    public void Project_IsOrthonormalInPlane()
    {
        var mesh = FieldTestMeshes.Basement();
        foreach (var p in PlanarPatchSplitter.Split(mesh.Shells, 5, 0.05))
        {
            Assert.Equal(0, p.U.Dot(p.Normal), 12);
            Assert.Equal(0, p.V.Dot(p.Normal), 12);
            Assert.Equal(0, p.U.Dot(p.V), 12);
            Assert.Equal(1, p.U.Length, 12);
            Assert.All(p.Shells.SelectMany(s => s.Nodes), n => Assert.True(p.DistanceToPlane(n) < 1e-9));
        }
    }
}
