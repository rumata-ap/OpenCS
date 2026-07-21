using System.Windows.Media.Media3D;
using CScore;
using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.OpenSees.Tests;

public sealed class FemSectionGlyphFactoryTests
{
    [Fact]
    public void Create_UsesMemberMidpointAndNewLocalFrame()
    {
        var members = new[]
        {
            new FemMember
            {
                ElemTag = "7", ElemType = "beam", NodeIdsJson = "[1,2]", RotationDeg = 0
            }
        };
        var nodes = new Dictionary<string, Point3D>
        {
            ["1"] = new(0, 0, 0),
            ["2"] = new(3, 0, 0)
        };

        var glyph = Assert.Single(FemSectionGlyphFactory.Create(members, [], nodes));

        Assert.Equal(new Point3D(1.5, 0, 0), glyph.Center);
        Assert.Equal(new Vector3D(1, 0, 0), glyph.LocalX);
        Assert.Equal(new Vector3D(0, 0, 1), glyph.LocalY);
        Assert.Equal(new Vector3D(0, -1, 0), glyph.LocalZ);
        Assert.Equal(0, glyph.RotationDeg);
        Assert.Empty(glyph.Contours);
        Assert.Equal(0.24, glyph.FallbackHalfSize, 12);
    }

    [Fact]
    public void Create_MapsCrossSectionContourToLocalYZ()
    {
        var member = new FemMember
        {
            ElemTag = "7", ElemType = "beam", NodeIdsJson = "[1,2]", CrossSectionId = 9
        };
        var contour = new Contour(
            new[] { -0.2, 0.2, 0.2, -0.2, -0.2 },
            new[] { -0.1, -0.1, 0.1, 0.1, -0.1 },
            "outer") { Type = ContourType.Hull };
        var sections = new[]
        {
            new CrossSection
            {
                Id = 9,
                Areas = [new MaterialArea { Contours = [contour] }]
            }
        };
        var nodes = new Dictionary<string, Point3D>
        {
            ["1"] = new(0, 0, 0),
            ["2"] = new(3, 0, 0)
        };

        var glyph = Assert.Single(FemSectionGlyphFactory.Create([member], sections, nodes));
        var points = Assert.Single(glyph.Contours);

        Assert.Equal((-0.1, -0.2), points[0]);
        Assert.Equal((0.1, 0.2), points[2]);
    }
}
