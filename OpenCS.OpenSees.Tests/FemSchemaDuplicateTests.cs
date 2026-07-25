using CScore.Fem;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class FemSchemaDuplicateTests
{
    [Fact]
    public void DuplicateFemSchema_CreatesIndependentCopyWithRemappedReferences()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-dup-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Original", SourceType = "internal" };
            db.SaveFemSchema(schema);

            var node1 = new FemNode { NodeTag = "1", X = 0, Y = 0, Z = 0, DofMask = 63 };
            var node2 = new FemNode { NodeTag = "2", X = 3, Y = 0, Z = 0 };
            var member = new FemMember { ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]" };
            var group = new FemMemberGroup { Tag = "G1", MemberTagsJson = "[\"1\"]" };
            db.SaveFemSchemaEdit(schema.Id, [node1, node2], [member], [group], [], []);

            var loadCase = new FemLoadCase { Tag = "LC1", Sp20Type = "short_term" };
            db.SaveFemSchemaEdit(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), [group], [loadCase], []);
            loadCase = db.GetFemLoadCases(schema.Id).Single();

            var nodeLoad = new FemNodeLoad
            {
                LoadCaseId = loadCase.Id,
                NodeId = db.GetFemNodes(schema.Id).First(n => n.NodeTag == "2").Id,
                Fz = -1000
            };
            db.SaveFemSchemaEdit(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), [group],
                db.GetFemLoadCases(schema.Id), [nodeLoad]);
            // SaveFemSchemaEdit пересоздаёт fem_load_cases (новые id) на каждом вызове — берём
            // актуальный id заново, а не полагаемся на закешированный loadCase.Id (он бы указывал
            // на уже удалённую строку).
            loadCase = db.GetFemLoadCases(schema.Id).Single();

            var definition = new FemLoadDefinition { SchemaId = schema.Id, Tag = "D1", SourceKind = "manual" };
            definition.SetExpression(new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Single,
                LoadCaseIds = [loadCase.Id]
            });
            db.SaveFemSchemaEdit(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), [group],
                db.GetFemLoadCases(schema.Id), db.GetFemNodeLoads(schema.Id), [], [], [definition]);

            var (meshNodes, meshElements) = FemMeshDiscretizer.Discretize(
                schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), null);
            db.SaveFemMeshSnapshot(schema.Id, meshNodes, meshElements);

            // Копируем
            var copy = db.DuplicateFemSchema(schema.Id, "Original (копия)");

            Assert.NotEqual(schema.Id, copy.Id);
            Assert.Equal("Original (копия)", copy.Tag);

            var copyNodes = db.GetFemNodes(copy.Id);
            var copyMembers = db.GetFemMembers(copy.Id);
            Assert.Equal(2, copyNodes.Count);
            Assert.Single(copyMembers);
            Assert.NotEqual(node1.Id, copyNodes.First(n => n.NodeTag == "1").Id);
            // node_ids_json тег-based — переносится без изменений (не SQL id!)
            Assert.Equal(member.NodeIdsJson, copyMembers[0].NodeIdsJson);

            var copyLoadCases = db.GetFemLoadCases(copy.Id);
            Assert.Single(copyLoadCases);
            Assert.NotEqual(loadCase.Id, copyLoadCases[0].Id);

            var copyNodeLoads = db.GetFemNodeLoads(copy.Id);
            Assert.Single(copyNodeLoads);
            Assert.Equal(copyLoadCases[0].Id, copyNodeLoads[0].LoadCaseId);
            Assert.Equal(copyNodes.First(n => n.NodeTag == "2").Id, copyNodeLoads[0].NodeId);
            Assert.Equal(-1000, copyNodeLoads[0].Fz);

            var copyDefinitions = db.GetFemLoadDefinitions(copy.Id);
            Assert.Single(copyDefinitions);
            var copyExpr = copyDefinitions[0].GetExpression();
            Assert.Equal(copyLoadCases[0].Id, copyExpr.LoadCaseIds.Single());

            var copyMeshNodes = db.GetFemMeshNodes(copy.Id);
            var copyMeshElements = db.GetFemMeshElements(copy.Id);
            Assert.Equal(meshNodes.Count, copyMeshNodes.Count);
            Assert.Equal(meshElements.Count, copyMeshElements.Count);

            // Исходная схема не тронута
            Assert.Equal(2, db.GetFemNodes(schema.Id).Count);
            Assert.Single(db.GetFemLoadCases(schema.Id));
            Assert.Single(db.GetFemNodeLoads(schema.Id));

            // Группа скопирована независимо
            db.LoadAll();
            var reloadedCopy = db.FemSchemas.Single(s => s.Id == copy.Id);
            Assert.Single(reloadedCopy.MemberGroups);
            Assert.Equal("G1", reloadedCopy.MemberGroups[0].Tag);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DuplicateFemSchema_ThrowsForUnknownSourceSchema()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-dup-missing-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            Assert.Throws<InvalidOperationException>(() => db.DuplicateFemSchema(999, "X"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
