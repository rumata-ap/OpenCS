using System.Text.Json;
using System.Text.Json.Serialization;
using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class SubmodelMaterializationPlannerTests
{
    // ---------- рама «колонна–ригель–колонна» по записанной фикстуре ----------

    static (SubmodelMaterializationBuild Build, BoundaryScenario Scenario, SubmodelExtraction Extraction)
        Portal(string[] selection, IReadOnlyList<DofOverride>? overrides = null)
    {
        var parent = PortalFrameReference.Model();
        var (extraction, meshNodes, meshElements) = PortalFrameReference.ExtractWithMesh(parent, selection);
        var scenario = PortalFrameReference.Build(parent, extraction, PortalFrameReference.FixtureResult(), overrides);
        var build = SubmodelMaterializationPlanner.Plan(new(extraction, scenario, meshNodes, meshElements, parent.Nodes));
        return (build, scenario, extraction);
    }

    static SubmodelMaterializationPlan Success(SubmodelMaterializationBuild build)
    {
        Assert.True(build.IsSuccess, string.Join(" | ", build.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        return build.Plan!;
    }

    [Fact]
    public void Portal_MiddleElement_BuildsLayerAndClampsStartByParentDisplacement()
    {
        var (build, scenario, _) = Portal(["13"]);
        var plan = Success(build);

        Assert.Equal(["3", "4"], plan.Nodes.Select(n => n.NodeTag));
        Assert.Equal([1, 2], plan.Nodes.Select(n => n.Id));
        Assert.All(plan.Nodes, n => Assert.Equal(0, n.DofMask));
        var member = Assert.Single(plan.Members);
        Assert.Equal("13", member.ElemTag);
        Assert.Equal("[3,4]", member.NodeIdsJson);
        Assert.Equal(PortalFrameReference.SectionId, member.CrossSectionId);
        Assert.Equal("manual", member.GjStrategy);

        var load = Assert.Single(plan.MemberLoads);
        Assert.Equal(member.Id, load.MemberId);
        Assert.Equal("global", load.CoordinateSystem);
        Assert.Equal("uniform", load.DistributionType);
        Assert.Equal(0, load.StartOffsetM, 12);
        Assert.Equal(0, load.EndOffsetM, 12);
        Assert.Equal(PortalFrameReference.Q, load.QzStart, 9);
        Assert.Equal(PortalFrameReference.Q, load.QzEnd, 9);

        var start = scenario.Ends.Single(e => e.AtStart);
        Assert.Equal("3", start.ChildNodeTag);
        Assert.Equal(0, plan.Summary.RankBefore);
        Assert.Equal(6, plan.Summary.GaugeDofs.Count);
        int startId = plan.Nodes.Single(n => n.NodeTag == "3").Id;
        foreach (var gauge in plan.Summary.GaugeDofs)
        {
            Assert.True(gauge.AtStart);
            Assert.Equal(start.Displacement![gauge.Dof], gauge.Value, 15);
            Assert.Contains(plan.KinematicLoads, k => k.NodeId == startId && k.Dof == gauge.Dof + 1 && k.Value == gauge.Value);
        }
        // Сила gauge-DOF остаётся приложенной.
        var startLoad = Assert.Single(plan.NodeLoads, l => l.NodeId == startId);
        Assert.Equal(start.Dofs[0].Value!.Value, startLoad.Fx, 9);
        Assert.Equal(start.Dofs[4].Value!.Value, startLoad.My, 9);
        Assert.Single(plan.NodeLoads, l => l.NodeId == plan.Nodes.Single(n => n.NodeTag == "4").Id);
        Assert.Equal(6, plan.KinematicLoads.Count);
    }

    [Fact]
    public void Portal_MeshCopies_PointToOwnStructuralLayer()
    {
        var plan = Success(Portal(["12", "13", "14"]).Build);

        Assert.All(plan.MeshNodes, n => Assert.Equal(n.NodeTag, n.SourceNodeTag));
        Assert.All(plan.MeshElements, e => Assert.Equal(e.ElemTag, e.SourceMemberTag));
        var memberTags = plan.Members.Select(m => m.ElemTag).ToHashSet();
        Assert.All(plan.MeshNodes, n => Assert.Contains(n.SourceMemberTag!, memberTags));
    }

    [Fact]
    public void Portal_WholeBeam_ThreeMembersGaugeAtStartJoint()
    {
        var (build, _, _) = Portal(["12", "13", "14"]);
        var plan = Success(build);

        Assert.Equal(["12", "13", "14"], plan.Members.Select(m => m.ElemTag));
        Assert.Equal(3, plan.MemberLoads.Count);
        Assert.All(plan.Summary.GaugeDofs, g => Assert.Equal("2", g.ChildNodeTag));
        Assert.Equal(6, plan.Summary.GaugeDofs.Count);
    }

    [Fact]
    public void Portal_StartKinematicOverride_NeedsNoGauge()
    {
        var overrides = Enumerable.Range(0, 6).Select(d => new DofOverride(true, d, DofMode.Kinematic)).ToList();
        var (build, scenario, _) = Portal(["13"], overrides);
        var plan = Success(build);

        Assert.Equal(6, plan.Summary.RankBefore);
        Assert.Empty(plan.Summary.GaugeDofs);
        int startId = plan.Nodes.Single(n => n.NodeTag == "3").Id;
        Assert.Equal(6, plan.KinematicLoads.Count(k => k.NodeId == startId));
        Assert.DoesNotContain(plan.NodeLoads, l => l.NodeId == startId);
        var start = scenario.Ends.Single(e => e.AtStart);
        Assert.All(plan.KinematicLoads, k => Assert.Equal(start.Displacement![k.Dof - 1], k.Value, 15));
    }

    [Fact]
    public void Portal_LoadReferencesUseTemporaryIdsNotTags()
    {
        var plan = Success(Portal(["13"]).Build);

        var nodeIds = plan.Nodes.Select(n => n.Id).ToHashSet();
        Assert.All(plan.NodeLoads, l => Assert.Contains(l.NodeId, nodeIds));
        Assert.All(plan.KinematicLoads, l => Assert.Contains(l.NodeId, nodeIds));
        Assert.DoesNotContain(plan.NodeLoads, l => l.NodeId is 3 or 4);
        Assert.All(plan.MemberLoads, l => Assert.Equal(plan.LoadCase.Id, l.LoadCaseId));
        Assert.Equal(new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [plan.LoadCase.Id] }.ToJson(),
            plan.LinearAnalysis.LoadExpressionJson);
        Assert.Equal("linear", plan.LinearAnalysis.Kind);
    }

    [Fact]
    public void Portal_BrokenMesh_IsMeshStaleWithoutException()
    {
        var parent = PortalFrameReference.Model();
        var (extraction, meshNodes, meshElements) = PortalFrameReference.ExtractWithMesh(parent, ["12", "13", "14"]);
        var scenario = PortalFrameReference.Build(parent, extraction, PortalFrameReference.FixtureResult());

        var build = SubmodelMaterializationPlanner.Plan(new(extraction, scenario, meshNodes,
            meshElements.Where(e => e.ElemTag != "13").ToList(), parent.Nodes));

        Assert.Null(build.Plan);
        Assert.Contains(build.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.MeshStale);
    }

    [Fact]
    public void Portal_ForeignScenario_IsRejected()
    {
        var parent = PortalFrameReference.Model();
        var (extraction, meshNodes, meshElements) = PortalFrameReference.ExtractWithMesh(parent, ["13"]);
        var scenario = PortalFrameReference.Build(parent, extraction, PortalFrameReference.FixtureResult()) with { ExtractionId = 999 };

        var build = SubmodelMaterializationPlanner.Plan(new(extraction, scenario, meshNodes, meshElements, parent.Nodes));

        Assert.Null(build.Plan);
        Assert.Contains(build.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.ScenarioBlocked && d.IsError);
    }

    // ---------- синтетика на балке 101..105 (цепочка 202→203, концы 102 и 104) ----------

    static readonly Dof6 Displacement = new(1e-3, 2e-3, -3e-3, 1e-4, -2e-4, 3e-4);

    static (SubmodelExtraction Extraction, List<FemMeshNode> Nodes, List<FemElement> Elements, ParentModel Parent) Synthetic()
    {
        var parent = BoundaryTestModels.Beam();
        var extraction = BoundaryTestModels.Extraction(parent, BoundaryTestModels.MiddleChain);
        var nodes = parent.MeshNodes.Where(n => n.NodeTag is "102" or "103" or "104")
            .Select(n => new FemMeshNode { NodeTag = n.NodeTag, X = n.X, Y = n.Y, Z = n.Z, SourceNodeTag = n.SourceNodeTag }).ToList();
        var elements = parent.MeshElements.Where(e => e.ElemTag is "202" or "203")
            .Select(e => new FemElement { ElemTag = e.ElemTag, NodeIdsJson = e.NodeIdsJson, CrossSectionId = 5, GjManualValue = 1e9 }).ToList();
        return (extraction, nodes, elements, parent);
    }

    static ScenarioEnd End(bool atStart, string tag, DofMode mode, Dof6? displacement = null, Dof6? force = null) =>
        new(atStart, tag, tag,
            Enumerable.Range(0, 6).Select(d => new DofAssignment(mode,
                mode switch { DofMode.Force => (force ?? new Dof6(10, 20, 30, 40, 50, 60))[d], DofMode.Kinematic => displacement![d], _ => null },
                DofSource.Auto)).ToList(),
            mode == DofMode.Force ? force ?? new Dof6(10, 20, 30, 40, 50, 60) : null, [], null, displacement,
            new ControlCheck(null, 0, 0, false, false));

    static BoundaryScenario Scenario(ScenarioEnd start, ScenarioEnd end,
        IReadOnlyList<RetainedDistributedLoad>? distributed = null, IReadOnlyList<RetainedKinematicLoad>? kinematic = null,
        ScenarioStatus status = ScenarioStatus.Complete) =>
        new(77, 1.0, status, status == ScenarioStatus.Incomplete ? LoadCompleteness.Unknown : LoadCompleteness.Known,
            distributed ?? [], [], [], kinematic ?? [], new LoadAccounting(0, 0, 0), [start, end], []);

    static SubmodelMaterializationBuild Plan(BoundaryScenario scenario,
        Action<List<FemNode>, List<FemElement>>? mutate = null, SubmodelExtraction? extraction = null)
    {
        var (defaultExtraction, nodes, elements, parent) = Synthetic();
        mutate?.Invoke(parent.Nodes, elements);
        return SubmodelMaterializationPlanner.Plan(new(extraction ?? defaultExtraction, scenario, nodes, elements, parent.Nodes));
    }

    [Fact]
    public void ParentSupportAtEnd_BecomesDofMaskWithoutLoadsOrGauge()
    {
        var plan = Success(Plan(Scenario(End(true, "102", DofMode.Fixed), End(false, "104", DofMode.Force, Displacement))));

        Assert.Equal(63, plan.Nodes.Single(n => n.NodeTag == "102").DofMask);
        int startId = plan.Nodes.Single(n => n.NodeTag == "102").Id;
        Assert.DoesNotContain(plan.NodeLoads, l => l.NodeId == startId);
        Assert.DoesNotContain(plan.KinematicLoads, l => l.NodeId == startId);
        Assert.Equal(6, plan.Summary.RankBefore);
        Assert.Empty(plan.Summary.GaugeDofs);
    }

    [Fact]
    public void InteriorParentSupport_IsTransferredAndCountsInRank()
    {
        // Внутренний узел 103 ссылается на конструктивный узел "4" родителя — закрепим его полностью.
        var build = Plan(Scenario(End(true, "102", DofMode.Force, Displacement), End(false, "104", DofMode.Force, Displacement)),
            (parentNodes, _) => parentNodes.Single(n => n.NodeTag == "4").DofMask = 63);
        var plan = Success(build);

        Assert.Equal(63, plan.Nodes.Single(n => n.NodeTag == "103").DofMask);
        Assert.Equal(6, plan.Summary.RankBefore);
        Assert.Empty(plan.Summary.GaugeDofs);
    }

    [Fact]
    public void PartialAndTrapezoidalLoads_ConvertFractionsToOffsets()
    {
        var q1 = new PlanarVector3(0, 0, -1000);
        var q2 = new PlanarVector3(0, 0, -3000);
        var plan = Success(Plan(Scenario(End(true, "102", DofMode.Force, Displacement), End(false, "104", DofMode.Force, Displacement),
            [new RetainedDistributedLoad("202", 0.25, 0.75, q1, q1, 7, "10"),
             new RetainedDistributedLoad("203", 0, 1, q1, q2, 7, "10")])));

        var uniform = plan.MemberLoads.Single(l => l.MemberId == plan.Members.Single(m => m.ElemTag == "202").Id);
        Assert.Equal("uniform", uniform.DistributionType);
        Assert.Equal(0.5, uniform.StartOffsetM, 12);
        Assert.Equal(0.5, uniform.EndOffsetM, 12);
        var trapezoid = plan.MemberLoads.Single(l => l.MemberId == plan.Members.Single(m => m.ElemTag == "203").Id);
        Assert.Equal("trapezoidal", trapezoid.DistributionType);
        Assert.Equal(-1000, trapezoid.QzStart);
        Assert.Equal(-3000, trapezoid.QzEnd);
    }

    [Fact]
    public void BlockedScenario_IsNotMaterialized()
    {
        var build = Plan(Scenario(End(true, "102", DofMode.Force, Displacement), End(false, "104", DofMode.Force, Displacement),
            status: ScenarioStatus.Blocked));

        Assert.Null(build.Plan);
        Assert.Contains(build.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.ScenarioBlocked);
    }

    [Fact]
    public void IncompleteScenario_IsMaterializedWithWarningInSummary()
    {
        var plan = Success(Plan(Scenario(End(true, "102", DofMode.Force, Displacement), End(false, "104", DofMode.Force, Displacement),
            status: ScenarioStatus.Incomplete)));

        Assert.Contains(plan.Summary.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.LoadsUnknown && !d.IsError);
    }

    [Fact]
    public void BuildAndSummaryDiagnostics_AreTheSameList()
    {
        var build = Plan(Scenario(End(true, "102", DofMode.Force, Displacement), End(false, "104", DofMode.Force, Displacement),
            status: ScenarioStatus.Incomplete));

        Assert.Equal(build.Diagnostics, build.Plan!.Summary.Diagnostics);
    }

    [Fact]
    public void MissingSection_OrUnknownBeta_IsMemberIncomplete()
    {
        var scenario = Scenario(End(true, "102", DofMode.Force, Displacement), End(false, "104", DofMode.Force, Displacement));
        var noSection = Plan(scenario, (_, elements) => elements.Single(e => e.ElemTag == "203").CrossSectionId = null);
        Assert.Null(noSection.Plan);
        Assert.Contains(noSection.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.MemberIncomplete && d.SourceKeys!.Contains("203"));

        var (extraction, _, _, _) = Synthetic();
        var absentBeta = new SubmodelExtraction
        {
            Id = extraction.Id, Nodes = extraction.Nodes, Tolerances = extraction.Tolerances, Metrics = extraction.Metrics,
            Segments = extraction.Segments.Select(s => s with { BetaSource = BetaSource.Absent }).ToList()
        };
        var noBeta = Plan(scenario, extraction: absentBeta);
        Assert.Null(noBeta.Plan);
        Assert.Contains(noBeta.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.MemberIncomplete);
    }

    [Fact]
    public void GaugeWithoutParentDisplacement_IsBlocked()
    {
        var build = Plan(Scenario(End(true, "102", DofMode.Force), End(false, "104", DofMode.Force)));

        Assert.Null(build.Plan);
        Assert.Contains(build.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.GaugeUnavailable && d.IsError);
    }

    [Fact]
    public void ScenarioReferencingMissingElement_IsRejectedWithoutException()
    {
        var q = new PlanarVector3(0, 0, -1);
        var build = Plan(Scenario(End(true, "102", DofMode.Force, Displacement), End(false, "104", DofMode.Force, Displacement),
            [new RetainedDistributedLoad("999", 0, 1, q, q, 1, "10")]));

        Assert.Null(build.Plan);
        Assert.Contains(build.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.ScenarioBlocked);
    }

    [Fact]
    public void RetainedKinematicLoad_IsOneBasedDof()
    {
        var plan = Success(Plan(Scenario(End(true, "102", DofMode.Force, Displacement), End(false, "104", DofMode.Force, Displacement),
            kinematic: [new RetainedKinematicLoad("103", 2, -0.004, 4)])));

        int id = plan.Nodes.Single(n => n.NodeTag == "103").Id;
        var load = Assert.Single(plan.KinematicLoads, k => k.NodeId == id);
        Assert.Equal(3, load.Dof);
        Assert.Equal(-0.004, load.Value);
    }

    // ---------- доменная сериализация сводки (опции, эквивалентные DatabaseService._jsonSettings) ----------

    [Fact]
    public void Summary_RoundTripsThroughJson()
    {
        var plan = Success(Portal(["13"]).Build);
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        string json = JsonSerializer.Serialize(plan.Summary, options);
        var restored = JsonSerializer.Deserialize<SubmodelMaterializationSummary>(json, options)!;

        Assert.Equal(plan.Summary.ExtractionId, restored.ExtractionId);
        Assert.Equal(plan.Summary.RankBefore, restored.RankBefore);
        Assert.Equal(plan.Summary.GaugeDofs, restored.GaugeDofs);
        Assert.Equal(plan.Summary.NodeCount, restored.NodeCount);
        Assert.Equal(plan.Summary.Diagnostics.Select(d => (d.Code, d.Message, d.IsError)),
            restored.Diagnostics.Select(d => (d.Code, d.Message, d.IsError)));
    }
}
