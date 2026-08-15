using System.Windows.Media.Media3D;
using OpenCS.OpenSees.Structural;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки общих рядов узловых и силовых эпюр конструктивного стержня.</summary>
public class FemMemberResultSeriesBuilderTests
{
    [Fact]
    public void BuildNodal_SortsByArcAndSkipsElementWithMissingEndpoint()
    {
        var context = Context(
            new FemMemberMeshElement(20, 1, 2, Point(0), Point(1)),
            new FemMemberMeshElement(10, 2, 3, Point(1), Point(2)));
        var values = new Dictionary<int, FemNodeDisplacement>
        {
            [1] = new(1, 1.0, 0, 0, 0, 0, 0),
            [2] = new(2, 2.0, 0, 0, 0, 0, 0)
        };

        var series = FemMemberResultSeriesBuilder.BuildNodal(
            context, values, FemNodalComponent.Ux);

        var segment = Assert.Single(series.Segments);
        Assert.Equal(20, segment.ElementTag);
        Assert.Equal(1.0, segment.Value0, 12);
        Assert.Equal(2.0, segment.Value1, 12);
    }

    [Fact]
    public void BuildNodal_UsesGlobalUxAndRxValues()
    {
        var context = Context(new FemMemberMeshElement(10, 1, 2, Point(0), Point(2)));
        var values = new Dictionary<int, FemNodeDisplacement>
        {
            [1] = new(1, 0.125, 0, 0, 0.25, 0, 0),
            [2] = new(2, 0.375, 0, 0, 0.75, 0, 0)
        };

        var displacement = FemMemberResultSeriesBuilder.BuildNodal(
            context, values, FemNodalComponent.Ux);
        var rotation = FemMemberResultSeriesBuilder.BuildNodal(
            context, values, FemNodalComponent.Rx);

        Assert.Equal(0.125, Assert.Single(displacement.Segments).Value0, 12);
        Assert.Equal(0.25, Assert.Single(rotation.Segments).Value0, 12);
    }

    [Fact]
    public void BuildNodal_UsesElementTagForEqualArcTie()
    {
        var context = Context(
            new FemMemberMeshElement(20, 1, 2, Point(0), Point(1)),
            new FemMemberMeshElement(10, 3, 4, Point(0), Point(1)));
        var values = new Dictionary<int, FemNodeDisplacement>
        {
            [1] = new(1, 1, 0, 0, 0, 0, 0),
            [2] = new(2, 2, 0, 0, 0, 0, 0),
            [3] = new(3, 3, 0, 0, 0, 0, 0),
            [4] = new(4, 4, 0, 0, 0, 0, 0)
        };

        var series = FemMemberResultSeriesBuilder.BuildNodal(
            context, values, FemNodalComponent.Ux);

        Assert.Equal([10, 20], series.Segments.Select(s => s.ElementTag));
    }

    [Fact]
    public void BuildForces_PreservesExistingMzSignConvention()
    {
        var context = Context(new FemMemberMeshElement(7, 1, 2, Point(0), Point(1)));
        var forces = new Dictionary<int, FemElementEndForces>
        {
            [7] = new(7, 0, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 4)
        };

        var series = FemMemberResultSeriesBuilder.BuildForces(
            context, forces, FemForceComponent.Mz);

        var segment = Assert.Single(series.Segments);
        Assert.Equal(3.0, segment.Value0, 12);
        Assert.Equal(-4.0, segment.Value1, 12);
    }

    [Fact]
    public void BuildForces_AndLoadItemUseTheSameCanonicalEndpointValues()
    {
        var force = new FemElementEndForces(
            7, 1000, 2000, 3000, 4000, 5000, 6000,
            7000, 8000, 9000, 10000, 11000, 12000);
        var context = Context(new FemMemberMeshElement(7, 1, 2, Point(0), Point(1)));
        var canonical = FemForceEndpointConverter.Convert(
            force, FemForceEndpointSignPolicy.OpenSeesDefault).Start;
        var item = FemForceEndpointConverter.ToLoadItem(canonical, 1, "node 1");

        foreach (var component in Enum.GetValues<FemForceComponent>())
        {
            var series = FemMemberResultSeriesBuilder.BuildForces(
                context, new Dictionary<int, FemElementEndForces> { [7] = force }, component);
            Assert.Equal(
                FemForceEndpointConverter.ReadComponent(canonical, component),
                Assert.Single(series.Segments).Value0,
                12);
        }

        Assert.Equal(canonical.N / 1000.0, item.N, 12);
        Assert.Equal(canonical.Mz / 1000.0, item.Mx, 12);
        Assert.Equal(canonical.My / 1000.0, item.My, 12);
        Assert.Equal(canonical.Qz / 1000.0, item.Vx, 12);
        Assert.Equal(canonical.Qy / 1000.0, item.Vy, 12);
        Assert.Equal(canonical.Mx / 1000.0, item.T, 12);
    }

    [Fact]
    public void Scale_ConvertsDisplacementsAndRotationsWithoutMutatingRawSeries()
    {
        var raw = new FemDiagramSeries([
            new FemDiagramSegment(1, 0, 1, 0.002, 0.004)]);

        var displacement = FemDiagramValueScaler.Scale(
            raw, FemResultGroup.Displacements, FemLengthUnit.Centimeters, FemRotationScale.One);
        var rotation = FemDiagramValueScaler.Scale(
            raw, FemResultGroup.Rotations, FemLengthUnit.Meters, FemRotationScale.OneHundred);

        Assert.Equal(0.2, displacement.Segments[0].Value0, 12);
        Assert.Equal(0.004 * 100, rotation.Segments[0].Value1, 12);
        Assert.Equal(0.002, raw.Segments[0].Value0, 12);
    }

    static FemMemberGeometryContext Context(params FemMemberMeshElement[] elements) =>
        new("M1", Point(0), new Vector3D(1, 0, 0), elements);

    static Point3D Point(double x) => new(x, 0, 0);
}
