using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class ShellContactTests
{
    static readonly ResolvedTolerances Tol = ChainTolerances.Default.Resolve(4.0);
    static ShellContactKind Classify(IReadOnlyList<PlanarVector3> points, PlanarVector3 probe) =>
        ShellContactGeometry.Classify(points.Select(RigidTransformFixture.Apply).ToList(), RigidTransformFixture.Apply(probe), Tol);
    static readonly IReadOnlyList<PlanarVector3> Triangle = [new(0,0,0), new(1,0,0), new(0,1,0)];
    static readonly IReadOnlyList<PlanarVector3> Quad = [new(0,0,0), new(1,0,0), new(1,1,0), new(0,1,0)];
    [Fact] public void Classify_ProbeAtVertex_IsVertex() => Assert.Equal(ShellContactKind.Vertex, Classify(Triangle, new(1,0,0)));
    [Fact] public void Classify_ProbeOnEdge_IsEdge() => Assert.Equal(ShellContactKind.Edge, Classify(Triangle, new(.5,0,0)));
    [Fact] public void Classify_ProbeInsideTriangle_IsFace() => Assert.Equal(ShellContactKind.Face, Classify(Triangle, new(.25,.25,0)));
    [Fact] public void Classify_ProbeOutside_IsNone() => Assert.Equal(ShellContactKind.None, Classify(Triangle, new(5,5,0)));
    [Fact] public void Classify_ProbeInsidePlanarQuad_IsFace() => Assert.Equal(ShellContactKind.Face, Classify(Quad, new(.5,.5,0)));
    [Fact] public void Classify_WarpedQuad_IsReportedAsWarped()
    {
        IReadOnlyList<PlanarVector3> warped = [new(0,0,0),new(1,0,0),new(1,1,.4),new(0,1,0)];
        Assert.Equal(ShellContactKind.Warped, Classify(warped, new(.5,.5,.1)));
        Assert.Equal(ShellContactKind.Vertex, Classify(warped, new(1,0,0)));
    }
    [Fact] public void Classify_DegenerateShell_IsDegenerate() => Assert.Equal(ShellContactKind.Degenerate, Classify([new(0,0,0),new(0,0,0),new(1,0,0)], new(0,0,0)));
}
