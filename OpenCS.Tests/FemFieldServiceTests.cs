using CScore.Fem;
using CScore.Fem.Fields;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Эффективные C1 и пружины: поле переживает повторный импорт ГУ, пересчёт при смене сетки, ручные пружины.</summary>
public sealed class FemFieldServiceTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-field-svc-{Guid.NewGuid():N}.db");
    static readonly string Lira = FemLoadOrigin.Import("lira");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    static List<FemMeshNode> Nodes(double dz) =>
    [
        new() { NodeTag = "1", X = 0, Y = 0, Z = dz, Origin = FemMember.MeshSourceImported },
        new() { NodeTag = "2", X = 1, Y = 0, Z = dz, Origin = FemMember.MeshSourceImported },
        new() { NodeTag = "3", X = 1, Y = 1, Z = dz, Origin = FemMember.MeshSourceImported },
        new() { NodeTag = "4", X = 0, Y = 1, Z = dz, Origin = FemMember.MeshSourceImported },
    ];

    /// <summary>Пластина 1 × 1 м; узлы в порядке хранения сетки «1 2 4 3» (как в ЛИРЕ/SCAD).</summary>
    static FemElement Shell() => new()
    {
        ElemTag = "10", ElemType = "shell", NodeIdsJson = "[1,2,4,3]", Origin = FemMember.MeshSourceImported, FoundationC1 = 1000,
    };

    (DatabaseService Db, FemSchema Schema) Seed()
    {
        var db = new DatabaseService(_path);
        var schema = new FemSchema { Tag = "S", SourceType = "lira" };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id, Nodes(0), [Shell()]);
        db.SaveFemBoundary(schema.Id, Lira, [], [new FemSpring { NodeTag = "1", Kx = 1, Ky = 1, Kz = 5, SourceElemTag = "56" }], []);
        db.LoadAll();
        return (db, schema);
    }

    static FemField ConstField(int schemaId, string quantity, string component, double value, FieldMethod method) => new()
    {
        SchemaId = schemaId, Name = quantity, Quantity = quantity, HasZ = false,
        Definition = new FemFieldDefinition
        {
            Columns = [new FemFieldColumn("V", component, "Н", 1)],
            Settings = new FieldSettings(method, MaxDistance: 5, PlaneTolerance: 0.5, MatchTolerance: 0.5),
        },
        Points = method == FieldMethod.Match
            ? [new(new(0.1, 0, 0), [value])]
            : [new(new(-1, -1, 0), [value]), new(new(2, -1, 0), [value]), new(new(-1, 2, 0), [value]), new(new(2, 2, 0), [value])],
    };

    [Fact]
    public void NoFields_ReturnsRawData()
    {
        var (db, schema) = Seed();
        using (db)
        {
            var eff = FemFieldService.Effective(db, schema.Id);
            Assert.Equal(1000, Assert.Single(eff.Elements).FoundationC1);
            Assert.Equal(5, Assert.Single(eff.Springs).Kz);
            Assert.Empty(eff.Sources);
        }
    }

    [Fact]
    public void Field_SurvivesBoundaryReimport_AndSpringIsPerDof()
    {
        var (db, schema) = Seed();
        using (db)
        {
            var c1 = ConstField(schema.Id, FemFieldQuantities.FoundationC1, "c1", 2000, FieldMethod.Interpolate);
            var kz = ConstField(schema.Id, FemFieldQuantities.Spring, "kz", 7, FieldMethod.Match);
            db.SaveFemField(c1);
            db.SaveFemField(kz);
            FemFieldService.Recompute(db, c1);
            FemFieldService.Recompute(db, kz);

            db.SaveFemBoundary(schema.Id, Lira, [], [new FemSpring { NodeTag = "1", Kx = 1, Ky = 1, Kz = 6, SourceElemTag = "56" }], [],
                new Dictionary<string, FemElementBoundaryProps> { ["10"] = new(null, null, 3000) });

            var eff = FemFieldService.Effective(db, schema.Id);
            Assert.Equal(2000, Assert.Single(eff.Elements).FoundationC1);
            Assert.Equal([1, 1, 7, 0, 0, 0], Assert.Single(eff.Springs).Stiffnesses);
            Assert.DoesNotContain(eff.Diagnostics, d => d.Code == "field_recomputed");
        }
    }

    [Fact]
    public void MeshChange_RecomputesCache()
    {
        var (db, schema) = Seed();
        using (db)
        {
            var c1 = ConstField(schema.Id, FemFieldQuantities.FoundationC1, "c1", 2000, FieldMethod.Interpolate);
            db.SaveFemField(c1);
            FemFieldService.Recompute(db, c1);
            string sig = db.GetFemFields(schema.Id)[0].MeshSignature!;

            db.SaveFemMeshSnapshot(schema.Id, Nodes(0.2), [Shell()]);
            var eff = FemFieldService.Effective(db, schema.Id);
            Assert.Contains(eff.Diagnostics, d => d.Code == "field_recomputed");
            Assert.NotEqual(sig, db.GetFemFields(schema.Id)[0].MeshSignature);
            Assert.Equal(2000, Assert.Single(eff.Elements).FoundationC1);
        }
    }

    [Fact]
    public void InvalidateAnalyses_ResetsResults()
    {
        var (db, schema) = Seed();
        using (db)
        {
            var analysis = new FemAnalysis { SchemaId = schema.Id, Tag = "A", ResultId = 123 };
            db.SaveFemAnalysis(analysis);
            db.LoadAll();
            Assert.Equal(1, FemFieldService.InvalidateAnalyses(db, schema.Id));
        }
    }
}
