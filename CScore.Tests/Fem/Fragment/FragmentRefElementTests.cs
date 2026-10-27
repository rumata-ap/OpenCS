using CScore.Fem;
using CScore.Fem.Fragment;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

public sealed class FragmentRefElementTests
{
    [Fact]
    public void From_KeepsRebarTypesThicknessStiffnessAndC1()
    {
        var nodes = new[]
        {
            new FemMeshNode { NodeTag = "1", X = 0, Y = 0 }, new FemMeshNode { NodeTag = "2", X = 1, Y = 0 },
            new FemMeshNode { NodeTag = "3", X = 0, Y = 1 },
        }.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        var e = new FemElement
        {
            ElemTag = "101", ElemType = "shell", NodeIdsJson = "[1,2,3]", StiffnessNum = 4, FoundationC1 = 5000,
            ReinforcementTypeIds = "1 2", ThicknessM = 0.25, SourceMemberTag = "S1",
        };

        var r = Assert.Single(FragmentRefElement.From([e], nodes));

        Assert.Equal(("101", 4, 5000.0, "1 2", 0.25, "S1"),
            (r.Tag, r.StiffnessNum!.Value, r.FoundationC1!.Value, r.ReinforcementTypeIds, r.ThicknessM!.Value, r.SourceMemberTag));
    }
}
