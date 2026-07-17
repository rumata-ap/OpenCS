using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

public sealed class FemFragmentClipboardTests
{
    [Fact]
    public void Copy_CapturesOnlySelectedNodesMembersAndFullyContainedGroups()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        session.Nodes.Add(new FemNode { Id = 1, NodeTag = "1", X = 0 });
        session.Nodes.Add(new FemNode { Id = 2, NodeTag = "2", X = 1 });
        session.Nodes.Add(new FemNode { Id = 3, NodeTag = "3", X = 2 });
        session.Members.Add(new FemMember { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" });
        session.Members.Add(new FemMember { Id = 2, ElemTag = "2", NodeIdsJson = "[2,3]" });
        session.MemberGroups.Add(new FemMemberGroup { Id = 1, Tag = "M1", MemberTagsJson = "[1]" });

        var snapshot = FemFragmentClipboard.Copy(session,
            nodeTags: new HashSet<string> { "1", "2" },
            memberTags: new HashSet<string> { "1" });

        Assert.Equal(2, snapshot.Nodes.Count);
        Assert.Single(snapshot.Members);
        Assert.Single(snapshot.MemberGroups);
    }

    [Fact]
    public void Copy_PullsInMemberNodesEvenWhenNotExplicitlySelected()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        session.Nodes.Add(new FemNode { Id = 1, NodeTag = "1", X = 0 });
        session.Nodes.Add(new FemNode { Id = 2, NodeTag = "2", X = 1 });
        session.Members.Add(new FemMember { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" });

        // Пользователь выделил только стержень, ни один узел явно не выбран.
        var snapshot = FemFragmentClipboard.Copy(session,
            nodeTags: new HashSet<string>(),
            memberTags: new HashSet<string> { "1" });

        Assert.Equal(2, snapshot.Nodes.Count);
        Assert.Single(snapshot.Members);
    }

    [Fact]
    public void Paste_GeneratesNewTagsAppliesOffsetAndRemapsReferences()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        // Id намеренно НЕ совпадает с NodeTag/ElemTag: ссылки в JSON идут по Tag, а не по БД-Id.
        session.Nodes.Add(new FemNode { Id = 101, NodeTag = "1", X = 0, Y = 0, Z = 0 });
        session.Nodes.Add(new FemNode { Id = 102, NodeTag = "2", X = 1, Y = 0, Z = 0 });
        session.Members.Add(new FemMember { Id = 201, ElemTag = "1", NodeIdsJson = "[1,2]",
            CrossSectionId = 5, GjStrategy = "manual", GjManualValue = 100 });
        session.MemberGroups.Add(new FemMemberGroup { Id = 1, Tag = "M1", MemberTagsJson = "[1]" });

        var snapshot = FemFragmentClipboard.Copy(session,
            new HashSet<string> { "1", "2" }, new HashSet<string> { "1" });

        session.Execute(new PasteFragmentCommand(snapshot, dx: 10, dy: 0, dz: 0));

        Assert.Equal(4, session.Nodes.Count);
        var pastedNode = session.Nodes.Single(n => n.NodeTag == "3");
        Assert.Equal(10, pastedNode.X);
        var pastedMember = session.Members.Single(m => m.ElemTag == "2");
        Assert.Equal(5, pastedMember.CrossSectionId);
        Assert.Equal("manual", pastedMember.GjStrategy);
        var pastedGroup = session.MemberGroups.Single(g => g.Tag != "M1");
        Assert.Equal("M1 (копия)", pastedGroup.Tag);

        session.Undo();
        Assert.Equal(2, session.Nodes.Count);
        Assert.Single(session.Members);
        Assert.Single(session.MemberGroups);
    }
}
