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

    [Fact]
    public void DeleteNodeCommand_CascadesToMemberLoads_AndUndoRestoresAll()
    {
        var session = NewSession();
        var n1 = new FemNode { Id = 101, NodeTag = "1" };
        var n2 = new FemNode { Id = 102, NodeTag = "2" };
        var n3 = new FemNode { Id = 103, NodeTag = "3" };
        session.Nodes.AddRange([n1, n2, n3]);
        session.Members.AddRange([
            new FemMember { Id = 201, ElemTag = "1", NodeIdsJson = "[1,2]" },
            new FemMember { Id = 202, ElemTag = "2", NodeIdsJson = "[2,3]" }
        ]);
        session.MemberGroups.Add(new FemMemberGroup { Id = 1, Tag = "M1", MemberTagsJson = "[1,2]" });
        session.NodeLoads.Add(new FemNodeLoad { Id = 301, NodeId = 102, Fz = 5 });
        session.KinematicLoads.Add(new FemKinematicLoad { Id = 302, NodeId = 102, Dof = 3, Value = 0.01 });
        session.MemberLoads.AddRange([
            new FemMemberLoad { Id = 401, MemberId = 201, QzStart = 1 },
            new FemMemberLoad { Id = 402, MemberId = 202, QzEnd = 2 }
        ]);

        session.Execute(new DeleteNodeCommand(n2));

        Assert.DoesNotContain(n2, session.Nodes);
        Assert.Empty(session.Members);
        Assert.Empty(session.MemberLoads);
        Assert.Empty(session.NodeLoads);
        Assert.Empty(session.KinematicLoads);
        Assert.Equal("[]", session.MemberGroups[0].MemberTagsJson);

        session.Undo();

        Assert.Equal(3, session.Nodes.Count);
        Assert.Equal(2, session.Members.Count);
        Assert.Equal(2, session.MemberLoads.Count);
        Assert.Single(session.NodeLoads);
        Assert.Single(session.KinematicLoads);
        Assert.Equal("[1,2]", session.MemberGroups[0].MemberTagsJson);
    }

    [Fact]
    public void DeleteNodesCommand_RemovesSharedMemberOnce_AndUndoRedoRestoresOnce()
    {
        var session = NewSession();
        var n1 = new FemNode { Id = 101, NodeTag = "1" };
        var n2 = new FemNode { Id = 102, NodeTag = "2" };
        session.Nodes.AddRange([n1, n2]);
        var member = new FemMember { Id = 201, ElemTag = "1", NodeIdsJson = "[1,2]" };
        session.Members.Add(member);
        session.MemberLoads.Add(new FemMemberLoad { Id = 301, MemberId = member.Id, QzStart = 3 });
        session.MarkSaved();

        session.Execute(new DeleteNodesCommand([n1, n2]));
        Assert.Empty(session.Nodes);
        Assert.Empty(session.Members);
        Assert.Empty(session.MemberLoads);

        session.Undo();
        Assert.Equal(2, session.Nodes.Count);
        Assert.Single(session.Members);
        Assert.Same(member, session.Members[0]);
        Assert.Single(session.MemberLoads);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        session.Redo();
        Assert.Empty(session.Members);
        Assert.Empty(session.MemberLoads);
    }

    [Fact]
    public void DeleteNodesCommand_Preview_DoesNotMutateSession()
    {
        var session = NewSession();
        var n1 = new FemNode { Id = 101, NodeTag = "1" };
        var n2 = new FemNode { Id = 102, NodeTag = "2" };
        session.Nodes.AddRange([n1, n2]);
        session.Members.Add(new FemMember { Id = 201, ElemTag = "1", NodeIdsJson = "[1,2]" });

        var impact = DeleteNodesCommand.Preview(session, [n1]);

        Assert.Equal(1, impact.NodeCount);
        Assert.Equal(1, impact.MemberCount);
        Assert.Equal(2, session.Nodes.Count);
        Assert.Single(session.Members);
    }

    [Fact]
    public void DeleteNodeCommand_RemovesUnsavedAdjacentMember()
    {
        var session = NewSession();
        var node = new FemNode { Id = 101, NodeTag = "1" };
        session.Nodes.Add(node);
        session.Members.Add(new FemMember { Id = 0, ElemTag = "1", NodeIdsJson = "[1,2]" });

        session.Execute(new DeleteNodeCommand(node));

        Assert.Empty(session.Members);
    }
}
