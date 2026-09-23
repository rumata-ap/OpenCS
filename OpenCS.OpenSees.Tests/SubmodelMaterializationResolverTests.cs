using CScore;
using CScore.Submodel;
using CScore.Tests.Submodel;
using OpenCS.OpenSees.CScore;

namespace OpenCS.OpenSees.Tests;

/// <summary>Материализованная субмодель принимается штатным линейным резолвером без OpenSees.</summary>
public sealed class SubmodelMaterializationResolverTests
{
    internal static Dictionary<int, GeoProps> SectionProps() => new()
    {
        [PortalFrameReference.SectionId] = new GeoProps
        {
            A = PortalFrameReference.Area, EA = PortalFrameReference.Area * PortalFrameReference.E,
            Ix = PortalFrameReference.Inertia, EIx = PortalFrameReference.Inertia * PortalFrameReference.E,
            Iy = PortalFrameReference.Inertia, EIy = PortalFrameReference.Inertia * PortalFrameReference.E
        }
    };

    [Fact]
    public void MaterializedMiddleElement_ResolvesWithGaugeAndGlobalMemberLoad()
    {
        var parent = PortalFrameReference.Model();
        var (extraction, meshNodes, meshElements) = PortalFrameReference.ExtractWithMesh(parent, ["13"]);
        var scenario = PortalFrameReference.Build(parent, extraction, PortalFrameReference.FixtureResult());
        var build = SubmodelMaterializationPlanner.Plan(new(extraction, scenario, meshNodes, meshElements, parent.Nodes));
        Assert.True(build.IsSuccess);
        var plan = build.Plan!;

        var resolve = new FemLinearModelResolver().Resolve(plan.MeshNodes, plan.MeshElements, plan.Nodes, plan.Members,
            plan.NodeLoads, SectionProps(), plan.MemberLoads, plan.KinematicLoads);

        Assert.True(resolve.Ok, string.Join(" | ", resolve.Errors));
        var model = resolve.Model!;
        Assert.All(model.Nodes, n => Assert.All(n.Fixed, f => Assert.False(f)));
        Assert.Equal(6, model.KinematicLoads.Count(k => k.NodeTag == 3));
        Assert.Contains(model.Loads, l => l.NodeTag == 3);
        Assert.Contains(model.Loads, l => l.NodeTag == 4);
        var distributed = Assert.Single(model.DistributedLoads);
        Assert.Equal(13, distributed.ElementTag);
        double magnitude = Math.Sqrt(distributed.WxStart * distributed.WxStart + distributed.WyStart * distributed.WyStart
            + distributed.WzStart * distributed.WzStart);
        Assert.Equal(Math.Abs(PortalFrameReference.Q), magnitude, 6);
    }
}
