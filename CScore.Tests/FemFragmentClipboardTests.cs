using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

public sealed class FemFragmentClipboardTests
{
    [Fact]
    public void Copy_CapturesOnlySelectedNodesElementsAndFullyContainedMembers()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        session.Nodes.Add(new FemNode { Id = 1, NodeTag = "1", X = 0 });
        session.Nodes.Add(new FemNode { Id = 2, NodeTag = "2", X = 1 });
        session.Nodes.Add(new FemNode { Id = 3, NodeTag = "3", X = 2 });
        session.Elements.Add(new FemElement { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" });
        session.Elements.Add(new FemElement { Id = 2, ElemTag = "2", NodeIdsJson = "[2,3]" });
        session.Members.Add(new FemMember { Id = 1, Tag = "M1", ElemIdsJson = "[1]", CrossSectionId = 5 });

        var snapshot = FemFragmentClipboard.Copy(session,
            nodeTags: new HashSet<string> { "1", "2" },
            elemTags: new HashSet<string> { "1" });

        Assert.Equal(2, snapshot.Nodes.Count);
        Assert.Single(snapshot.Elements);
        Assert.Single(snapshot.Members);
    }

    [Fact]
    public void Paste_GeneratesNewTagsAppliesOffsetAndRemapsReferences()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        session.Nodes.Add(new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0 });
        session.Nodes.Add(new FemNode { Id = 2, NodeTag = "2", X = 1, Y = 0, Z = 0 });
        session.Elements.Add(new FemElement { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" });
        session.Members.Add(new FemMember { Id = 1, Tag = "M1", ElemIdsJson = "[1]", CrossSectionId = 5,
            GjStrategy = "manual", GjManualValue = 100 });

        var snapshot = FemFragmentClipboard.Copy(session,
            new HashSet<string> { "1", "2" }, new HashSet<string> { "1" });

        session.Execute(new PasteFragmentCommand(snapshot, dx: 10, dy: 0, dz: 0));

        Assert.Equal(4, session.Nodes.Count);
        var pastedNode = session.Nodes.Single(n => n.NodeTag == "3");
        Assert.Equal(10, pastedNode.X);
        var pastedElement = session.Elements.Single(e => e.ElemTag == "2");
        var pastedMember = session.Members.Single(m => m.Tag != "M1");
        Assert.Equal(5, pastedMember.CrossSectionId);
        Assert.Equal("manual", pastedMember.GjStrategy);

        session.Undo();
        Assert.Equal(2, session.Nodes.Count);
        Assert.Single(session.Elements);
        Assert.Single(session.Members);
    }
}
