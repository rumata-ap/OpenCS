using CScore.Fem;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Назначение сечения группе: конструктивный слой и схемы «только сетка» (импорт ЛИРА/SCAD).</summary>
public class FemMemberGroupCrossSectionTests
{
    static DatabaseService NewDb() => new(Path.Combine(Path.GetTempPath(),
        "opencs_group_section_" + Guid.NewGuid().ToString("N") + ".db"));

    [Fact]
    public void MeshOnlySchema_SectionIsStoredOnMeshElementsOfGroup()
    {
        using var db = NewDb();
        var schema = new FemSchema { Tag = "Схема Лира (API)", SourceType = "lira" };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id,
            [new FemMeshNode { NodeTag = "1", X = 0 }, new FemMeshNode { NodeTag = "2", X = 1 },
             new FemMeshNode { NodeTag = "3", X = 2 }],
            [new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]" },
             new FemElement { ElemTag = "2", NodeIdsJson = "[2,3]" }]);
        var group = new FemMemberGroup { SchemaId = schema.Id, Tag = "Колонна1", MemberTagsJson = "[1]" };

        Assert.True(db.IsFemConstructiveLayerEmpty(schema.Id));
        Assert.Null(db.GetFemMemberGroupCrossSectionId(group));

        db.SetFemMemberGroupCrossSection(group, 42);

        Assert.Equal(42, db.GetFemMemberGroupCrossSectionId(group));
        var mesh = db.GetFemMeshElements(schema.Id);
        Assert.Equal(42, mesh.Single(e => e.ElemTag == "1").CrossSectionId);
        Assert.Null(mesh.Single(e => e.ElemTag == "2").CrossSectionId);
        // Конструктивный слой не создаётся — схема остаётся «только сетка» для 3D-вида и расчёта.
        Assert.Empty(db.GetFemMembers(schema.Id));
        Assert.Empty(db.GetFemNodes(schema.Id));
    }

    [Fact]
    public void ConstructiveSchema_SectionIsStoredOnFemMembersOfGroup()
    {
        using var db = NewDb();
        var schema = new FemSchema { Tag = "Схема" };
        db.SaveFemSchema(schema);
        db.SaveFemTopology(schema.Id,
            [new FemNode { SchemaId = schema.Id, NodeTag = "1", X = 0 },
             new FemNode { SchemaId = schema.Id, NodeTag = "2", X = 1 }],
            [new FemMember { SchemaId = schema.Id, ElemTag = "7", NodeIdsJson = "[1,2]" }], []);
        db.SaveFemMeshSnapshot(schema.Id,
            [new FemMeshNode { NodeTag = "1", X = 0 }, new FemMeshNode { NodeTag = "2", X = 1 }],
            [new FemElement { ElemTag = "7", NodeIdsJson = "[1,2]", SourceMemberTag = "7" }]);
        var group = new FemMemberGroup { SchemaId = schema.Id, Tag = "Балка", MemberTagsJson = "[7]" };

        Assert.False(db.IsFemConstructiveLayerEmpty(schema.Id));

        db.SetFemMemberGroupCrossSection(group, 5);

        Assert.Equal(5, db.GetFemMemberGroupCrossSectionId(group));
        Assert.Equal(5, db.GetFemMembers(schema.Id).Single().CrossSectionId);
        // Снимок сетки не трогаем — он пересобирается из конструктивного слоя явным «Дискретизировать».
        Assert.Null(db.GetFemMeshElements(schema.Id).Single().CrossSectionId);
    }
}
