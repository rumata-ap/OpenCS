using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 7, Task 8: частичная замена региона CoupledWithExplicitPartition.</summary>
public sealed class ShellReplacementPartitionTests
{
    [Fact]
    public void ValidPartition_WithCoveredBoundary_PassesCleanly()
    {
        var manifest = Partition();

        var result = ShellReplacementDoubleCountingCheck.CheckPartition(manifest, [Interface("b1"), Interface("b2")]);

        Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void PartitionPolicyWithoutPolygon_IsRejected()
    {
        var manifest = Partition() with { };
        manifest = new ShellReplacementManifest(
            manifest.StripId, manifest.SourceRegionId, ShellReplacementPolicy.CoupledWithExplicitPartition,
            manifest.ReplacedRegionPolygon, manifest.StripLoadSourceTags);

        var result = ShellReplacementDoubleCountingCheck.CheckPartition(manifest, []);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_partition_polygon_required");
    }

    [Theory]
    [InlineData(ShellReplacementPolicy.DiagnosticOnly)]
    [InlineData(ShellReplacementPolicy.ReplaceShellRegion)]
    public void PolygonOnOtherPolicies_IsRejected(ShellReplacementPolicy policy)
    {
        var manifest = new ShellReplacementManifest("strip-1", 10, policy, Corridor(), [])
        {
            PartitionPolygon = PartitionPolygon()
        };

        var result = ShellReplacementDoubleCountingCheck.CheckPartition(manifest, []);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_partition_polygon_unexpected");
    }

    [Fact]
    public void PartitionOutsideCorridor_IsRejected()
    {
        var manifest = Partition(partition:
        [
            new PlanarPoint2D(1.0, -0.5), new PlanarPoint2D(9.0, -0.5),
            new PlanarPoint2D(9.0, 0.5), new PlanarPoint2D(1.0, 0.5)
        ]);

        var result = ShellReplacementDoubleCountingCheck.CheckPartition(manifest, [Interface("b1"), Interface("b2")]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_partition_outside_corridor");
    }

    [Fact]
    public void UncoveredPartitionBoundary_IsRejected()
    {
        // У разбиения два внутренних ребра, объявлен только один интерфейс.
        var manifest = Partition(interfaceIds: ["b1"]);

        var result = ShellReplacementDoubleCountingCheck.CheckPartition(manifest, [Interface("b1")]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_partition_boundary_uncovered");
    }

    [Fact]
    public void DeclaredButMissingInterface_IsRejected()
    {
        var manifest = Partition();

        var result = ShellReplacementDoubleCountingCheck.CheckPartition(manifest, [Interface("b1")]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_partition_boundary_uncovered");
    }

    [Fact]
    public void LoadInsidePartitionStillOnShell_IsDoubleCount()
    {
        var manifest = Partition(loadTags: ["q_strip"]);

        var result = ShellReplacementDoubleCountingCheck.CheckLoads(manifest, ["q_strip"]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_shell_replacement_load_double_count");
    }

    [Fact]
    public void LoadOutsidePartitionRemovedFromShell_IsOrphaned()
    {
        var manifest = Partition(loadTags: ["q_strip"]);

        var result = ShellReplacementDoubleCountingCheck.CheckLoads(
            manifest, loadsStillActiveOnShell: [], retainedLoadTags: ["q_rest"]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_partition_load_orphaned");
    }

    [Fact]
    public void ConsistentLoadOwnership_Passes()
    {
        var manifest = Partition(loadTags: ["q_strip"]);

        var result = ShellReplacementDoubleCountingCheck.CheckLoads(
            manifest, loadsStillActiveOnShell: ["q_rest"], retainedLoadTags: ["q_rest"]);

        Assert.True(result.IsCalculable);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void OverlappingPartitionsOfSameRegion_AreRejected()
    {
        var first = Partition(stripId: "strip-1");
        var second = Partition(stripId: "strip-2");

        var result = ShellReplacementDoubleCountingCheck.CheckStiffness([first, second]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_partition_overlap");
    }

    [Fact]
    public void DisjointPartitionsOfSameRegion_AreAccepted()
    {
        var first = Partition(stripId: "strip-1", partition:
        [
            new PlanarPoint2D(1.0, -0.5), new PlanarPoint2D(2.0, -0.5),
            new PlanarPoint2D(2.0, 0.5), new PlanarPoint2D(1.0, 0.5)
        ]);
        var second = Partition(stripId: "strip-2", partition:
        [
            new PlanarPoint2D(4.0, -0.5), new PlanarPoint2D(5.0, -0.5),
            new PlanarPoint2D(5.0, 0.5), new PlanarPoint2D(4.0, 0.5)
        ]);

        var result = ShellReplacementDoubleCountingCheck.CheckStiffness([first, second]);

        Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
    }

    /// <summary>Обратная совместимость Среза 5: прежние политики не изменили поведение.</summary>
    [Fact]
    public void ExistingPolicies_AreUnchanged()
    {
        var replace = new ShellReplacementManifest(
            "strip-1", 10, ShellReplacementPolicy.ReplaceShellRegion, Corridor(), ["q"]);
        var diagnostic = new ShellReplacementManifest(
            "strip-1", 10, ShellReplacementPolicy.DiagnosticOnly, Corridor(), ["q"]);

        Assert.Contains(ShellReplacementDoubleCountingCheck.CheckLoads(replace, ["q"]).Diagnostics,
            d => d.Code == "plate_strip_shell_replacement_load_double_count");
        Assert.True(ShellReplacementDoubleCountingCheck.CheckLoads(replace, []).IsCalculable);
        Assert.Contains(ShellReplacementDoubleCountingCheck.CheckLoads(diagnostic, []).Diagnostics,
            d => d.Code == "plate_strip_shell_replacement_diagnostic_incomplete");
        Assert.True(ShellReplacementDoubleCountingCheck.CheckLoads(diagnostic, ["q"]).IsCalculable);
    }

    static ShellReplacementManifest Partition(
        string stripId = "strip-1",
        IReadOnlyList<PlanarPoint2D>? partition = null,
        IReadOnlyList<string>? interfaceIds = null,
        IReadOnlyList<string>? loadTags = null) =>
        new(stripId, 10, ShellReplacementPolicy.CoupledWithExplicitPartition, Corridor(), loadTags ?? [])
        {
            PartitionPolygon = partition ?? PartitionPolygon(),
            BoundaryInterfaceIds = interfaceIds ?? ["b1", "b2"]
        };

    /// <summary>Коридор полосы: 6 × 2 м.</summary>
    static IReadOnlyList<PlanarPoint2D> Corridor() =>
    [
        new(0.0, -1.0), new(6.0, -1.0), new(6.0, 1.0), new(0.0, 1.0)
    ];

    /// <summary>Разбиение в середине коридора: два ребра лежат на границе коридора,
    /// два внутренних требуют интерфейсов.</summary>
    static IReadOnlyList<PlanarPoint2D> PartitionPolygon() =>
    [
        new(2.0, -1.0), new(4.0, -1.0), new(4.0, 1.0), new(2.0, 1.0)
    ];

    static StripBoundaryInterface Interface(string id) => new()
    {
        Id = id,
        StripId = "strip-1",
        Geometry = new PlanarConstraintGeometry(
            PlanarConstraintGeometryKind.Curve, [new PlanarPoint2D(2.0, -1.0), new PlanarPoint2D(2.0, 1.0)]),
        NormalFromReplacedToRetained = new PlanarVector3(-1, 0, 0),
        ModeByDof = PlanarBoundaryModeByDof.None,
    };
}
