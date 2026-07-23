using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class FemNonlinearModelTests
{
    static FemLinearNode Node(int tag, double x) => new(tag, x, 0, 0, new bool[6]);

    static OpenSeesSectionModel Section() => new()
    {
        Materials = [new OpenSeesMaterialDefinition
        {
            Tag = 1,
            PositiveEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(0.01, 2_000_000)],
            NegativeEnvelope = [new EnvelopePoint(-0.01, -2_000_000), new EnvelopePoint(0, 0)]
        }],
        Fibers = [new OpenSeesFiber(0, 0, 0.01, 1)],
        GJ = 1e6
    };

    static FemNonlinearModel ValidModel() => new()
    {
        Nodes = [Node(1, 0), Node(2, 1)],
        Sections = new Dictionary<int, OpenSeesSectionModel> { [1] = Section() },
        Elements = [new FemNonlinearElement(1, 1, 2, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1))],
        Loads = [new FemLinearNodalLoad(2, 1000, 0, 0, 0, 0, 0)]
    };

    [Fact]
    public void Validate_ValidModel_DoesNotThrow() => ValidModel().Validate();

    [Fact]
    public void Validate_ElementReferencesMissingSection_Throws()
    {
        var model = ValidModel();
        model = new FemNonlinearModel
        {
            Nodes = model.Nodes, Sections = model.Sections, Loads = model.Loads,
            Elements = [new FemNonlinearElement(1, 1, 2, SectionTag: 99, NumIntegrationPoints: 5, Vecxz: (0, 0, 1))]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_ElementReferencesMissingNode_Throws()
    {
        var model = ValidModel();
        model = new FemNonlinearModel
        {
            Nodes = [Node(1, 0)], Sections = model.Sections, Loads = [],
            Elements = [new FemNonlinearElement(1, 1, 99, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1))]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveLegacyLoadFactorStep_Throws(double step)
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Loads = valid.Loads,
            LoadFactorStep = step
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_NonPositiveLoadFactorStep_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Loads = valid.Loads,
            LoadFactorStep = 0
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_MaxLoadFactorBelowStep_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Loads = valid.Loads,
            LoadFactorStep = 0.2, MaxLoadFactor = 0.1
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_ZeroRefinementDivisions_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Loads = valid.Loads,
            RefinementDivisions = 0
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_UnknownGeomTransfKind_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Loads = valid.Loads,
            GeomTransfKind = "Nope"
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_UnknownConvergenceTest_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Loads = valid.Loads,
            ConvergenceTest = "Nope"
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_DefaultConvergenceTest_IsEnergyIncr() => Assert.Equal("EnergyIncr", ValidModel().ConvergenceTest);

    [Fact]
    public void Validate_ZeroIntegrationPoints_Throws()
    {
        var model = ValidModel();
        model = new FemNonlinearModel
        {
            Nodes = model.Nodes, Sections = model.Sections, Loads = model.Loads,
            Elements = [new FemNonlinearElement(1, 1, 2, SectionTag: 1, NumIntegrationPoints: 0, Vecxz: (0, 0, 1))]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_CorotationalWithDistributedLoad_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Loads = valid.Loads,
            DistributedLoads = [new FemLinearDistributedLoad(1, 0, -1000, 0, 0, -1000, 0, 0, 1)],
            GeomTransfKind = "Corotational"
        };

        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_RejectsPointLoadOnMissingElement()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Loads = valid.Loads,
            PointLoads = [new FemLinearPointLoad(999, 10, 0, 0, 0.5)]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_CorotationalWithPointLoad_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Loads = valid.Loads,
            PointLoads = [new FemLinearPointLoad(1, 10, 0, 0, 0.5)],
            GeomTransfKind = "Corotational"
        };

        var ex = Assert.Throws<InvalidOperationException>(model.Validate);
        Assert.Contains("Corotational", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
