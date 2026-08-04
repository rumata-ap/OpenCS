using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.OpenSees.Tests;

public sealed class FemMemberLoadGlyphFactoryTests
{
    [Fact]
    public void Create_IgnoresUnsavedMembersWithDuplicateZeroIds()
    {
        var nodes = new[]
        {
            new FemNode { NodeTag = "1", X = 0 },
            new FemNode { NodeTag = "2", X = 1 },
            new FemNode { NodeTag = "3", X = 2 },
        };
        var members = new[]
        {
            new FemMember { Id = 0, ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]" },
            new FemMember { Id = 0, ElemTag = "2", ElemType = "beam", NodeIdsJson = "[2,3]" },
        };

        var glyphs = FemMemberLoadGlyphFactory.Create(members, nodes, []);

        Assert.Empty(glyphs);
    }
}
