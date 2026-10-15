using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

/// <summary>Составная команда — одна отмена; событие изменения сессии — для дерева схемы до сохранения.</summary>
public sealed class FemSchemaEditSessionCompositeTests
{
    [Fact]
    public void Composite_DoesInOrder_UndoesAsOne_AndRaisesChanged()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        FemNode a = new() { NodeTag = "1", DofMask = 7 }, b = new() { NodeTag = "2" };
        session.Nodes.AddRange([a, b]);
        int changed = 0;
        session.Changed += () => changed++;

        session.Execute(new CompositeFemCommand([new SetDofMasksCommand([a], 0), new SetDofMasksCommand([b], 63)]));
        Assert.Equal((0, 63), (a.DofMask, b.DofMask));

        session.Undo();
        Assert.Equal((7, 0), (a.DofMask, b.DofMask));
        Assert.False(session.CanUndo);

        session.Redo();
        Assert.Equal((0, 63), (a.DofMask, b.DofMask));
        session.MarkSaved();
        Assert.Equal(4, changed);
    }
}
