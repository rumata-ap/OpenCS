using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarKindClassifierTests
{
    [Fact]
    public void Classify_ReturnsPlate_ForHorizontalRegion()
    {
        var kind = PlanarKindClassifier.Classify(Frame3D.Identity, out var ambiguous);
        Assert.Equal("plate", kind);
        Assert.False(ambiguous);
    }

    [Fact]
    public void Classify_ReturnsWall_ForVerticalRegion()
    {
        var verticalFrame = new Frame3D(
            PlanarVector3.Zero,
            new PlanarVector3(1, 0, 0),
            new PlanarVector3(0, 0, 1),
            new PlanarVector3(0, -1, 0));

        var kind = PlanarKindClassifier.Classify(verticalFrame, out var ambiguous);

        Assert.Equal("wall", kind);
        Assert.False(ambiguous);
    }

    [Fact]
    public void Classify_MarksAmbiguous_NearFortyFiveDegrees()
    {
        // Нормаль под ~45° к вертикали: LocalZ = (sqrt(2)/2, 0, sqrt(2)/2).
        double s = Math.Sqrt(2) / 2;
        var frame45 = new Frame3D(
            PlanarVector3.Zero,
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(-s, 0, s),
            new PlanarVector3(s, 0, s));

        PlanarKindClassifier.Classify(frame45, out var ambiguous);

        Assert.True(ambiguous);
    }

    [Fact]
    public void FemMember_DefaultsToAutoKindSourceAndNullKind()
    {
        var member = new CScore.Fem.FemMember();
        Assert.Equal("auto", member.KindSource);
        Assert.Null(member.Kind);
        Assert.Null(member.PlanarRegionId);
    }
}
