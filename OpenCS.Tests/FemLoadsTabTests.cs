using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Вкладка «Загружения»: правка в ячейках командами редактора, строки по активному загружению.</summary>
[Collection("FEM schema editor VM")]
public sealed class FemLoadsTabTests(FemSchemaEditorVMFixture fixture)
{
    (FemSchemaEditorVM editor, FemLoadsTabVM tab, FemLoadCase g, FemLoadCase q) NewTab()
    {
        var editor = new FemSchemaEditorVM(new FemSchema { Id = 1, Tag = "S" }, fixture.App);
        editor.Session.Nodes.AddRange([new FemNode { Id = 11, NodeTag = "1" }, new FemNode { Id = 12, NodeTag = "2", X = 3 }]);
        editor.Session.Members.Add(new FemMember { Id = 21, ElemTag = "B1", ElemType = "beam", NodeIdsJson = "[1,2]" });
        var g = new FemLoadCase { Id = 1, SchemaId = 1, Tag = "G", Sp20Type = "permanent" };
        var q = new FemLoadCase { Id = 2, SchemaId = 1, Tag = "Q", Sp20Type = "short_term" };
        editor.Session.LoadCases.AddRange([g, q]);
        editor.RefreshCollections();
        editor.SelectedLoadCase = g;
        return (editor, new FemLoadsTabVM(editor), g, q);
    }

    [Fact]
    public void LoadCaseCells_EditOneUndoEach_AndRejectBadInput()
    {
        var (editor, tab, g, _) = NewTab();
        var row = tab.LoadCaseRows[0];
        Assert.True(row.IsActive);

        row.GammaFUnfav = "1,1";
        Assert.Equal(1.1, g.GammaFUnfav);
        row.SelfWeight = "1,05";
        Assert.Equal(1.05, g.SelfWeightFactor);

        row.GammaFFav = "abc";
        Assert.Null(g.GammaFFav);
        Assert.NotNull(tab.Status);
        row.SelfWeight = "0";
        Assert.Equal(1.05, g.SelfWeightFactor);

        row.Tag = "Q";
        Assert.Equal("G", g.Tag);
        Assert.NotNull(tab.Status);

        row.GammaFUnfav = "";
        Assert.Null(g.GammaFUnfav);

        editor.UndoCommand.Execute(null);
        Assert.Equal(1.1, g.GammaFUnfav);
        editor.UndoCommand.Execute(null);
        editor.UndoCommand.Execute(null);
        Assert.Null(g.GammaFUnfav);
        Assert.Null(g.SelfWeightFactor);
    }

    [Fact]
    public void SetSp20Type_SeveralRowsOneUndo()
    {
        var (editor, tab, g, q) = NewTab();
        tab.SetSp20Type(tab.LoadCaseRows, "long_term");
        Assert.Equal("long_term", g.Sp20Type);
        Assert.Equal("long_term", q.Sp20Type);
        editor.UndoCommand.Execute(null);
        Assert.Equal("permanent", g.Sp20Type);
        Assert.Equal("short_term", q.Sp20Type);
    }

    [Fact]
    public void LoadRows_FollowActiveLoadCase()
    {
        var (editor, tab, g, q) = NewTab();
        editor.Session.NodeLoads.Add(new FemNodeLoad { LoadCaseId = g.Id, NodeId = 11, Fz = -5000 });
        editor.Session.NodeLoads.Add(new FemNodeLoad { LoadCaseId = q.Id, NodeId = 12, Fz = -1000 });
        editor.Session.MeshNodeLoads.Add(new FemMeshNodeLoad { LoadCaseId = q.Id, MeshNodeTag = "77", Fx = 2000, Origin = FemLoadOrigin.Import("lira") });
        editor.RefreshCollections();

        var nodeRow = Assert.Single(tab.NodeLoadRows);
        Assert.Equal("1", nodeRow.NodeTag);
        Assert.Equal("-5", nodeRow.Fz);
        Assert.Empty(tab.MeshNodeLoadRows);

        tab.SelectedLoadCaseRow = tab.LoadCaseRows[1];
        Assert.Same(q, editor.SelectedLoadCase);
        Assert.Equal("2", Assert.Single(tab.NodeLoadRows).NodeTag);
        Assert.False(Assert.Single(tab.MeshNodeLoadRows).IsEditable);
        Assert.False(tab.LoadCaseRows[0].IsActive);
        Assert.True(tab.LoadCaseRows[1].IsActive);
    }

    [Fact]
    public void NodeLoadCell_KeepsRowAndOtherComponents()
    {
        var (editor, tab, g, _) = NewTab();
        editor.Selection.Set(["1", "2"], [], [], [], []);
        tab.AddNodeLoadsToSelection();
        Assert.Equal(2, tab.NodeLoadRows.Count);
        Assert.Equal(1, editor.Session.NodeLoads.Count(l => l.NodeId == 11));

        var row = tab.NodeLoadRows.First(r => r.NodeTag == "1");
        row.Fz = "-10";
        row.Mx = "2,5";
        Assert.Same(row, tab.NodeLoadRows.First(r => r.NodeTag == "1"));
        var load = editor.Session.NodeLoads.Single(l => l.NodeId == 11 && l.LoadCaseId == g.Id);
        Assert.Equal(-10000, load.Fz);
        Assert.Equal(2500, load.Mx);

        tab.AddNodeLoadsToSelection();
        Assert.Equal(2, editor.Session.NodeLoads.Count);
        Assert.NotNull(tab.Status);
    }

