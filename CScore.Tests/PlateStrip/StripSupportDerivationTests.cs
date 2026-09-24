using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripSupportDerivationTests
{
    static PlanarRegion Region()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [-10, 20, 20, -10], Y = [-10, -10, 10, 10] }, frame: Frame3D.Identity);
        region.Id = 1;
        return region;
    }

    static SupportLocus At(double x, double y) => new()
    {
        Frame = new Frame3D(new(x, y, 0), Frame3D.Identity.LocalX, Frame3D.Identity.LocalY, Frame3D.Identity.LocalZ)
    };

    /// <summary>Полоса от (0,0) до (L·cos a, L·sin a).</summary>
    static PlateStripBeamAnalogy Strip(double angleDeg = 0.0, double length = 6.0)
    {
        double a = angleDeg * Math.PI / 180.0;
        var result = PlateStripGeometryBuilder.Build(
            "strip", Region(), At(0, 0), At(length * Math.Cos(a), length * Math.Sin(a)), 1.0);
        Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        return result.Analogy!;
    }

    static StripSupportCandidate Nodal(string tag, double u, double v, params int[] restrained)
    {
        var dofs = new bool[6];
        foreach (int k in restrained) dofs[k] = true;
        var source = new PlanarBoundarySourceReference("fem_node", tag);
        return new(StripSupportCandidate.BuildId(StripSupportKind.NodalRestraint, source, 0),
            StripSupportKind.NodalRestraint,
            new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Point, [new(u, v)]), dofs, source);
    }

    static StripSupportCandidate Column(string tag, double u, double v)
    {
        var source = new PlanarBoundarySourceReference("fem_member", tag);
        return new(StripSupportCandidate.BuildId(StripSupportKind.Column, source, 0),
            StripSupportKind.Column,
            new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Point, [new(u, v)]), new bool[6], source);
    }

    static StripSupportCandidate Wall(string tag, double u, double v0, double v1)
    {
        var source = new PlanarBoundarySourceReference("planar_region", tag);
        return new(StripSupportCandidate.BuildId(StripSupportKind.Wall, source, 0),
            StripSupportKind.Wall,
            new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, [new(u, v0), new(u, v1)]),
            new bool[6], source);
    }

    const int UX = 0, UY = 1, UZ = 2, RX = 3, RY = 4, RZ = 5;

    [Fact]
    public void NoCandidates_BothEndsNotFound()
    {
        var result = StripSupportDerivation.Derive(Strip(), Frame3D.Identity, []);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Scheme);
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == "plate_strip_support_not_found" && d.IsError));
    }

    [Fact]
    public void ColumnsAtBothEnds_PinnedWithParentMomentsAndEndAxialForce()
    {
        var result = StripSupportDerivation.Derive(
            Strip(), Frame3D.Identity, [Column("C1", 0, 0), Column("C2", 6, 0)]);

        Assert.True(result.IsCalculable);
        Assert.Equal(
            new StripBeamSupportScheme(StripBeamEndCondition.Pinned, StripBeamEndCondition.Pinned, StripAxialRestraint.StartOnly),
            result.Scheme);
        Assert.Equal(StripEndActionComponents.My | StripEndActionComponents.Mz, result.Start!.TransferFromParent);
        Assert.Equal(StripEndActionComponents.N | StripEndActionComponents.My | StripEndActionComponents.Mz,
            result.End!.TransferFromParent);
        Assert.Equal(StripSupportKind.Column, result.Start.Kind);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void NodalRestraintWithAllRotations_IsFixedWithoutWarning()
    {
        var result = StripSupportDerivation.Derive(
            Strip(), Frame3D.Identity, [Nodal("1", 0, 0, UZ, RX, RY, RZ), Column("C2", 6, 0)]);

        Assert.Equal(StripBeamEndCondition.Fixed, result.Scheme!.StartCondition);
        Assert.Equal(StripEndActionComponents.None, result.Start!.TransferFromParent);
        Assert.Equal(StripSupportKind.NodalRestraint, result.Start.Kind);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "plate_strip_support_partial_rotation_fixity");
    }

    [Fact]
    public void OnlyBendingRotationFixed_IsFixedWithPartialFixityWarning()
    {
        var result = StripSupportDerivation.Derive(
            Strip(), Frame3D.Identity, [Nodal("1", 0, 0, UZ, RY), Column("C2", 6, 0)]);

        Assert.True(result.IsCalculable);
        Assert.Equal(StripBeamEndCondition.Fixed, result.Scheme!.StartCondition);
        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal("plate_strip_support_partial_rotation_fixity", warning.Code);
        Assert.False(warning.IsError);
    }

    [Fact]
    public void StripAt45Degrees_WithOnlyRyFixed_IsPinned()
    {
        // Ось LocalY полосы под 45° имеет проекции на глобальные X и Y — нужен и RX, и RY.
        var strip = Strip(angleDeg: 45.0);
        double e = 6.0 / Math.Sqrt(2.0);

        var result = StripSupportDerivation.Derive(
            strip, Frame3D.Identity, [Nodal("1", 0, 0, UZ, RY), Column("C2", e, e)]);

        Assert.Equal(StripBeamEndCondition.Pinned, result.Scheme!.StartCondition);

        var both = StripSupportDerivation.Derive(
            strip, Frame3D.Identity, [Nodal("1", 0, 0, UZ, RX, RY, RZ), Column("C2", e, e)]);
        Assert.Equal(StripBeamEndCondition.Fixed, both.Scheme!.StartCondition);
    }

    [Fact]
    public void AxialRestraintAtBothEnds_IsBothEndsAndNoAxialTransfer()
    {
        var result = StripSupportDerivation.Derive(
            Strip(), Frame3D.Identity, [Nodal("1", 0, 0, UX, UZ), Nodal("2", 6, 0, UX, UY, UZ)]);

        Assert.Equal(StripAxialRestraint.BothEnds, result.Scheme!.AxialRestraint);
        Assert.False(result.End!.TransferFromParent.HasFlag(StripEndActionComponents.N));
    }

    [Theory]
    [InlineData(0.03, true)]
    [InlineData(0.07, false)]
    public void WallMatch_UsesDistanceToSegment(double offset, bool found)
    {
        var result = StripSupportDerivation.Derive(
            Strip(), Frame3D.Identity, [Wall("W1", -offset, -1, 1), Column("C2", 6, 0)]);

        Assert.Equal(found, result.IsCalculable);
        if (found) Assert.Equal(StripSupportKind.Wall, result.Start!.Kind);
    }

    [Fact]
    public void ColumnsAboveAndBelow_OneKindTwoReferences()
    {
        var strip = Strip();
        var result = StripSupportDerivation.Derive(
            strip, Frame3D.Identity, [Column("C1", 0, 0), Column("C1b", 0, 0), Column("C2", 6, 0)]);

        result.ApplyTo(strip);

        Assert.Equal(StripSupportKind.Column, strip.StartSupportLocus.Kind);
        Assert.Equal(["C1", "C1b"], strip.StartSupportLocus.SourceReferences.Select(r => r.SourceId));
        Assert.Single(strip.EndSupportLocus.SourceReferences);
    }

    [Fact]
    public void ApplyTo_KeepsFrameAndStructuralMode()
    {
        var strip = Strip();
        strip.StartSupportLocus.StructuralMode = BeamJunctionMode.Tie;
        var frame = strip.StartSupportLocus.Frame;

        StripSupportDerivation.Derive(strip, Frame3D.Identity, [Column("C1", 0, 0), Column("C2", 6, 0)])
            .ApplyTo(strip);

        Assert.Same(frame, strip.StartSupportLocus.Frame);
        Assert.Equal(BeamJunctionMode.Tie, strip.StartSupportLocus.StructuralMode);
    }

    [Fact]
    public void ApplyTo_NotCalculable_Throws()
    {
        var result = StripSupportDerivation.Derive(Strip(), Frame3D.Identity, []);

        Assert.Throws<InvalidOperationException>(() => result.ApplyTo(Strip()));
    }
}
