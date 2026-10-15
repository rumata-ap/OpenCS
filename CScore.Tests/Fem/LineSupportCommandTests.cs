using CScore.Fem;
using CScore.Fem.Editing;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Команды редактора для линейных связей: назначение одной отменой, замена, снятие, каскад с КонЭ.</summary>
public sealed class LineSupportCommandTests
{
    static FemPlateEdge Edge(string member, int vertex) =>
        new(new FemPlateEdgeKey(member, BoundaryLoop.Outer, 0, vertex), new(vertex, 0, 0), new(vertex + 1, 0, 0), new(0, 0, 1));

    static FemSchemaEditSession Session()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 3, Tag = "S" });
        session.Nodes.Add(new FemNode { Id = 1, NodeTag = "1" });
        session.Nodes.Add(new FemNode { Id = 2, NodeTag = "2" });
        session.Members.Add(new FemMember { ElemTag = "P", ElemType = "shell", PlanarRegionId = 1, NodeIdsJson = "[1,2]" });
        session.Members.Add(new FemMember { ElemTag = "Q", ElemType = "shell", PlanarRegionId = 2, NodeIdsJson = "[]" });
        return session;
    }

    [Fact]
    public void Set_ReplacesPerEdge_UndoRestores()
    {
        var session = Session();
        session.Execute(new SetLineSupportsCommand([Edge("P", 0), Edge("P", 1)], new FemLineSupport { Mask = 0b111 }));
        Assert.Equal(2, session.LineSupports.Count);
        Assert.All(session.LineSupports, s => Assert.Equal(3, s.SchemaId));
        Assert.Equal(new[] { 0, 1 }, session.LineSupports.Select(s => s.StartVertex));

        var spring = new FemLineSupport();
        spring.SetStiffnesses([0, 0, 5e7, 0, 0, 0]);
        session.Execute(new SetLineSupportsCommand([Edge("P", 1)], spring));
        Assert.Equal(2, session.LineSupports.Count);
        Assert.Equal(5e7, session.LineSupports.Single(s => s.StartVertex == 1).Kz);
        Assert.Equal(0, session.LineSupports.Single(s => s.StartVertex == 1).Mask);

        session.Undo();
        Assert.Equal(0b111, session.LineSupports.Single(s => s.StartVertex == 1).Mask);
        session.Undo();
        Assert.Empty(session.LineSupports);
        session.Redo();
        Assert.Equal(2, session.LineSupports.Count);
    }

    [Fact]
    public void Set_EmptyTemplate_Removes_AndRemoveCommand()
    {
        var session = Session();
        session.Execute(new SetLineSupportsCommand([Edge("P", 0), Edge("P", 1), Edge("Q", 0)], new FemLineSupport { Mask = 4 }));
        session.Execute(new SetLineSupportsCommand([Edge("P", 0)], new FemLineSupport()));
        Assert.Equal(2, session.LineSupports.Count);

        session.Execute(new RemoveLineSupportsCommand(session.LineSupports.Where(s => s.MemberTag == "Q").ToList()));
        Assert.Equal(["P"], session.LineSupports.Select(s => s.MemberTag));
        session.Undo();
        Assert.Equal(2, session.LineSupports.Count);
    }

    [Fact]
    public void DeleteMember_And_DeleteNodes_CascadeLineSupports()
    {
        var session = Session();
        session.Execute(new SetLineSupportsCommand([Edge("P", 0), Edge("Q", 0)], new FemLineSupport { Mask = 4 }));

        session.Execute(new DeleteMemberCommand(session.Members.Single(m => m.ElemTag == "Q")));
        Assert.Equal(["P"], session.LineSupports.Select(s => s.MemberTag));
        session.Undo();
        Assert.Equal(2, session.LineSupports.Count);

        session.Execute(new DeleteNodesCommand([session.Nodes[0]])); // узел 1 входит в «P»
        Assert.Equal(["Q"], session.LineSupports.Select(s => s.MemberTag));
        session.Undo();
        Assert.Equal(new[] { "P", "Q" }, session.LineSupports.Select(s => s.MemberTag));
    }
}
