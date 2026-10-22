using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Команды редактора для параметров АЖТ примыкания стержней: применение к выбору и отмена.</summary>
public class RigidJunctionCommandTests
{
    const string Trace = "[[0,0,0],[1,0,0],[0,1,0]]";

    static (FemSchemaEditSession Session, FemMember A, FemMember B) TwoBars()
    {
        var session = new FemSchemaEditSession(new FemSchema { Tag = "S" });
        var a = new FemMember { ElemTag = "A", ElemType = "beam", RigidJunctionMask = 3, RigidJunctionTraceJson = Trace };
        var b = new FemMember
        {
            ElemTag = "B", ElemType = "beam",
            RigidJunctionMode = FemMember.RigidJunctionOff, RigidJunctionKind = FemMember.RigidJunctionCoupled,
        };
        session.Members.AddRange([a, b]);
        return (session, a, b);
    }

    [Fact]
    public void SetParams_AppliesToAll_UndoRestoresEachOwnValues()
    {
        var (session, a, b) = TwoBars();

        session.Execute(new SetRigidJunctionParamsCommand([a, b], null, 7, FemMember.RigidJunctionCoupled));
        foreach (var m in new[] { a, b })
            Assert.Equal(((string?)null, (int?)7, FemMember.RigidJunctionCoupled),
                (m.RigidJunctionMode, m.RigidJunctionMask, m.RigidJunctionKind));
        Assert.Equal(Trace, a.RigidJunctionTraceJson);

        session.Undo();
        Assert.Equal(((string?)null, (int?)3, (string?)null), (a.RigidJunctionMode, a.RigidJunctionMask, a.RigidJunctionKind));
        Assert.Equal((FemMember.RigidJunctionOff, (int?)null, FemMember.RigidJunctionCoupled),
            (b.RigidJunctionMode, b.RigidJunctionMask, b.RigidJunctionKind));
    }

    [Fact]
    public void ClearTrace_ResetsExplicitTrace_UndoRestores()
    {
        var (session, a, b) = TwoBars();
        b.RigidJunctionTraceJson = "[[5,5,0],[6,5,0],[5,6,0]]";

        session.Execute(new ClearRigidJunctionTraceCommand([a, b]));
        Assert.Null(a.RigidJunctionTraceJson);
        Assert.Null(b.RigidJunctionTraceJson);
        Assert.Equal(3, a.RigidJunctionMask);

        session.Undo();
        Assert.Equal(Trace, a.RigidJunctionTraceJson);
        Assert.Equal("[[5,5,0],[6,5,0],[5,6,0]]", b.RigidJunctionTraceJson);
    }
}
