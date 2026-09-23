using CScore.Fem;
using CScore.Fem.Combinations;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Submodel.BoundaryTestModels;

namespace CScore.Tests.Submodel;

public sealed class SubmodelLoadClassifierTests
{
    static SubmodelLoadClassification Classify(
        IReadOnlyList<FemMemberLoad>? memberLoads = null,
        IReadOnlyList<FemNodeLoad>? nodeLoads = null,
        IReadOnlyList<FemKinematicLoad>? kinematic = null,
        (string, bool)[]? chainSpec = null,
        string sourceType = "internal")
    {
        var parent = Beam();
        var diagnostics = new List<FemValidationDiagnostic>();
        var chain = SubmodelChainTopology.Build(Extraction(parent, chainSpec ?? MiddleChain), parent.MeshElements, diagnostics)!;
        Assert.Empty(diagnostics);
        return SubmodelLoadClassifier.Classify(chain, sourceType, parent.Nodes, parent.Members, parent.MeshNodes,
            parent.MeshElements, new FemResolvedLoads(nodeLoads ?? [], memberLoads ?? [], kinematic ?? []));
    }

    [Fact]
    public void ChainTopology_FindsEndsAndInterior_ForBothDirections()
    {
        var parent = Beam();
        var forward = SubmodelChainTopology.Build(Extraction(parent, MiddleChain), parent.MeshElements, [])!;
        var reversed = SubmodelChainTopology.Build(Extraction(parent, MiddleChainReversed), parent.MeshElements, [])!;

        Assert.Equal(("102", "104"), (forward.StartParentNodeTag, forward.EndParentNodeTag));
        Assert.Equal(("104", "102"), (reversed.StartParentNodeTag, reversed.EndParentNodeTag));
        Assert.True(forward.IsInterior("103"));
        Assert.False(forward.IsInterior("101"));
    }

    [Fact]
    public void UniformLoadOnMember_RetainedOnChainSegments_InGlobalComponents()
    {
        var result = Classify(memberLoads:
        [
            new FemMemberLoad { Id = 5, MemberId = 10, DistributionType = "uniform", CoordinateSystem = "local", QyStart = -10 }
        ]);

        Assert.Equal(new[] { "202", "203" }, result.RetainedDistributed.Select(l => l.ChildElementTag));
        Assert.All(result.RetainedDistributed, l =>
        {
            Assert.Equal((0.0, 1.0), (l.AOverL, l.BOverL));
            // Для стержня вдоль X локальная y = глобальная Z (FemLocalAxis).
            Assert.Equal(-10, l.QAtA.Z, 12);
            Assert.Equal(0, l.QAtA.Y, 12);
            Assert.Equal(5, l.SourceMemberLoadId);
        });
        Assert.Equal(new LoadAccounting(2, 0, 0), result.Accounting);
        Assert.Equal(LoadCompleteness.Known, result.Completeness);
        Assert.DoesNotContain(result.Diagnostics, d => d.IsError);
    }

    [Fact]
    public void LoadOutsideChain_IsDiscarded()
    {
        var result = Classify(memberLoads:
        [
            new FemMemberLoad { Id = 5, MemberId = 10, DistributionType = "uniform", CoordinateSystem = "global", EndOffsetM = 7, QzStart = -1 }
        ]);

        Assert.Empty(result.RetainedDistributed);
        Assert.Equal(new LoadAccounting(0, 0, 1), result.Accounting);
    }

    [Fact]
    public void TrapezoidCrossingChainBoundary_IsCutAtTheBoundary()
    {
        // Участок x = 1..5, q от -1000 до -5000: на цепочке остаются x = 2..5.
        var result = Classify(memberLoads:
        [
            new FemMemberLoad
            {
                Id = 6, MemberId = 10, DistributionType = "trapezoidal", CoordinateSystem = "global",
                StartOffsetM = 1, EndOffsetM = 3, QzStart = -1000, QzEnd = -5000
            }
        ]);

        Assert.Equal(2, result.RetainedDistributed.Count);
        var first = result.RetainedDistributed.Single(l => l.ChildElementTag == "202");
        Assert.Equal((0.0, 1.0), (first.AOverL, first.BOverL));
        Assert.Equal(-2000, first.QAtA.Z, 8);
        Assert.Equal(-4000, first.QAtB.Z, 8);
        var second = result.RetainedDistributed.Single(l => l.ChildElementTag == "203");
        Assert.Equal((0.0, 0.5), (second.AOverL, second.BOverL));
        Assert.Equal(-5000, second.QAtB.Z, 8);
    }

    [Fact]
    public void ReversedChain_DoesNotMirrorStations_ChildElementKeepsParentNodeOrder()
    {
        var load = new FemMemberLoad
        {
            Id = 6, MemberId = 10, DistributionType = "trapezoidal", CoordinateSystem = "global",
            StartOffsetM = 1, EndOffsetM = 3, QzStart = -1000, QzEnd = -5000
        };
        var forward = Classify(memberLoads: [load]);
        var reversed = Classify(memberLoads: [load], chainSpec: MiddleChainReversed);

        Assert.Equal(
            forward.RetainedDistributed.OrderBy(l => l.ChildElementTag).Select(l => (l.ChildElementTag, l.AOverL, l.BOverL, l.QAtA.Z, l.QAtB.Z)),
            reversed.RetainedDistributed.OrderBy(l => l.ChildElementTag).Select(l => (l.ChildElementTag, l.AOverL, l.BOverL, l.QAtA.Z, l.QAtB.Z)));
    }

