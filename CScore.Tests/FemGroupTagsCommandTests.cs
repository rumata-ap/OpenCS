using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

/// <summary>Правка состава группы в сеансе редактора (выбор в 3D): добавить/убрать и отмена.</summary>
public sealed class FemGroupTagsCommandTests
{
    [Fact]
    public void Add_SkipsPresentTags_UndoRestoresComposition()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        var group = FemGroupComposition.NewMeshGroup(1, ["1", "2"], "Плита", null);
        session.MemberGroups.Add(group);

        var command = new EditMemberGroupTagsCommand(group, ["2", "3", "4"], remove: false);
        session.Execute(command);
        Assert.Equal(2, command.Changed);
        Assert.Equal(["1", "2", "3", "4"], group.Tags);

        session.Undo();
        Assert.Equal(["1", "2"], group.Tags);
        session.Redo();
        Assert.Equal(["1", "2", "3", "4"], group.Tags);
    }

    [Fact]
    public void Remove_DropsOnlyListedTags_UndoRestoresOrder()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        var group = FemGroupComposition.NewMembersGroup(1, ["Колонна · 1", "Б1", "Б2"], "Каркас", null);
        session.MemberGroups.Add(group);

        var command = new EditMemberGroupTagsCommand(group, ["Б1", "Нет"], remove: true);
        session.Execute(command);
        Assert.Equal(1, command.Changed);
        Assert.Equal(["Колонна · 1", "Б2"], group.Tags);

        session.Undo();
        Assert.Equal(["Колонна · 1", "Б1", "Б2"], group.Tags);
    }
}