    [Fact]
    public void MemberLoadCells_UnsavedLoadEditedInPlace_UniformEndFollowsStart()
    {
        var (editor, tab, _, _) = NewTab();
        editor.Selection.Set([], ["B1"], [], [], []);
        tab.AddMemberLoadsToSelection();
        var row = Assert.Single(tab.MemberLoadRows);

        row.QzStart = "-8";
        var load = Assert.Single(editor.Session.MemberLoads);
        Assert.Equal(-8000, load.QzStart);
        Assert.Equal(-8000, load.QzEnd);

        row.QzEnd = "-4";
        Assert.Single(editor.Session.MemberLoads);
        Assert.Equal("trapezoidal", load.DistributionType);
        Assert.Equal(-8000, load.QzStart);
        Assert.Equal(-4000, load.QzEnd);

        row.DistributionType = "point";
        Assert.Equal(0, load.QzEnd);
        Assert.Equal("", row.QzEnd);
        row.My = "3";
        Assert.Equal(3000, load.My);

        editor.UndoCommand.Execute(null);
        editor.UndoCommand.Execute(null);
        Assert.Equal("trapezoidal", load.DistributionType);
        Assert.Equal(-4000, load.QzEnd);
    }

    [Fact]
    public void KinematicDofChange_IsOneUndo()
    {
        var (editor, tab, g, _) = NewTab();
        editor.Selection.Set(["2"], [], [], [], []);
        tab.AddKinematicLoadsToSelection();
        var row = Assert.Single(tab.KinematicLoadRows);
        Assert.Equal(3, row.Dof);
        row.Value = "-12";
        Assert.Equal(-0.012, editor.Session.KinematicLoads.Single().Value, 12);

        tab.KinematicLoadRows[0].Dof = 1;
        var moved = Assert.Single(editor.Session.KinematicLoads);
        Assert.Equal(1, moved.Dof);
        Assert.Equal(-0.012, moved.Value, 12);

        editor.UndoCommand.Execute(null);
        var back = Assert.Single(editor.Session.KinematicLoads);
        Assert.Equal(3, back.Dof);
        Assert.Equal(g.Id, back.LoadCaseId);
    }

    [Fact]
    public void ElementLoads_ImportReadOnly_ManualEditedByReplace_DeleteSeveralOneUndo()
    {
        var (editor, tab, g, _) = NewTab();
        var imported = new FemElementLoad { LoadCaseId = g.Id, Origin = FemLoadOrigin.Import("scad"), LoadKind = FemElementLoadKinds.Uniform };
        imported.SetTargetTags(["5"]);
        imported.SetValues([-2000]);
        var manual = new FemElementLoad { LoadCaseId = g.Id, LoadKind = FemElementLoadKinds.Uniform };
        manual.SetTargetTags(["6", "7"]);
        manual.SetValues([-3000]);
        editor.Session.ElementLoads.AddRange([imported, manual]);
        editor.RefreshCollections();

        var importedRow = tab.ElementLoadRows[0];
        Assert.False(importedRow.IsEditable);
        importedRow.Value = "-9";
        Assert.Equal(-2000, imported.Values[0]);

        var manualRow = tab.ElementLoadRows[1];
        Assert.True(manualRow.IsEditable);
        manualRow.Value = "-4,5";
        Assert.DoesNotContain(manual, editor.Session.ElementLoads);
        var replaced = editor.Session.ElementLoads[1];
        Assert.Equal(-4500, replaced.Values[0]);
        Assert.Equal(["6", "7"], replaced.TargetTags);
        tab.ElementLoadRows[1].Direction = "local:z";
        Assert.True(editor.Session.ElementLoads[1].IsLocal);

        tab.DeleteRows(tab.ElementLoadRows);
        Assert.Empty(editor.Session.ElementLoads);
        editor.UndoCommand.Execute(null);
        Assert.Equal(2, editor.Session.ElementLoads.Count);
        Assert.Same(imported, editor.Session.ElementLoads[0]);
    }

    [Fact]
    public void DeleteMeshNodeLoad_UndoRestoresPosition()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        var a = new FemMeshNodeLoad { MeshNodeTag = "1" };
        var b = new FemMeshNodeLoad { MeshNodeTag = "2" };
        session.MeshNodeLoads.AddRange([a, b]);
        session.Execute(new DeleteMeshNodeLoadCommand(a));
        Assert.Equal([b], session.MeshNodeLoads);
        session.Undo();
        Assert.Equal([a, b], session.MeshNodeLoads);
    }
}