    [Fact]
    public void PointLoads_AreRoutedByPosition()
    {
        var result = Classify(memberLoads:
        [
            new FemMemberLoad { Id = 1, MemberId = 10, DistributionType = "point", CoordinateSystem = "global", StartOffsetM = 4, QzStart = -100 },
            new FemMemberLoad { Id = 2, MemberId = 10, DistributionType = "point", CoordinateSystem = "global", StartOffsetM = 5, QzStart = -200 },
            new FemMemberLoad { Id = 3, MemberId = 10, DistributionType = "point", CoordinateSystem = "global", StartOffsetM = 1, QzStart = -300 },
            new FemMemberLoad { Id = 4, MemberId = 10, DistributionType = "point", CoordinateSystem = "global", StartOffsetM = 2, QzStart = -400, My = 7 },
        ]);

        var nodal = Assert.Single(result.RetainedNodal);
        Assert.Equal(("103", -100.0, "member_point_load", 1), (nodal.ChildNodeTag, nodal.Load.Z, nodal.Source.Kind, nodal.Source.SourceId));
        var point = Assert.Single(result.RetainedPoints);
        Assert.Equal(("203", 0.5, -200.0), (point.ChildElementTag, point.XOverL, point.Force.Z));
        var boundary = Assert.Single(result.BoundaryNodal);
        Assert.Equal(("102", -400.0, 7.0), (boundary.ParentNodeTag, boundary.Load.Z, boundary.Load.Ry));
        Assert.Equal(new LoadAccounting(2, 1, 1), result.Accounting);
    }

    [Fact]
    public void NodeLoads_AreRoutedThroughSourceNodeTag()
    {
        var result = Classify(nodeLoads:
        [
            new FemNodeLoad { NodeId = 3, Fz = -1, My = 2 },
            new FemNodeLoad { NodeId = 4, Fx = 5 },
            new FemNodeLoad { NodeId = 1, Fz = -9 },
        ]);

        var boundary = Assert.Single(result.BoundaryNodal);
        Assert.Equal(("102", "node_load", 3), (boundary.ParentNodeTag, boundary.Source.Kind, boundary.Source.SourceId));
        Assert.Equal(new Dof6(0, 0, -1, 0, 2, 0), boundary.Load);
        var retained = Assert.Single(result.RetainedNodal);
        Assert.Equal(("103", 5.0), (retained.ChildNodeTag, retained.Load.X));
        Assert.Equal(new LoadAccounting(1, 1, 1), result.Accounting);
    }

    [Fact]
    public void KinematicLoads_EndIsInterface_InteriorIsRetained()
    {
        var result = Classify(kinematic:
        [
            new FemKinematicLoad { NodeId = 3, Dof = 3, Value = -0.002 },
            new FemKinematicLoad { NodeId = 4, Dof = 5, Value = 0.01 },
            new FemKinematicLoad { NodeId = 1, Dof = 1, Value = 0.5 },
        ]);

        var iface = Assert.Single(result.InterfaceKinematic);
        Assert.Equal(("102", 2, -0.002), (iface.ParentNodeTag, iface.Dof, iface.Value));
        var retained = Assert.Single(result.RetainedKinematic);
        Assert.Equal(("103", 4, 0.01), (retained.ChildNodeTag, retained.Dof, retained.Value));
        Assert.Equal(new LoadAccounting(1, 1, 1), result.Accounting);
    }

    [Fact]
    public void ImportedParent_WithoutLoads_IsUnknown()
    {
        var result = Classify(sourceType: "lira");

        Assert.Equal(LoadCompleteness.Unknown, result.Completeness);
        Assert.Contains(result.Diagnostics, d => d.Code == BoundaryScenarioDiagnostics.LoadsUnknown && !d.IsError);
    }

    [Fact]
    public void UnresolvedLoadOnChainMember_Blocks_ButOtherMemberOnlyInforms()
    {
        var parent = Beam();
        parent.Members.Add(new FemMember { Id = 11, ElemTag = "11", NodeIdsJson = "[1,2]", ElemType = "beam" });
        var chain = SubmodelChainTopology.Build(Extraction(parent, MiddleChain), parent.MeshElements, [])!;
        var result = SubmodelLoadClassifier.Classify(chain, "internal", parent.Nodes, parent.Members, parent.MeshNodes,
            parent.MeshElements, new FemResolvedLoads([],
            [
                new FemMemberLoad { Id = 1, MemberId = 10, DistributionType = "point", StartOffsetM = 5, Mz = 3 },
                new FemMemberLoad { Id = 2, MemberId = 11, DistributionType = "uniform", QzStart = -1 },
            ], []));

        var error = Assert.Single(result.Diagnostics, d => d.IsError);
        Assert.Equal(BoundaryScenarioDiagnostics.LoadUnresolved, error.Code);
        Assert.Contains(result.Diagnostics, d => !d.IsError && d.Message.Contains("вне цепочки"));
    }
}
