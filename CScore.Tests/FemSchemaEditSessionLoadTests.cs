using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

public sealed class FemSchemaEditSessionLoadTests
{
    static FemSchemaEditSession NewSession() => new(new FemSchema { Id = 1 });

    [Fact]
    public void AddLoadCaseCommand_AddsAndUndoRemoves()
    {
        var session = NewSession();
        var loadCase = new FemLoadCase { Tag = "G", Sp20Type = "permanent" };

        session.Execute(new AddLoadCaseCommand(loadCase));
        Assert.Single(session.LoadCases);

        session.Undo();
        Assert.Empty(session.LoadCases);
    }

    [Fact]
    public void EditLoadCaseCommand_CopiesFieldsAndUndoRestores()
    {
        var session = NewSession();
        var loadCase = new FemLoadCase { Id = 1, Tag = "G", Sp20Type = "permanent" };
        session.LoadCases.Add(loadCase);
        var newValues = new FemLoadCase { Tag = "Q", Sp20Type = "short_term", GammaFUnfav = 1.3 };

        session.Execute(new EditLoadCaseCommand(loadCase, newValues));
        Assert.Equal("Q", loadCase.Tag);
        Assert.Equal(1.3, loadCase.GammaFUnfav);

        session.Undo();
        Assert.Equal("G", loadCase.Tag);
        Assert.Null(loadCase.GammaFUnfav);
    }

    [Fact]
    public void DeleteLoadCaseCommand_CascadesToNodeLoadsAndUndoRestores()
    {
        var session = NewSession();
        var loadCase = new FemLoadCase { Id = 1, Tag = "G" };
        session.LoadCases.Add(loadCase);
        session.NodeLoads.Add(new FemNodeLoad { Id = 1, LoadCaseId = 1, NodeId = 10, Fz = 5 });

        session.Execute(new DeleteLoadCaseCommand(loadCase));
        Assert.Empty(session.LoadCases);
        Assert.Empty(session.NodeLoads);

        session.Undo();
        Assert.Single(session.LoadCases);
        Assert.Single(session.NodeLoads);
    }

    [Fact]
    public void SetNodeLoadCommand_CreatesNewRowThenUpdatesInPlace()
    {
        var session = NewSession();

        session.Execute(new SetNodeLoadCommand(1, 10, fx: 1, fy: 0, fz: 0, mx: 0, my: 0, mz: 0));
        Assert.Single(session.NodeLoads);
        Assert.Equal(1, session.NodeLoads[0].Fx);
        Assert.Equal(session.Schema.Id, session.NodeLoads[0].SchemaId);

        session.Execute(new SetNodeLoadCommand(1, 10, fx: 9, fy: 0, fz: 0, mx: 0, my: 0, mz: 0));
        Assert.Single(session.NodeLoads);
        Assert.Equal(9, session.NodeLoads[0].Fx);

        session.Undo();
        Assert.Equal(1, session.NodeLoads[0].Fx);

        session.Undo();
        Assert.Empty(session.NodeLoads);
    }

    [Fact]
    public void DeleteNodeLoadCommand_RemovesAndUndoRestores()
    {
        var session = NewSession();
        var load = new FemNodeLoad { Id = 1, LoadCaseId = 1, NodeId = 10, Fz = 5 };
        session.NodeLoads.Add(load);

        session.Execute(new DeleteNodeLoadCommand(load));
        Assert.Empty(session.NodeLoads);

        session.Undo();
        Assert.Single(session.NodeLoads);
    }

    [Fact]
    public void ElementLoadCommands_AddReplaceDeleteAndUndo()
    {
        var session = NewSession();
        var lc = new FemLoadCase { Id = -1, Tag = "L1" };
        session.Execute(new AddLoadCaseCommand(lc));
        var a = new FemElementLoad { LoadCaseId = -1 };
        var b = new FemElementLoad { LoadCaseId = -1 };
        var c = new FemElementLoad { LoadCaseId = -1 };
        session.Execute(new AddElementLoadCommand(a));
        session.Execute(new AddElementLoadCommand(b));
        session.Execute(new ReplaceElementLoadCommand(a, c));
        Assert.Equal([c, b], session.ElementLoads);
        session.Execute(new DeleteElementLoadCommand(c));
        Assert.Equal([b], session.ElementLoads);
        session.Undo();
        Assert.Equal([c, b], session.ElementLoads);
        session.Undo();
        Assert.Equal([a, b], session.ElementLoads);

        session.Execute(new DeleteLoadCaseCommand(lc));
        Assert.Empty(session.ElementLoads);
        session.Undo();
        Assert.Equal(2, session.ElementLoads.Count);
    }
}
