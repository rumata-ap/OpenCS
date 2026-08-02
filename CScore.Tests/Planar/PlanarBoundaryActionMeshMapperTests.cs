using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public sealed class PlanarBoundaryActionMeshMapperTests
{
    [Fact]
    public void CutMapperBuildsOrderedChainFromBoundaryMapping()
    {
        var key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 2);
        var snapshot = Snapshot([0, 1, 2], [new() { Key = key, NodeIndices = [0, 1, 2] }]);
        var cut = Cut(key);

        var result = PlanarCutInterfaceMeshMapper.Map(cut, snapshot);

        Assert.True(result.IsCalculable, Diagnostics(result.Diagnostics));
        Assert.Equal([0, 1, 2], result.Mapping!.OrderedNodes.Select(node => node.NodeIndex));
        Assert.Equal([0.0, 0.5, 1.0], result.Mapping.OrderedNodes.Select(node => node.S), new DoubleComparer(1e-12));
    }

    [Fact]
    public void CutMapperReversesChainToMatchCutGeometry()
    {
        var key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 2);
        var snapshot = Snapshot([0, 1, 2], [new() { Key = key, NodeIndices = [2, 1, 0] }]);
        var cut = Cut(key);

        var result = PlanarCutInterfaceMeshMapper.Map(cut, snapshot);

        Assert.True(result.IsCalculable, Diagnostics(result.Diagnostics));
        Assert.Equal([0, 1, 2], result.Mapping!.OrderedNodes.Select(node => node.NodeIndex));
    }

    [Fact]
    public void Map_LinearBoundaryForcePreservesForceAndMoment()
    {
        var key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 2);
        var snapshot = Snapshot([0, 1, 2], [new() { Key = key, NodeIndices = [0, 1, 2] }]);
        var cut = Cut(key);
        var mapping = Assert.IsType<PlanarCutInterfaceMeshMapping>(
            PlanarCutInterfaceMeshMapper.Map(cut, snapshot).Mapping);
        var actions = new PlanarBoundaryActionSet
        {
            SourceMode = PlanarBoundaryActionSourceMode.Template,
            ForceActions =
            [
                new PlanarBoundaryForceAction
                {
                    InterfaceId = "top",
                    DofMask = PlanarDofMask.UZ,
                    Samples =
                    [
                        new(0, new(0, 0, 2), PlanarVector3.Zero),
                        new(1, new(0, 0, 2), PlanarVector3.Zero)
                    ]
                }
            ]
        };

        var result = PlanarBoundaryActionMeshMapper.Map(cut, snapshot, actions, mapping);

        Assert.True(result.IsCalculable, Diagnostics(result.Diagnostics));
        Assert.Equal(new(0, 0, 4), result.AppliedForceGlobal);
        Assert.Equal(new(0, -4, 0), result.AppliedMomentGlobal);
        Assert.Equal(result.AppliedForceGlobal, result.MappedForceGlobal);
        Assert.Equal(result.AppliedMomentGlobal, result.MappedMomentGlobal);
        Assert.Equal(1, result.NodalActions.Single(action => action.NodeIndex == 0).ForceGlobal.Z, 10);
        Assert.Equal(2, result.NodalActions.Single(action => action.NodeIndex == 1).ForceGlobal.Z, 10);
        Assert.Equal(1, result.NodalActions.Single(action => action.NodeIndex == 2).ForceGlobal.Z, 10);
    }

    [Fact]
    public void Map_KinematicActionInterpolatesAtMappedNodes()
    {
        var key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 2);
        var snapshot = Snapshot([0, 1, 2], [new() { Key = key, NodeIndices = [0, 1, 2] }]);
        var cut = Cut(key);
        var mapping = Assert.IsType<PlanarCutInterfaceMeshMapping>(
            PlanarCutInterfaceMeshMapper.Map(cut, snapshot).Mapping);
        var actions = new PlanarBoundaryActionSet
        {
            SourceMode = PlanarBoundaryActionSourceMode.Template,
            KinematicActions =
            [
                new PlanarBoundaryKinematicAction
                {
                    InterfaceId = "top",
                    DofMask = PlanarDofMask.UZ,
                    Samples =
                    [
                        new(0, PlanarVector3.Zero, PlanarVector3.Zero),
                        new(1, new(0, 0, 0.02), PlanarVector3.Zero)
                    ]
                }
            ]
        };

        var result = PlanarBoundaryActionMeshMapper.Map(cut, snapshot, actions, mapping);

        Assert.True(result.IsCalculable, Diagnostics(result.Diagnostics));
        Assert.Equal(0, result.PrescribedDofs[(0, 2)], 10);
        Assert.Equal(0.01, result.PrescribedDofs[(1, 2)], 10);
        Assert.Equal(0.02, result.PrescribedDofs[(2, 2)], 10);
    }

    [Fact]
    public void Map_ForceReferencePointPreservesMomentAboutGlobalOrigin()
    {
        var key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 2);
        var snapshot = Snapshot([0, 1, 2], [new() { Key = key, NodeIndices = [0, 1, 2] }]);
        var cut = Cut(key);
        var mapping = Assert.IsType<PlanarCutInterfaceMeshMapping>(
            PlanarCutInterfaceMeshMapper.Map(cut, snapshot).Mapping);
        var actions = new PlanarBoundaryActionSet
        {
            SourceMode = PlanarBoundaryActionSourceMode.Template,
            ForceActions =
            [
                new PlanarBoundaryForceAction
                {
                    InterfaceId = "top",
                    DofMask = PlanarDofMask.UZ,
                    ReferencePoint = new(0, 1, 0),
                    Samples = [new(0, new(0, 0, 2), PlanarVector3.Zero)]
                }
            ]
        };

        var result = PlanarBoundaryActionMeshMapper.Map(cut, snapshot, actions, mapping);

        Assert.True(result.IsCalculable, Diagnostics(result.Diagnostics));
        Assert.Equal(result.AppliedMomentGlobal, result.MappedMomentGlobal);
        Assert.Equal(4, result.MappedMomentGlobal.X, 10);
    }

    [Fact]
    public void Map_RejectsStaleMappingFingerprint()
    {
        var key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 2);
        var snapshot = Snapshot([0, 1, 2], [new() { Key = key, NodeIndices = [0, 1, 2] }]);
        var cut = Cut(key);
        var mapping = new PlanarCutInterfaceMeshMapping
        {
            InterfaceId = "top",
            SnapshotId = 99,
            SnapshotFingerprint = "old",
            OrderedNodes = [],
            OrderedEdges = []
        };

        var result = PlanarBoundaryActionMeshMapper.Map(
            cut,
            snapshot,
            new PlanarBoundaryActionSet { SourceMode = PlanarBoundaryActionSourceMode.Template },
            mapping);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "planar_boundary_mapping_stale");
    }

    static PlanarCutInterface Cut(PlanarBoundaryKey? boundaryKey = null) => new()
    {
        Id = "top",
        Geometry = new PlanarConstraintGeometry(
            PlanarConstraintGeometryKind.Curve,
            [new(0, 0), new(2, 0)]),
        NormalFromFragmentToOmittedSide = new(0, 1, 0),
        ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Free),
        BoundaryKey = boundaryKey
    };

    static PlanarMeshSnapshot Snapshot(
        IReadOnlyList<int> indices,
        IReadOnlyList<PlanarMeshBoundaryMapping> boundaries) => new()
    {
        Id = 7,
        InputFingerprint = "snapshot-fingerprint",
        IsCalculable = true,
        Nodes = indices.Select((index, position) =>
            new PlanarMeshNode(index, position, 0, position, 0, 0)).ToArray(),
        BoundaryMappings = boundaries
    };

    static string Diagnostics(IEnumerable<CScore.Fem.FemValidationDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message));

    sealed class DoubleComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;
        public int GetHashCode(double obj) => 0;
    }
}
