using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public class FemLinearModelTests
{
    static FemLinearNode Node(int tag, double x) =>
        new(tag, x, 0, 0, new bool[6]);

    static FemLinearElement Elem(int tag, int i, int j) =>
        new(tag, i, j, A: 0.01, E: 2e11, G: 8e10, J: 1e-5, Iy: 1e-4, Iz: 1e-4, Vecxz: (0, 0, 1));

    [Fact]
    public void Validate_ValidModel_DoesNotThrow()
    {
        var model = new FemLinearModel
        {
            Nodes = [Node(1, 0), Node(2, 1)],
            Elements = [Elem(1, 1, 2)],
            Loads = [new FemLinearNodalLoad(2, 1000, 0, 0, 0, 0, 0)]
        };
        model.Validate();
    }

    [Fact]
    public void Validate_ElementReferencesMissingNode_Throws()
    {
        var model = new FemLinearModel
        {
            Nodes = [Node(1, 0)],
            Elements = [Elem(1, 1, 99)],
            Loads = []
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_LoadOnMissingNode_Throws()
    {
        var model = new FemLinearModel
        {
            Nodes = [Node(1, 0), Node(2, 1)],
            Elements = [Elem(1, 1, 2)],
            Loads = [new FemLinearNodalLoad(77, 1, 0, 0, 0, 0, 0)]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_NonPositiveArea_Throws()
    {
        var model = new FemLinearModel
        {
            Nodes = [Node(1, 0), Node(2, 1)],
            Elements = [new FemLinearElement(1, 1, 2, A: 0, E: 2e11, G: 8e10, J: 1e-5, Iy: 1e-4, Iz: 1e-4, Vecxz: (0, 0, 1))],
            Loads = []
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }
}
