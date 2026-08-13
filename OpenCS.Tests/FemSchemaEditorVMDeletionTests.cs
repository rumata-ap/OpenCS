using CScore;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

sealed class NullFileDialogService : IFileDialogService
{
    public string? OpenFile(string? filter = null, string? title = null) => null;
    public string? SaveFile(string? filter = null, string? defaultExt = null, string? title = null) => null;
    public string? SelectFolder(string? title = null, string? initialDirectory = null) => null;
}

[CollectionDefinition("FEM schema editor VM", DisableParallelization = true)]
public sealed class FemSchemaEditorVMCollection : ICollectionFixture<FemSchemaEditorVMFixture>
{
}

public sealed class FemSchemaEditorVMFixture
{
    public AppViewModel App { get; } = new(new LogService(), new NullFileDialogService());
}

[Collection("FEM schema editor VM")]
public sealed class FemSchemaEditorVMDeletionTests(FemSchemaEditorVMFixture fixture)
{
    FemSchemaEditorVM NewEditor() => new(new FemSchema { Id = 1 }, fixture.App);

    [Fact]
    public void DeleteNodesByTags_UsesImpactAndClearsSelection_AndUndoRestoresTopology()
    {
        var editor = NewEditor();
        var node = new FemNode { Id = 101, NodeTag = "1" };
        var other = new FemNode { Id = 102, NodeTag = "2" };
        editor.Session.Nodes.AddRange([node, other]);
        editor.Session.Members.Add(new FemMember { Id = 201, ElemTag = "1", NodeIdsJson = "[1,2]" });
        editor.Selection.ToggleNode(node.NodeTag, additive: false);

        var impact = editor.GetNodeDeletionImpact([node.NodeTag]);
        Assert.Equal(1, impact.NodeCount);
        Assert.Equal(1, impact.MemberCount);

        Assert.True(editor.DeleteNodesByTags([node.NodeTag]));
        Assert.DoesNotContain(node, editor.Session.Nodes);
        Assert.Empty(editor.Session.Members);
        Assert.Empty(editor.Selection.SelectedNodeTags);

        editor.UndoCommand.Execute(null);

        Assert.Contains(node, editor.Session.Nodes);
        Assert.Single(editor.Session.Members);
        Assert.Empty(editor.Selection.SelectedNodeTags);
    }

    [Fact]
    public void UndoUnrelatedCommand_PreservesCurrentSelection()
    {
        var editor = NewEditor();
        var node = new FemNode { Id = 101, NodeTag = "1" };
        editor.Session.Nodes.Add(node);
        editor.Session.Execute(new MoveNodeCommand(node, 5, 0, 0));
        editor.Selection.ToggleNode(node.NodeTag, additive: false);

        editor.UndoCommand.Execute(null);

        Assert.Equal(0, node.X);
        Assert.Contains(node.NodeTag, editor.Selection.SelectedNodeTags);
    }
}
