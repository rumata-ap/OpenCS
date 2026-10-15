using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Узловая связь узлов КонЭ в редакторе: закрепления и пружины одной командой, замена, снятие, отмена.</summary>
public sealed class NodeBoundaryCommandTests
{
    static FemSchemaEditSession Session()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 5, Tag = "S" });
        session.Nodes.Add(new FemNode { Id = 1, NodeTag = "1" });
        session.Nodes.Add(new FemNode { Id = 2, NodeTag = "2", DofMask = 0b111 });
        return session;
    }

    [Fact]
    public void Set_MaskAndSprings_ReplacesPerNode_UndoRestores()
    {
        var session = Session();
        var nodes = session.Nodes.ToList();
        session.Execute(new SetNodeBoundaryCommand(nodes, 0b011, [0, 0, 2e7, 0, 0, 0]));
        Assert.All(session.Nodes, n => Assert.Equal(0b011, n.DofMask));
        Assert.Equal(2, session.NodeSprings.Count);
        Assert.All(session.NodeSprings, s =>
        {
            Assert.Equal(FemSpringTargetKinds.Node, s.TargetKind);
            Assert.Equal(FemLoadOrigin.Manual, s.Origin);
            Assert.Equal(5, s.SchemaId);
            Assert.Equal(2e7, s.Kz);
        });

        // Второй узел: другая пружина заменяет прежнюю, не добавляется.
        session.Execute(new SetNodeBoundaryCommand([nodes[1]], 0, [0, 0, 0, 3e5, 0, 0]));
        Assert.Equal(2, session.NodeSprings.Count);
        var second = session.NodeSprings.Single(s => s.NodeTag == "2");
        Assert.Equal(0, second.Kz);
        Assert.Equal(3e5, second.Kux);

        // Нулевые жёсткости — пружина снимается.
        session.Execute(new SetNodeBoundaryCommand([nodes[0]], 0, new double[6]));
        Assert.Equal("2", Assert.Single(session.NodeSprings).NodeTag);

        session.Undo();
        session.Undo();
        Assert.Equal(2e7, session.NodeSprings.Single(s => s.NodeTag == "2").Kz);
        session.Undo();
        Assert.Empty(session.NodeSprings);
        Assert.Equal(0, nodes[0].DofMask);
        Assert.Equal(0b111, nodes[1].DofMask);
        session.Redo();
        Assert.Equal(2, session.NodeSprings.Count);
    }

    [Fact]
    public void Constructor_RequiresSixStiffnesses() =>
        Assert.Throws<ArgumentException>(() => new SetNodeBoundaryCommand([], 0, [1, 2]));
}
