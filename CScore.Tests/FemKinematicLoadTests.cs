using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

public sealed class FemKinematicLoadTests
{
    [Fact]
    public void SetKinematicLoadCommand_IsUndoable()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });

        session.Execute(new SetKinematicLoadCommand(2, 3, 1, 0.015));

        var load = Assert.Single(session.KinematicLoads);
        Assert.Equal(2, load.LoadCaseId);
        Assert.Equal(3, load.NodeId);
        Assert.Equal(1, load.Dof);
        Assert.Equal(0.015, load.Value);

        session.Undo();
        Assert.Empty(session.KinematicLoads);

        session.Redo();
        Assert.Equal(0.015, Assert.Single(session.KinematicLoads).Value);
    }

    [Fact]
    public void DeleteLoadCaseCommand_RemovesAndRestoresKinematicLoads()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        var loadCase = new FemLoadCase { Id = 2, SchemaId = 1, Tag = "Settlement" };
        session.LoadCases.Add(loadCase);
        session.KinematicLoads.Add(new FemKinematicLoad
        {
            SchemaId = 1, LoadCaseId = 2, NodeId = 3, Dof = 2, Value = -0.01
        });

        session.Execute(new DeleteLoadCaseCommand(loadCase));
        Assert.Empty(session.KinematicLoads);

        session.Undo();
        Assert.Single(session.KinematicLoads);
        Assert.Equal(-0.01, session.KinematicLoads[0].Value);
    }
}
