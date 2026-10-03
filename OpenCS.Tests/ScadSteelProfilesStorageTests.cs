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
            SteelProfileEntry[] entries =
            [
                new(2, "RUSSIAN okv2012 59", shape, null),
                new(3, "OTHER x 1", null, "нет сортамента SCAD OTHER.PRF"),
            ];
            db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadSteelProfiles, "",
                Encoding.UTF8.GetBytes(SteelProfileIndex.ToJson(entries)));

            var data = FemCheckSchemaData.Load(db, schema.Id);

            Assert.Empty(data.Errors);
            Assert.NotNull(data.SteelProfiles);
            Assert.Equal(shape, data.SteelProfiles!.Find(2)!.Shape);
            Assert.Equal("нет сортамента SCAD OTHER.PRF", data.SteelProfiles.Find(3)!.Reason);
            Assert.Null(data.SteelProfiles.Find(1));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Lira_SaveAndLoad_ThroughSchemaData()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-steel-profiles-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "ЛИРА", SourceType = "lira" };
            db.SaveFemSchema(schema);
            var shape = new ImportedSteelShape(SteelProfileKind.Box, SteelFabrication.Bent,
                0.08, 0.08, 0.003, 0.003, 0.003, 0, 0, "ГОСТ 30245-94", "80 x 3", 8.96, 87.8, 87.8);
            db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.LiraSteelProfiles, "",
                Encoding.UTF8.GetBytes(SteelProfileIndex.ToJson([new(14, "gn-kv94.profiles.srt: 80 x 3", shape, null, "С245")])));

            var data = FemCheckSchemaData.Load(db, schema.Id);

            Assert.Empty(data.Errors);
            Assert.Equal(shape, data.SteelProfiles!.Find(14)!.Shape);
            Assert.Equal("С245", data.SteelProfiles.Find(14)!.SteelMark);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Lira_Resolve_Kind1018Only_MissingFileHasReason()
    {
        LiraStiffnessRecord[] stiffnesses =
        [
            new(1, LiraStiffnessParams.BarRectKind, "Брус", "B:30 H:50 BAR_END", 0.01),
            new(14, LiraSteelProfiles.SteelKindCode, "", "Section = Tubing  MatId = STL  File  = |gn-kv94.profiles.srt|  Shape = |80 x 3|", 0.01),
        ];

        var entries = LiraSteelProfileLoader.Resolve(stiffnesses, []);

        var e = Assert.Single(entries);
        Assert.Equal(14, e.Num);
        Assert.Equal("gn-kv94.profiles.srt: 80 x 3", e.Source);
        Assert.Equal("нет сортамента ЛИРЫ gn-kv94.profiles.srt", e.Reason);
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
            Assert.Null(FemCheckSchemaData.Load(db, schema.Id).SteelProfiles);
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
