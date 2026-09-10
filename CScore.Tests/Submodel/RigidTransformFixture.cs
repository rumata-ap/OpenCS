using CScore.Planar;
using CScore.Submodel;

namespace CScore.Tests.Submodel;

/// <summary>Жёсткое движение для проверки инвариантности анализа к ориентации.</summary>
public static class RigidTransformFixture
{
    static readonly PlanarVector3 Axis = new PlanarVector3(0.37, -0.62, 0.69).Normalize();
    const double AngleRad = 0.9128;
    static readonly PlanarVector3 Shift = new(-3.14, 7.5, 2.71);

    public static PlanarVector3 Apply(PlanarVector3 p)
    {
        var c = Math.Cos(AngleRad);
        var s = Math.Sin(AngleRad);
        return p * c + Axis.Cross(p) * s + Axis * (Axis.Dot(p) * (1 - c)) + Shift;
    }

    public static BeamSegmentInput Apply(BeamSegmentInput segment) =>
        segment with { Start = Apply(segment.Start), End = Apply(segment.End) };

    public static IReadOnlyList<BeamSegmentInput> Apply(IReadOnlyList<BeamSegmentInput> segments) =>
        segments.Select(Apply).ToList();

    public static EnvironmentElement Apply(EnvironmentElement element) =>
        element with { NodePoints = element.NodePoints.Select(Apply).ToList() };

    public static BeamSegmentInput Segment(string key, double x0, double x1, double betaDeg = 0) =>
        new(key, new PlanarVector3(x0, 0, 0), new PlanarVector3(x1, 0, 0), betaDeg, BetaSource.Member, "m1");
}
