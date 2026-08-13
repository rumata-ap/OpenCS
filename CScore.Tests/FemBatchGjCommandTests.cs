using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

public sealed class FemBatchGjCommandTests
{
    [Fact]
    public void BatchCommandChangesAllMembersAndUndoRedoRestoresAllGjFields()
    {
        var session = new FemSchemaEditSession(new FemSchema { Tag = "GJ" });
        var first = new FemMember
        {
            GjStrategy = "manual",
            GjManualValue = 10,
            GjTorsionTaskId = 1
        };
        var second = new FemMember
        {
            GjStrategy = "saint_venant",
            GjManualValue = null,
            GjTorsionTaskId = 2
        };
        session.Members.AddRange([first, second]);

        session.Execute(new SetMembersGjCommand(
        [
            new MemberGjAssignment(first, "manual", 100, null),
            new MemberGjAssignment(second, "manual", 200, null),
        ]));

        Assert.Equal(("manual", 100d, null), (first.GjStrategy, first.GjManualValue, first.GjTorsionTaskId));
        Assert.Equal(("manual", 200d, null), (second.GjStrategy, second.GjManualValue, second.GjTorsionTaskId));

        session.Undo();

        Assert.Equal(("manual", 10d, 1), (first.GjStrategy, first.GjManualValue, first.GjTorsionTaskId));
        Assert.Equal(("saint_venant", null, 2), (second.GjStrategy, second.GjManualValue, second.GjTorsionTaskId));

        session.Redo();

        Assert.Equal(("manual", 100d, null), (first.GjStrategy, first.GjManualValue, first.GjTorsionTaskId));
        Assert.Equal(("manual", 200d, null), (second.GjStrategy, second.GjManualValue, second.GjTorsionTaskId));
    }

    [Fact]
    public void SaintVenantMemberOutsideAssignmentsIsUntouched()
    {
        var session = new FemSchemaEditSession(new FemSchema { Tag = "GJ" });
        var saintVenant = new FemMember
        {
            GjStrategy = "saint_venant",
            GjTorsionTaskId = 42
        };
        var manual = new FemMember { GjStrategy = "manual", GjManualValue = 10 };
        session.Members.AddRange([saintVenant, manual]);

        session.Execute(new SetMembersGjCommand(
        [new MemberGjAssignment(manual, "manual", 20, null)]));

        Assert.Equal("saint_venant", saintVenant.GjStrategy);
        Assert.Null(saintVenant.GjManualValue);
        Assert.Equal(42, saintVenant.GjTorsionTaskId);
    }
}
