using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemMemberLoadDatabaseTests
{
    [Fact]
    public void MemberLoad_RoundTripsThroughDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-member-load-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Member load test" };
            db.SaveFemSchema(schema);
            var nodes = new[]
            {
                new FemNode { SchemaId = schema.Id, NodeTag = "1" },
                new FemNode { SchemaId = schema.Id, NodeTag = "2" }
            };
            db.SaveFemTopology(schema.Id, nodes, [new FemMember
            {
                SchemaId = schema.Id, ElemTag = "10", NodeIdsJson = "[1,2]"
            }], []);
            var member = db.GetFemMembers(schema.Id).Single();
            var loadCase = new FemLoadCase { SchemaId = schema.Id, Tag = "Q" };
            db.SaveFemLoadCase(loadCase);
            var load = new FemMemberLoad
            {
                SchemaId = schema.Id, LoadCaseId = loadCase.Id, MemberId = member.Id,
                CoordinateSystem = "global", DistributionType = "trapezoidal",
                StartOffsetM = 0.4, EndOffsetM = 0.7,
                QxStart = 100, QyStart = -200, QzStart = 300,
                QxEnd = 400, QyEnd = -500, QzEnd = 600
            };

            db.SaveFemMemberLoad(load);
            var copy = db.GetFemMemberLoads(schema.Id).Single();

            Assert.Equal(load.MemberId, copy.MemberId);
            Assert.Equal("global", copy.CoordinateSystem);
            Assert.Equal(0.4, copy.StartOffsetM);
            Assert.Equal(600, copy.QzEnd);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveFemSchemaEdit_RemapsTransientMemberAndLoadCaseReferences()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-member-load-edit-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Transient member load" };
            db.SaveFemSchema(schema);
            var node1 = new FemNode { Id = -1, SchemaId = schema.Id, NodeTag = "1" };
            var node2 = new FemNode { Id = -2, SchemaId = schema.Id, NodeTag = "2" };
            var member = new FemMember
            {
                Id = -3, SchemaId = schema.Id, ElemTag = "10", NodeIdsJson = "[1,2]"
            };
            var loadCase = new FemLoadCase
            {
                Id = -4, SchemaId = schema.Id, Tag = "G", Sp20Type = "permanent"
            };
            var memberLoad = new FemMemberLoad
            {
                SchemaId = schema.Id, LoadCaseId = loadCase.Id, MemberId = member.Id,
                QzStart = -1000, QzEnd = -1000
            };

            db.SaveFemSchemaEdit(schema.Id, [node1, node2], [member], [], [loadCase], [],
                [memberLoad], []);

            var savedMember = db.GetFemMembers(schema.Id).Single();
            var savedCase = db.GetFemLoadCases(schema.Id).Single();
            var savedLoad = db.GetFemMemberLoads(schema.Id).Single();
            Assert.True(savedMember.Id > 0);
            Assert.True(savedCase.Id > 0);
            Assert.Equal(savedMember.Id, savedLoad.MemberId);
            Assert.Equal(savedCase.Id, savedLoad.LoadCaseId);
            Assert.Equal(-1000, savedLoad.QzEnd);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveFemTopology_PreservesMemberLoadByMemberTag()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-member-load-topology-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Member topology" };
            db.SaveFemSchema(schema);
            var loadCase = new FemLoadCase { SchemaId = schema.Id, Tag = "Q" };
            db.SaveFemLoadCase(loadCase);
            db.SaveFemTopology(schema.Id,
                [new FemNode { NodeTag = "1" }, new FemNode { NodeTag = "2" }],
                [new FemMember { ElemTag = "10", NodeIdsJson = "[1,2]" }], []);
            var member = db.GetFemMembers(schema.Id).Single();
            db.SaveFemMemberLoad(new FemMemberLoad
            {
                SchemaId = schema.Id, LoadCaseId = loadCase.Id, MemberId = member.Id,
                DistributionType = "trapezoidal", QyStart = -100, QyEnd = -300
            });

            db.SaveFemTopology(schema.Id,
                [new FemNode { NodeTag = "1", X = 2 }, new FemNode { NodeTag = "2", X = 5 }],
                [new FemMember { ElemTag = "10", NodeIdsJson = "[1,2]" }], []);

            var savedLoad = db.GetFemMemberLoads(schema.Id).Single();
            Assert.Equal(db.GetFemMembers(schema.Id).Single().Id, savedLoad.MemberId);
            Assert.Equal(-300, savedLoad.QyEnd);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
