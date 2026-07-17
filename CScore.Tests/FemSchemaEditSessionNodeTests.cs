using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

public sealed class FemSchemaEditSessionNodeTests
{
    static FemSchemaEditSession NewSession(int schemaId = 1) => new(new FemSchema { Id = schemaId });

    [Fact]
    public void AddNodeCommand_AddsNodeAndUndoRemovesIt()
    {
        var session = NewSession();
        var node = new FemNode { NodeTag = "1", X = 1, Y = 2, Z = 3 };

        session.Execute(new AddNodeCommand(node));
        Assert.Single(session.Nodes);
        Assert.True(session.CanUndo);

        session.Undo();
        Assert.Empty(session.Nodes);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        session.Redo();
        Assert.Single(session.Nodes);
    }

    [Fact]
    public void MoveNodeCommand_UpdatesCoordinatesAndUndoRestores()
    {
        var session = NewSession();
        var node = new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0 };
        session.Execute(new AddNodeCommand(node));

        session.Execute(new MoveNodeCommand(node, 5, 6, 7));
        Assert.Equal(5, session.Nodes[0].X);

        session.Undo();
        Assert.Equal(0, session.Nodes[0].X);
    }

    [Fact]
    public void SetDofMaskCommand_UpdatesMaskAndUndoRestores()
    {
        var session = NewSession();
        var node = new FemNode { Id = 1, NodeTag = "1" };
        session.Execute(new AddNodeCommand(node));

        session.Execute(new SetDofMaskCommand(node, 63));
        Assert.Equal(63, session.Nodes[0].DofMask);

        session.Undo();
        Assert.Equal(0, session.Nodes[0].DofMask);
    }

    [Fact]
    public void DeleteNodeCommand_CascadesToMembersGroupsAndLoads_AndUndoRestoresAll()
    {
        var session = NewSession();
        // Id намеренно НЕ совпадает с NodeTag: NodeIdsJson/MemberTagsJson ссылаются по Tag, а не по БД-Id.
        var n1 = new FemNode { Id = 101, NodeTag = "1" };
        var n2 = new FemNode { Id = 102, NodeTag = "2" };
        session.Execute(new AddNodeCommand(n1));
        session.Execute(new AddNodeCommand(n2));
        session.Members.Add(new FemMember { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" });
        session.MemberGroups.Add(new FemMemberGroup { Id = 1, Tag = "M1", MemberTagsJson = "[1]" });
        session.LoadCases.Add(new FemLoadCase { Id = 1, Tag = "G" });
        session.NodeLoads.Add(new FemNodeLoad { Id = 1, LoadCaseId = 1, NodeId = 101, Fz = 5 });

        session.Execute(new DeleteNodeCommand(n1));

        Assert.Single(session.Nodes);
        Assert.Empty(session.Members);
        Assert.Empty(session.NodeLoads);
        Assert.Equal("[]", session.MemberGroups[0].MemberTagsJson);

        session.Undo();
        Assert.Equal(2, session.Nodes.Count);
        Assert.Single(session.Members);
        Assert.Single(session.NodeLoads);
        Assert.Equal("[1]", session.MemberGroups[0].MemberTagsJson);
    }
}
