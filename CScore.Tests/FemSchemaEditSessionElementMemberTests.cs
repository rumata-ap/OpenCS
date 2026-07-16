using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

public sealed class FemSchemaEditSessionElementMemberTests
{
    static FemSchemaEditSession NewSession() => new(new FemSchema { Id = 1 });

    [Fact]
    public void AddElementCommand_AddsAndUndoRemoves()
    {
        var session = NewSession();
        var element = new FemElement { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" };

        session.Execute(new AddElementCommand(element));
        Assert.Single(session.Elements);

        session.Undo();
        Assert.Empty(session.Elements);
    }

    [Fact]
    public void DeleteElementCommand_RemovesFromMemberReferencesAndUndoRestores()
    {
        var session = NewSession();
        var element = new FemElement { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" };
        session.Elements.Add(element);
        var member = new FemMember { Id = 1, Tag = "M1", ElemIdsJson = "[1]" };
        session.Members.Add(member);

        session.Execute(new DeleteElementCommand(element));
        Assert.Empty(session.Elements);
        Assert.Equal("[]", member.ElemIdsJson);

        session.Undo();
        Assert.Single(session.Elements);
        Assert.Equal(JsonSerializer.Deserialize<int[]>("[1]"), JsonSerializer.Deserialize<int[]>(member.ElemIdsJson));
    }

    [Fact]
    public void CreateMemberCommand_AddsAndUndoRemoves()
    {
        var session = NewSession();
        var member = new FemMember { Tag = "M1", ElemIdsJson = "[1]" };

        session.Execute(new CreateMemberCommand(member));
        Assert.Single(session.Members);

        session.Undo();
        Assert.Empty(session.Members);
    }

    [Fact]
    public void SetMemberSectionCommand_UpdatesAndUndoRestores()
    {
        var session = NewSession();
        var member = new FemMember { Id = 1, Tag = "M1", CrossSectionId = null };
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
        var member = new FemMember { Id = 1, Tag = "M1", GjStrategy = "manual", GjManualValue = 100 };
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
