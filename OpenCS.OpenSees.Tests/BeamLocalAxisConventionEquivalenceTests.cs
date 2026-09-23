using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

/// <summary>Копия конвенции осей в CScore обязана совпадать с FemLocalAxis побитно.</summary>
public sealed class BeamLocalAxisConventionEquivalenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        var directions = new (double X, double Y, double Z)[]
        {
            (3, 0, 0), (-3, 0, 0), (0, 2, 0), (0, 0, 4), (0, 0, -4), (1, 2, 3), (-2.5, 0.7, -1.1),
            (1e-6, 0, 5), (0.3, -0.4, 0.2)
        };
        foreach (var d in directions)
            foreach (var angle in new[] { 0.0, 30.0, -45.0, 90.0, 137.5 })
                yield return [d.X, d.Y, d.Z, angle];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Frame_MatchesFemLocalAxisBitwise(double dx, double dy, double dz, double angle)
    {
        var i = new PlanarVector3(1.5, -2, 0.25);
        var j = i + new PlanarVector3(dx, dy, dz);
        var expected = FemLocalAxis.LocalFrame(
            new FemLinearNode(1, i.X, i.Y, i.Z, new bool[6]), new FemLinearNode(2, j.X, j.Y, j.Z, new bool[6]), angle);

        var actual = BeamLocalAxisConvention.Frame(i, j, angle);

        Assert.Equal(expected.X, (actual.X.X, actual.X.Y, actual.X.Z));
        Assert.Equal(expected.Y, (actual.Y.X, actual.Y.Y, actual.Y.Z));
        Assert.Equal(expected.Z, (actual.Z.X, actual.Z.Y, actual.Z.Z));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FrameProvider_MatchesFemLocalAxis(double dx, double dy, double dz, double angle)
    {
        var expected = FemLocalAxis.LocalFrame(
            new FemLinearNode(1, 0, 0, 0, new bool[6]), new FemLinearNode(2, dx, dy, dz, new bool[6]), angle);

        var frame = new BeamLocalAxisFrameProvider().Frame(new PlanarVector3(dx, dy, dz), angle);

        Assert.Equal(expected.Y.X, frame.LocalY.X, 12);
        Assert.Equal(expected.Y.Y, frame.LocalY.Y, 12);
        Assert.Equal(expected.Y.Z, frame.LocalY.Z, 12);
        Assert.Equal(expected.Z.X, frame.LocalZ.X, 12);
        Assert.Equal(expected.Z.Y, frame.LocalZ.Y, 12);
        Assert.Equal(expected.Z.Z, frame.LocalZ.Z, 12);
    }

    [Fact]
    public void ZeroLength_ThrowsLikeFemLocalAxis() =>
        Assert.Throws<InvalidOperationException>(() =>
            BeamLocalAxisConvention.Frame(new PlanarVector3(1, 1, 1), new PlanarVector3(1, 1, 1)));
}
