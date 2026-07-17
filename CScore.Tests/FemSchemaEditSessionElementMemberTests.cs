using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

public sealed class FemSchemaEditSessionElementMemberTests
{
    static FemSchemaEditSession NewSession() => new(new FemSchema { Id = 1 });

    [Fact]
    public void AddMemberCommand_AddsAndUndoRemoves()
    {
        var session = NewSession();
        var member = new FemMember { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" };

        session.Execute(new AddMemberCommand(member));
        Assert.Single(session.Members);

        session.Undo();
        Assert.Empty(session.Members);
    }

    [Fact]
    public void DeleteMemberCommand_RemovesFromGroupReferencesAndUndoRestores()
    {
        var session = NewSession();
        var member = new FemMember { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" };
        session.Members.Add(member);
        var group = new FemMemberGroup { Id = 1, Tag = "M1", MemberTagsJson = "[1]" };
        session.MemberGroups.Add(group);

        session.Execute(new DeleteMemberCommand(member));
        Assert.Empty(session.Members);
        Assert.Equal("[]", group.MemberTagsJson);

        session.Undo();
        Assert.Single(session.Members);
        Assert.Equal(JsonSerializer.Deserialize<int[]>("[1]"), JsonSerializer.Deserialize<int[]>(group.MemberTagsJson));
    }

    [Fact]
    public void CreateMemberGroupCommand_AddsAndUndoRemoves()
    {
        var session = NewSession();
        var group = new FemMemberGroup { Tag = "M1", MemberTagsJson = "[1]" };

        session.Execute(new CreateMemberGroupCommand(group));
        Assert.Single(session.MemberGroups);

        session.Undo();
        Assert.Empty(session.MemberGroups);
    }

    [Fact]
    public void SetMemberSectionCommand_UpdatesAndUndoRestores()
    {
        var session = NewSession();
        var member = new FemMember { Id = 1, ElemTag = "1", CrossSectionId = null };
        session.Members.Add(member);

        session.Execute(new SetMemberSectionCommand(member, 42));
        Assert.Equal(42, member.CrossSectionId);

        session.Undo();
        Assert.Null(member.CrossSectionId);
    }

    [Fact]
    public void SetMemberGjCommand_UpdatesAllThreeFieldsAndUndoRestores()
    {
        var session = NewSession();
        var member = new FemMember { Id = 1, ElemTag = "1", GjStrategy = "manual", GjManualValue = 100 };
        session.Members.Add(member);

        session.Execute(new SetMemberGjCommand(member, "saint_venant", null, 7));
        Assert.Equal("saint_venant", member.GjStrategy);
        Assert.Null(member.GjManualValue);
        Assert.Equal(7, member.GjTorsionTaskId);

        session.Undo();
        Assert.Equal("manual", member.GjStrategy);
        Assert.Equal(100, member.GjManualValue);
        Assert.Null(member.GjTorsionTaskId);
    }
}
