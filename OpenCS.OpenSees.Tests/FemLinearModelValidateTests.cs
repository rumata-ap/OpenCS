using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public class FemLinearModelValidateTests
{
    static FemLinearModel BaseModel() => new()
    {
        Nodes = [new FemLinearNode(1, 0, 0, 0, new bool[6]), new FemLinearNode(2, 3, 0, 0, new bool[6])],
        Elements = [new FemLinearElement(1, 1, 2, 0.02, 3e10, 1e6, 1.0, 5e-4, 2e-4, (0, 0, 1))]
    };

    [Fact]
    public void Validate_RejectsPointLoadOnMissingElement()
    {
        var model = BaseModel();
        var withLoad = new FemLinearModel
        {
            Nodes = model.Nodes, Elements = model.Elements,
            PointLoads = [new FemLinearPointLoad(99, 100, 0, 0, 0.5)]
        };
        Assert.Throws<InvalidOperationException>(() => withLoad.Validate());
    }

    [Fact]
    public void Validate_RejectsPointLoadWithXOverLOutOfRange()
    {
        var model = BaseModel();
        var withLoad = new FemLinearModel
        {
            Nodes = model.Nodes, Elements = model.Elements,
            PointLoads = [new FemLinearPointLoad(1, 100, 0, 0, 1.0)]
        };
        Assert.Throws<InvalidOperationException>(() => withLoad.Validate());
    }

    [Fact]
    public void Validate_AcceptsValidPointLoad()
    {
        var model = BaseModel();
        var withLoad = new FemLinearModel
        {
            Nodes = model.Nodes, Elements = model.Elements,
            PointLoads = [new FemLinearPointLoad(1, 100, 0, 0, 0.5)]
        };
        withLoad.Validate();
    }

    [Fact]
    public void Validate_RejectsOnlyOneShearArea()
    {
        var model = BaseModel();
        var invalid = new FemLinearModel
        {
            Nodes = model.Nodes,
            Elements = [model.Elements.Single() with { Avy = 0.015 }]
        };

        Assert.Throws<InvalidOperationException>(() => invalid.Validate());
    }

    [Theory]
    [InlineData(0.0, 0.012)]
    [InlineData(0.015, 0.0)]
    [InlineData(-0.015, 0.012)]
    [InlineData(0.015, double.NaN)]
    public void Validate_RejectsNonpositiveOrNonfiniteShearAreas(double avy, double avz)
    {
        var model = BaseModel();
        var invalid = new FemLinearModel
        {
            Nodes = model.Nodes,
            Elements = [model.Elements.Single() with { Avy = avy, Avz = avz }]
        };

        Assert.Throws<InvalidOperationException>(() => invalid.Validate());
    }
}
