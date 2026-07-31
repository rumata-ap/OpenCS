using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellOpenSeesModelTests
{
    private const string Fingerprint = "section-fingerprint";

    private static ShellOpenSeesModel BaseModel() => new()
    {
        Nodes =
        [
            new(1, 0, 0, 0, [true, true, true, true, true, true], null),
            new(2, 1, 0, 0, [true, true, true, true, true, true], null),
            new(3, 1, 1, 0, new bool[6], null),
            new(4, 0, 1, 0, new bool[6], null)
        ],
        Materials = [new(1, "concrete", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))],
        Sections = [new(20, "plate", 0.2, ShellFrame.Identity,
            [
                new(0, ShellLayerKind.Concrete, -0.05, 0.1, 1, 0, "layer:0"),
                new(1, ShellLayerKind.Concrete, 0, 0.1, 1, 0, "layer:1"),
                new(2, ShellLayerKind.Concrete, 0.05, 0.1, 1, 0, "layer:2")
            ],
            ShellMappingMode.Exact, [], Fingerprint)],
        Elements = [new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20, Fingerprint,
            ShellFrame.Identity, ShellIntegrationPolicy.Full, null)],
        Stages = [new() { Tag = "stage-1", Loads = [new(3, 0, 0, -1000, 0, 0, 0)] }]
    };

    [Fact]
    public void Validate_AcceptsMinimalModel() => BaseModel().Validate();

    [Fact]
    public void Validate_RejectsEmptyStages()
    {
        var model = BaseModel() with { Stages = [] };
        var ex = Assert.Throws<InvalidOperationException>(model.Validate);
        Assert.Contains("стади", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsStageLoadOnUnknownNode()
    {
        var model = BaseModel() with { Stages = [new() { Tag = "s", Loads = [new(999, 0, 0, -1, 0, 0, 0)] }] };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_RejectsNonPositiveLoadFactorStep()
    {
        var model = BaseModel() with { Stages = [new() { Tag = "s", LoadFactorStep = 0 }] };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_RejectsNonlinearBeamSectionTagCollidingWithShellSectionTag()
    {
        var model = BaseModel() with
        {
            NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel>
            {
                [20] = new() { GJ = 1, Materials = [new() { Tag = 1, Native = new Steel01Spec(4e8, 2e11, 0.01) }],
                    Fibers = [new(0, 0, 0.001, 1)] }
            },
            NonlinearBeamElements = [new(100, 3, 4, 20, 3, (0, 1, 0))]
        };
        var ex = Assert.Throws<InvalidOperationException>(model.Validate);
        Assert.Contains("секци", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsNonlinearBeamMaterialTagCollidingWithShellMaterialTag()
    {
        var model = BaseModel() with
        {
            NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel>
            {
                [30] = new() { GJ = 1, Materials = [new() { Tag = 1, Native = new Steel01Spec(4e8, 2e11, 0.01) }],
                    Fibers = [new(0, 0, 0.001, 1)] }
            },
            NonlinearBeamElements = [new(100, 3, 4, 30, 3, (0, 1, 0))]
        };
        var ex = Assert.Throws<InvalidOperationException>(model.Validate);
        Assert.Contains("материал", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AcceptsMixedShellAndNonlinearBeam()
    {
        var model = BaseModel() with
        {
            NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel>
            {
                [30] = new() { GJ = 1, Materials = [new() { Tag = 40, Native = new Steel01Spec(4e8, 2e11, 0.01) }],
                    Fibers = [new(0, 0, 0.001, 40)] }
            },
            NonlinearBeamElements = [new(100, 3, 4, 30, 3, (0, 1, 0))]
        };
        model.Validate();
    }

    [Fact]
    public void Validate_AcceptsShellCorotationalAndEnhancedAssumedStrainDefaults() => BaseModel().Validate();

    [Fact]
    public void Validate_AcceptsNonlinearBeamGeomTransfCorotational()
    {
        var model = BaseModel() with
        {
            NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel>
            {
                [30] = new() { GJ = 1, Materials = [new() { Tag = 40, Native = new Steel01Spec(4e8, 2e11, 0.01) }],
                    Fibers = [new(0, 0, 0.001, 40)] }
            },
            NonlinearBeamElements = [new(100, 3, 4, 30, 3, (0, 1, 0))],
            NonlinearBeamGeomTransfKind = "Corotational"
        };
        model.Validate();
    }

    [Fact]
    public void Validate_RejectsDrillingStabilizationWithT3Element()
    {
        var model = BaseModel() with
        {
            Elements =
            [
                .. BaseModel().Elements,
                new(11, ShellElementKind.ASDShellT3, [1, 2, 3], 20, Fingerprint,
                    ShellFrame.Identity, ShellIntegrationPolicy.Full, null)
            ],
            Drilling = new DrillingPolicy { Mode = ShellDrillingMode.Stabilization, StabilizationValue = 0.001 }
        };
        var ex = Assert.Throws<InvalidOperationException>(model.Validate);
        Assert.Contains("Stabilization", ex.Message);
    }

    [Fact]
    public void Validate_AcceptsDrillingNonlinearDrillingWithMixedQ4T3()
    {
        var model = BaseModel() with
        {
            Elements =
            [
                .. BaseModel().Elements,
                new(11, ShellElementKind.ASDShellT3, [1, 2, 3], 20, Fingerprint,
                    ShellFrame.Identity, ShellIntegrationPolicy.Full, null)
            ],
            Drilling = new DrillingPolicy { Mode = ShellDrillingMode.NonlinearDrilling }
        };
        model.Validate();
    }

    [Fact]
    public void Validate_RejectsNonPositiveMaterialStateFilterIndex()
    {
        var model = BaseModel() with
        {
            MaterialStateRecording = new ShellStateRecordingPolicy
            {
                ShellIntegrationPoints = [0]
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(model.Validate);
        Assert.Contains("material-state", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AcceptsSortedDistinctMaterialStateFilters()
    {
        var model = BaseModel() with
        {
            MaterialStateRecording = new ShellStateRecordingPolicy
            {
                ShellIntegrationPoints = [2, 1, 2],
                BeamIntegrationPoints = [3, 1, 3],
                BeamFiberIndices = [2, 0, 2]
            }
        };

        model.Validate();
    }
}
