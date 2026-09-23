using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

/// <summary>
/// Эталон рамы по записанной фикстуре реального линейного прогона OpenSees
/// (перезапись — opt-in тест StraightBeamBoundaryScenarioIntegrationTests с OPENCS_WRITE_SUBMODEL_FIXTURE=1).
/// Работает без OpenSees и закрепляет конвенцию localForce на раме с жёсткими узлами.
/// </summary>
public sealed class StraightBeamBoundaryScenarioPortalFrameTests
{
    static IParentLinearResult RecordedResult() =>
        PortalFrameReference.ToParentResult(PortalFrameReference.Deserialize(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, PortalFrameReference.FixtureRelativePath))));

    [Fact]
    public void WholeBeam_ControlEquilibriumAndHoggingAtRigidJoints()
    {
        var parent = PortalFrameReference.Model();
        var scenario = PortalFrameReference.Build(parent, PortalFrameReference.Extract(parent, ["12", "13", "14"]), RecordedResult());

        PortalFrameReference.Verify(parent, scenario);
        PortalFrameReference.VerifyHoggingAtRigidJoints(scenario);
        Assert.Equal(3, scenario.RetainedDistributedLoads.Count);
    }

    [Fact]
    public void MiddleElement_ControlAndEquilibrium()
    {
        var parent = PortalFrameReference.Model();
        var scenario = PortalFrameReference.Build(parent, PortalFrameReference.Extract(parent, ["13"]), RecordedResult());

        PortalFrameReference.Verify(parent, scenario);
        Assert.Equal(new[] { "3", "4" }, scenario.Ends.Select(e => e.ParentNodeTag).OrderBy(t => t));
    }

    [Fact]
    public void WholeBeam_HorizontalLoadAtJoint_GoesToBoundaryNotRetained()
    {
        var parent = PortalFrameReference.Model();
        var scenario = PortalFrameReference.Build(parent, PortalFrameReference.Extract(parent, ["12", "13", "14"]), RecordedResult());

        var left = scenario.Ends.Single(e => e.ParentNodeTag == "2");
        Assert.Contains(left.Contributions, c => c.SourceType == "node_load");
        Assert.Empty(scenario.RetainedNodalLoads);
    }
}
