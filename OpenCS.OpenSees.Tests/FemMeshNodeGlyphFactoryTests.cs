using System.Windows.Media.Media3D;
using OpenCS.ViewModels;

namespace OpenCS.OpenSees.Tests;

public sealed class FemMeshNodeGlyphFactoryTests
{
    [Fact]
    public void Create_BuildsThreeOrthogonalSegmentsForEachMeshNode()
    {
        var points = FemMeshNodeGlyphFactory.Create([new Point3D(1, 2, 3)], halfSize: 0.05);

        Assert.Equal(6, points.Count);
        Assert.Equal(new Point3D(0.95, 2, 3), points[0]);
        Assert.Equal(new Point3D(1.05, 2, 3), points[1]);
        Assert.Equal(new Point3D(1, 1.95, 3), points[2]);
        Assert.Equal(new Point3D(1, 2.05, 3), points[3]);
        Assert.Equal(new Point3D(1, 2, 2.95), points[4]);
        Assert.Equal(new Point3D(1, 2, 3.05), points[5]);
    }
}
