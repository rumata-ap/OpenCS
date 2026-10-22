using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Сводка параметров АЖТ для набора КонЭ (диалог «АЖТ примыкания…») и правка с неопределёнными полями.</summary>
public class RigidJunctionParamsEditTests
{
    static (FemSchemaEditSession Session, FemMember A, FemMember B) TwoBars()
    {
        var session = new FemSchemaEditSession(new FemSchema { Tag = "S" });
        // A: авто, все DOF, жёсткое тело, явный след; B: не создавать, только X Y Z, объединение.
        var a = new FemMember { ElemTag = "A", ElemType = "beam", RigidJunctionTraceJson = "[[0,0,0],[1,0,0],[0,1,0]]" };
        var b = new FemMember
        {
            ElemTag = "B", ElemType = "beam", RigidJunctionMode = FemMember.RigidJunctionOff,
            RigidJunctionMask = 0b000111, RigidJunctionKind = FemMember.RigidJunctionCoupled,
        };
        session.Members.AddRange([a, b]);
        return (session, a, b);
    }

    [Fact]
    public void Summary_SingleMember_AllValuesDefined()
    {
        var (_, a, _) = TwoBars();
        var s = RigidJunctionParamsSummary.Of([a]);
        Assert.Equal(false, s.Off);
        Assert.Equal(false, s.Coupled);
        Assert.All(s.Dofs, d => Assert.Equal(true, d));
        Assert.Equal(1, s.TraceCount);
    }

    [Fact]
    public void Summary_DifferentMembers_MixedIsNull()
    {
        var (_, a, b) = TwoBars();
        var s = RigidJunctionParamsSummary.Of([a, b]);
        Assert.Null(s.Off);
        Assert.Null(s.Coupled);
        Assert.Equal(new bool?[] { true, true, true, null, null, null }, s.Dofs);
        Assert.Equal(1, s.TraceCount);
    }

    [Fact]
    public void Build_MixedFieldsKeepOwnValues_OneUndo()
    {
        var (session, a, b) = TwoBars();
        // Режим всем «авто», вид не трогать, X снять, повороты не трогать.
        var cmd = RigidJunctionParamsSummary.BuildCommand([a, b], off: false, coupled: null,
            [false, true, true, null, null, null], clearTrace: false);
        Assert.NotNull(cmd);
        session.Execute(cmd!);

        Assert.Equal(((string?)null, (int?)0b111110, (string?)null), (a.RigidJunctionMode, a.RigidJunctionMask, a.RigidJunctionKind));
        Assert.Equal(((string?)null, (int?)0b000110, FemMember.RigidJunctionCoupled),
            (b.RigidJunctionMode, b.RigidJunctionMask, b.RigidJunctionKind));
        Assert.NotNull(a.RigidJunctionTraceJson);

        session.Undo();
        Assert.Equal(((string?)null, (int?)null, (string?)null), (a.RigidJunctionMode, a.RigidJunctionMask, a.RigidJunctionKind));
        Assert.Equal((FemMember.RigidJunctionOff, (int?)0b000111, FemMember.RigidJunctionCoupled),
            (b.RigidJunctionMode, b.RigidJunctionMask, b.RigidJunctionKind));
    }

    [Fact]
    public void Build_AllDofsIsNullMask_ClearTraceInSameUndo()
    {
        var (session, a, b) = TwoBars();
        var cmd = RigidJunctionParamsSummary.BuildCommand([a, b], off: null, coupled: false,
            [true, true, true, true, true, true], clearTrace: true);
        session.Execute(cmd!);

        Assert.Null(b.RigidJunctionMask);
        Assert.Null(b.RigidJunctionKind);
        Assert.Equal(FemMember.RigidJunctionOff, b.RigidJunctionMode);
        Assert.Null(a.RigidJunctionTraceJson);

        session.Undo();
        Assert.NotNull(a.RigidJunctionTraceJson);
        Assert.Equal(0b000111, b.RigidJunctionMask);
    }

    [Fact]
    public void Build_NothingChanged_ReturnsNull()
    {
        var (_, a, b) = TwoBars();
        Assert.Null(RigidJunctionParamsSummary.BuildCommand([a, b], null, null, new bool?[6], clearTrace: false));
        Assert.Null(RigidJunctionParamsSummary.BuildCommand([b], null, null, new bool?[6], clearTrace: true));
    }
}
