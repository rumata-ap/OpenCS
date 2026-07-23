using CScore.Fem;
using Xunit;

namespace CScore.Tests;

public sealed class FemMemberLoadTests
{
    static (FemSchema Schema, FemLoadCase LoadCase, FemMember Member) Fixture()
    {
        return (
            new FemSchema { Id = 1 },
            new FemLoadCase { Id = 2, SchemaId = 1, Tag = "Q", Sp20Type = "short_term" },
            new FemMember { Id = 3, SchemaId = 1, ElemTag = "10", NodeIdsJson = "[1,2]" });
    }

    [Fact]
    public void Validator_RejectsUniformLoadWithDifferentEndIntensity()
    {
        var (schema, loadCase, member) = Fixture();
        var load = new FemMemberLoad
        {
            Id = 4, SchemaId = 1, LoadCaseId = loadCase.Id, MemberId = member.Id,
            DistributionType = "uniform", QyStart = 1000, QyEnd = 2000
        };

        var diagnostics = FemCanonicalValidator.Validate(
            schema, [loadCase], [], [], [member], [load]);

        Assert.Contains(diagnostics, d => d.Code == "member_load_uniform_end_mismatch");
    }

    [Fact]
    public void Validator_RejectsUnknownCoordinateSystemAndNegativeOffset()
    {
        var (schema, loadCase, member) = Fixture();
        var load = new FemMemberLoad
        {
            Id = 4, SchemaId = 1, LoadCaseId = loadCase.Id, MemberId = member.Id,
            CoordinateSystem = "diagonal", StartOffsetM = -0.1
        };

        var diagnostics = FemCanonicalValidator.Validate(
            schema, [loadCase], [], [], [member], [load]);

        Assert.Contains(diagnostics, d => d.Code == "member_load_coordinate_system_invalid");
        Assert.Contains(diagnostics, d => d.Code == "member_load_offset_invalid");
    }

    [Fact]
    public void Validator_RejectsMissingMemberAndNonFiniteIntensity()
    {
        var (schema, loadCase, _) = Fixture();
        var load = new FemMemberLoad
        {
            Id = 4, SchemaId = 1, LoadCaseId = loadCase.Id, MemberId = 99,
            QzStart = double.NaN
        };

        var diagnostics = FemCanonicalValidator.Validate(
            schema, [loadCase], [], [], [], [load]);

        Assert.Contains(diagnostics, d => d.Code == "member_missing");
        Assert.Contains(diagnostics, d => d.Code == "member_load_component_not_finite");
    }

    [Fact]
    public void SetMemberLoadCommand_IsUndoable()
    {
        var schema = new FemSchema { Id = 1 };
        var session = new CScore.Fem.Editing.FemSchemaEditSession(schema);
        var load = new FemMemberLoad
        {
            LoadCaseId = 2, MemberId = 3, DistributionType = "trapezoidal",
            StartOffsetM = 1, EndOffsetM = 2, QyStart = -100, QyEnd = -200
        };

        session.Execute(new CScore.Fem.Editing.SetMemberLoadCommand(load));
        Assert.Equal(load.LoadCaseId, session.MemberLoads.Single().LoadCaseId);
        Assert.Equal(1, session.MemberLoads.Single().SchemaId);

        session.Undo();
        Assert.Empty(session.MemberLoads);
        session.Redo();
        Assert.Single(session.MemberLoads);
        Assert.Equal(-200, session.MemberLoads[0].QyEnd);
    }

    [Fact]
    public void DeleteLoadCaseCommand_RemovesAndRestoresMemberLoads()
    {
        var loadCase = new FemLoadCase { Id = 2, SchemaId = 1, Tag = "Q" };
        var schema = new FemSchema { Id = 1 };
        var session = new CScore.Fem.Editing.FemSchemaEditSession(schema);
        session.LoadCases.Add(loadCase);
        session.MemberLoads.Add(new FemMemberLoad { LoadCaseId = loadCase.Id, MemberId = 3 });

        session.Execute(new CScore.Fem.Editing.DeleteLoadCaseCommand(loadCase));

        Assert.Empty(session.MemberLoads);
        session.Undo();
        Assert.Single(session.MemberLoads);
    }

    [Fact]
    public void DeleteMemberCommand_RemovesAndRestoresMemberLoads()
    {
        var schema = new FemSchema { Id = 1 };
        var member = new FemMember { Id = 3, ElemTag = "10" };
        var session = new CScore.Fem.Editing.FemSchemaEditSession(schema);
        session.Members.Add(member);
        session.MemberLoads.Add(new FemMemberLoad { MemberId = member.Id });

        session.Execute(new CScore.Fem.Editing.DeleteMemberCommand(member));

        Assert.Empty(session.MemberLoads);
        session.Undo();
        Assert.Single(session.MemberLoads);
    }
}
