using System.Text;
using CScore.Fem;
using CScore.Import;
using CScore.Sp16;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Стальные профили SCAD при схеме: вложение scad_steel_profiles → FemCheckSchemaData.</summary>
public sealed class ScadSteelProfilesStorageTests
{
    [Fact]
    public void SaveAndLoad_ThroughSchemaData()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-steel-profiles-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "SCAD", SourceType = "scad" };
            db.SaveFemSchema(schema);
            var shape = new ImportedSteelShape(SteelProfileKind.Box, SteelFabrication.Bent,
                0.1, 0.1, 0.004, 0.004, 0.004, 0, 0, "ГОСТ 30245-2012", "100x4", 14.95, 225.1, 225.1);
            ScadSteelProfileEntry[] entries =
            [
                new(2, "RUSSIAN okv2012 59", shape, null),
                new(3, "OTHER x 1", null, "нет сортамента SCAD OTHER.PRF"),
            ];
            db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadSteelProfiles, "",
                Encoding.UTF8.GetBytes(ScadSteelProfileIndex.ToJson(entries)));

            var data = FemCheckSchemaData.Load(db, schema.Id);

            Assert.Empty(data.Errors);
            Assert.NotNull(data.ScadSteelProfiles);
            Assert.Equal(shape, data.ScadSteelProfiles!.Find(2)!.Shape);
            Assert.Equal("нет сортамента SCAD OTHER.PRF", data.ScadSteelProfiles.Find(3)!.Reason);
            Assert.Null(data.ScadSteelProfiles.Find(1));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void NoAttachment_Null()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-steel-profiles-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "SCAD", SourceType = "scad" };
            db.SaveFemSchema(schema);
            Assert.Null(FemCheckSchemaData.Load(db, schema.Id).ScadSteelProfiles);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Resolve_StzOnly_MissingBaseHasReason()
    {
        LiraStiffnessRecord[] stiffnesses =
        [
            new(1, ScadStiffnessParams.ScadKindCode, "", "S0 3e7 30 50", 0.01),
            new(2, ScadStiffnessParams.ScadKindCode, "", "STZ RUSSIAN okv2012 59 TMP 1.2e-05", 1),
        ];

        var entries = OpenCS.Services.Scad.ScadSteelProfileLoader.Resolve(stiffnesses, prfDirectory: null);

        var e = Assert.Single(entries);
        Assert.Equal(2, e.Num);
        Assert.Equal("RUSSIAN okv2012 59", e.Source);
        Assert.Equal("нет сортамента SCAD RUSSIAN.PRF", e.Reason);
    }
}
