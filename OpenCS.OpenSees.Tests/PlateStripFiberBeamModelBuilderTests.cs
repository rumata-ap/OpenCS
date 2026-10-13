using CScore.PlateStrip;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Model;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>Срез 8c: сборка OpenSees-модели полосы без запуска расчёта.</summary>
public sealed class PlateStripFiberBeamModelBuilderTests
{
    static readonly double[] Stations = [0.0, 0.25, 0.5, 0.75, 1.0];
    const double L = 8.0;

    static OpenSeesSectionModel Section() => new()
    {
        Materials = [new OpenSeesMaterialDefinition
        {
            Tag = 1,
            PositiveEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(0.01, 1e6)],
            NegativeEnvelope = [new EnvelopePoint(-0.01, -1e6), new EnvelopePoint(0, 0)]
        }],
        Fibers = [new OpenSeesFiber(0, -0.1, 0.1, 1), new OpenSeesFiber(0, 0.1, 0.1, 1)],
        GJ = 1e5
    };

    static PlateStripFiberBeamModel Build(
        StripBeamSupportScheme scheme, IReadOnlyList<StripLoad> loads,
        KnownEndActions? end = null, IReadOnlyList<StripPrescribedDisplacement>? prescribed = null,
        IReadOnlyList<OpenSeesSectionModel>? sections = null) =>
        PlateStripFiberBeamModelBuilder.Build(
            sections ?? Enumerable.Repeat(Section(), 4).ToArray(), Stations, L, scheme, loads, end,
            prescribed ?? []);

    [Fact]
    public void SupportMasks_FollowSchemeAndTorsionIsFixedEverywhere()
    {
        var scheme = new StripBeamSupportScheme(
            StripBeamEndCondition.Fixed, StripBeamEndCondition.Pinned, StripAxialRestraint.BothEnds);
        var built = Build(scheme, [new() { SourceTag = "q", QzKnM = -1.0 }]);

        Assert.True(built.IsCalculable);
        var nodes = built.Model!.Nodes;
        Assert.All(nodes, n => Assert.True(n.Fixed[3]));
        Assert.Equal([true, true, true, true, true, true], nodes[0].Fixed);
        Assert.Equal([true, true, true, true, false, false], nodes[^1].Fixed);
        Assert.Equal([false, false, false, true, false, false], nodes[2].Fixed);
        Assert.Equal(L * 0.25, nodes[1].X, 12);
    }

    [Fact]
    public void DistributedLoad_IsSplitByElementsAndConvertedToNewtons()
    {
        var load = new StripLoad
        {
            SourceTag = "patch", Kind = StripLoadKind.DistributedUniform,
            StationStartFraction = 0.375, StationEndFraction = 0.625, QzKnM = -10.0
        };
        var built = Build(StripBeamSupportScheme.SimplySupported, [load]);

        var stage = Assert.Single(built.Model!.Stages);
        Assert.Equal(2, stage.DistributedLoads.Count);
        var first = stage.DistributedLoads[0];
        Assert.Equal(2, first.ElementTag);
        Assert.Equal(0.5, first.AOverL, 12);
        Assert.Equal(1.0, first.BOverL, 12);
        Assert.Equal(-10_000.0, first.WzStart, 9);
        var second = stage.DistributedLoads[1];
        Assert.Equal(3, second.ElementTag);
        Assert.Equal(0.0, second.AOverL, 12);
        Assert.Equal(0.5, second.BOverL, 12);
    }

    [Fact]
    public void EndActions_AreAppliedWithDiagramSign()
    {
        var built = Build(
            StripBeamSupportScheme.SimplySupported, [new() { SourceTag = "q", QzKnM = -1.0 }],
            new KnownEndActions(StartN: 5.0, StartMy: 20.0, EndN: 7.0, EndMy: 30.0, EndMz: 2.0));

        var loads = built.Model!.Stages[0].Loads;
        var start = loads.Single(l => l.NodeTag == 1);
        var end = loads.Single(l => l.NodeTag == 5);
        Assert.Equal(-5_000.0, start.Fx, 9);
        Assert.Equal(-20_000.0, start.My, 9);
        Assert.Equal(7_000.0, end.Fx, 9);
        Assert.Equal(30_000.0, end.My, 9);
        Assert.Equal(2_000.0, end.Mz, 9);
    }

    [Fact]
    public void Prescribed_FreesDofAndAddsKinematicLoad()
    {
        var built = Build(
            StripBeamSupportScheme.SimplySupported, [],
            prescribed: [new StripPrescribedDisplacement(NodeIndex: 4, Dof: 2, Value: -0.02)]);

        Assert.True(built.IsCalculable);
        var node = built.Model!.Nodes[4];
        Assert.False(node.Fixed[2]);
        var kinematic = Assert.Single(built.Model.Stages[0].KinematicLoads);
        Assert.Equal((5, 3, -0.02), (kinematic.NodeTag, kinematic.Dof, kinematic.Value));
    }

    [Fact]
    public void PointLoads_StationBecomesNodalAndInteriorBecomesElementLoad()
    {
        StripLoad[] loads =
        [
            new() { SourceTag = "a", Kind = StripLoadKind.Point, StationFraction = 0.5, PzKn = -10.0 },
            new() { SourceTag = "b", Kind = StripLoadKind.Point, StationFraction = 0.375, PzKn = -4.0 },
        ];
        var built = Build(StripBeamSupportScheme.SimplySupported, loads);

        var stage = built.Model!.Stages[0];
        Assert.Equal(-10_000.0, stage.Loads.Single(l => l.NodeTag == 3).Fz, 9);
        var point = Assert.Single(stage.PointLoads);
        Assert.Equal(2, point.ElementTag);
        Assert.Equal(0.5, point.XOverL, 12);
        Assert.Equal(-4_000.0, point.Pz, 9);
    }

    [Fact]
    public void SameSectionInstance_SharesOneSectionTag()
    {
        var shared = Section();
        var built = Build(
            StripBeamSupportScheme.SimplySupported, [new() { SourceTag = "q", QzKnM = -1.0 }],
            sections: [shared, shared, Section(), shared]);

        Assert.Equal(2, built.Model!.Sections.Count);
        Assert.Equal([1, 1, 2, 1], built.Model.Elements.Select(e => e.SectionTag).ToArray());
    }

    [Fact]
    public void InvalidInputs_ProduceErrorDiagnostics()
    {
        var wrongCount = Build(
            StripBeamSupportScheme.SimplySupported, [new() { SourceTag = "q", QzKnM = -1.0 }],
            sections: [Section()]);
        Assert.False(wrongCount.IsCalculable);
        Assert.Contains(wrongCount.Diagnostics, d => d.Code == "plate_strip_fiber_model_sections");

        var noActions = Build(StripBeamSupportScheme.SimplySupported, []);
        Assert.False(noActions.IsCalculable);

        var interiorMoment = Build(
            StripBeamSupportScheme.SimplySupported,
            [new() { SourceTag = "m", Kind = StripLoadKind.Point, StationFraction = 0.3, MzKnM = 5.0 }]);
        Assert.False(interiorMoment.IsCalculable);
    }
}
