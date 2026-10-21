using CScore.Fem;
using CScore.Fem.Fields;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Ветка «Поля из таблиц»: поля в порядке наложения, подпись с числом целей по кэшу и точек.</summary>
public sealed class FemFieldTreeVMTests
{
    [Fact]
    public void Reload_ListsFieldsInOrder_WithCounts()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-field-tree-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "S" };
            db.SaveFemSchema(schema);
            FemField Field(string name, string quantity) => new()
            {
                SchemaId = schema.Id, Name = name, Quantity = quantity,
                Definition = new FemFieldDefinition { Columns = [new FemFieldColumn("V", quantity == FemFieldQuantities.Spring ? "kz" : "c1", "Н", 1)] },
                Points = [new(new(0, 0, 0), [1]), new(new(1, 0, 0), [2])],
            };
            var c1 = Field("C1 из ГТО", FemFieldQuantities.FoundationC1);
            var rz = Field("Rz", FemFieldQuantities.Spring);
            db.SaveFemField(c1);
            db.SaveFemField(rz);
            db.ReplaceFemFieldValues(c1.Id,
                [new(c1.Id, FemFieldTargetKind.Shell, "1", "c1", 5, FieldHow.Interpolated, 0),
                 new(c1.Id, FemFieldTargetKind.Shell, "2", "c1", 6, FieldHow.Nearest, 0.2),
                 new(c1.Id, FemFieldTargetKind.Shell, "3", "c1", null, FieldHow.None, double.PositiveInfinity)], "S");
            db.SetFemFieldEnabled(rz.Id, false);

            var node = new FemFieldsSubNode(schema);
            node.Reload(db);
            Assert.Equal(["C1 из ГТО", "Rz"], node.Fields.Select(f => f.Field.Name));
            Assert.Equal(2, node.Fields[0].TargetCount);   // КЭ без значения не считается
            Assert.Equal(0, node.Fields[1].TargetCount);
            Assert.False(node.Fields[0].IsSpring);
            Assert.True(node.Fields[1].IsSpring);
            Assert.True(node.Fields[0].Enabled);
            Assert.False(node.Fields[1].Enabled);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void MeshNodeRow_ShowsFieldSources_NotTechnicalFieldOrigin()
    {
        var row = new FemMeshNodeRow(new FemMeshNode { NodeTag = "7" });
        row.Apply([],
            [new FemSpring { NodeTag = "7", Kz = 5, Origin = FemFieldOverlay.FieldOrigin }],
            ["поле «Rz»"]);
        Assert.Equal("поле «Rz»", row.OriginText);
        Assert.Equal(5, row.Stiffnesses[2]);

        row.SetManual(0, [0, 0, 9, 0, 0, 0]);
        Assert.Empty(row.ValueSources);
    }
}
