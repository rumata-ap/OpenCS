using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Import;
using CScore.PlateRebar;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Раскладка OpenCS на импортированной сетке: данные из БД, мозаика, угол согласования осей КЭ.</summary>
public class PlateLayoutPersistenceTests
{
    static FemMeshNode N(string tag, double x, double y, double z) =>
        new() { NodeTag = tag, X = x, Y = y, Z = z, Origin = FemMember.MeshSourceImported };

    /// <summary>Пластина по узлам в порядке обхода; хранится, как в ЛИРЕ, «1 2 4 3».</summary>
    static FemElement Shell(string tag, double? axisAngle, params int[] nodes) => new()
    {
        ElemTag = tag, ElemType = "shell", LocalAxisAngleDeg = axisAngle, Origin = FemMember.MeshSourceImported,
        NodeIdsJson = JsonSerializer.Serialize(new[] { nodes[0], nodes[1], nodes[3], nodes[2] }),
    };

    static string NewPath() => Path.Combine(Path.GetTempPath(), "opencs_plate_layout_" + Guid.NewGuid().ToString("N") + ".db");

    /// <summary>Плита из двух КЭ 2×2 м на отметке 3 м (КЭ 1 — x 0…2, КЭ 2 — x 2…4) — один кБ ЛИРЫ.</summary>
    static FemSchema CreateSchema(DatabaseService db)
    {
        var schema = new FemSchema { Tag = "Схема", SourceType = "lira" };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id,
            [N("1", 0, 0, 3), N("2", 2, 0, 3), N("3", 2, 2, 3), N("4", 0, 2, 3), N("5", 4, 0, 3), N("6", 4, 2, 3)],
            [Shell("1", 0, 1, 2, 3, 4), Shell("2", null, 2, 5, 6, 3)]);
        db.SaveFemSchemaConstructiveBlocks(schema.Id, [new LiraConstructiveBlockRecord(7, "ПЛИТА", "1 этаж", "", "", [1, 2])]);
        return schema;
    }

    [Fact]
    public void LocalAxisAngle_IsSavedAndReplaced()
    {
        using var db = new DatabaseService(NewPath());
        var schema = CreateSchema(db);

        var mesh = db.GetFemMeshElements(schema.Id).ToDictionary(e => e.ElemTag);
        Assert.Equal(0, mesh["1"].LocalAxisAngleDeg);
        Assert.Null(mesh["2"].LocalAxisAngleDeg);

        int updated = db.ReplaceFemElementLocalAxisAngles(schema.Id, new Dictionary<string, double> { ["2"] = -72.5, ["99"] = 10 });

        Assert.Equal(1, updated);
        mesh = db.GetFemMeshElements(schema.Id).ToDictionary(e => e.ElemTag);
        Assert.Null(mesh["1"].LocalAxisAngleDeg);   // КЭ нет в таблице источника — угол неизвестен
        Assert.Equal(-72.5, mesh["2"].LocalAxisAngleDeg);
    }

    [Fact]
    public void MigrationV68_AddsAxisAngleColumn()
    {
        string path = NewPath();
        int schemaId;
        using (var db = new DatabaseService(path))
            schemaId = CreateSchema(db).Id;
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE fem_elements DROP COLUMN local_axis_angle_deg; " +
                              "UPDATE settings SET value_json = '67' WHERE key = 'schema_version'";
            cmd.ExecuteNonQuery();
        }

        using (var db = new DatabaseService(path))
        {
            var mesh = db.GetFemMeshElements(schemaId);
            Assert.Equal(2, mesh.Count);
            Assert.All(mesh, e => Assert.Null(e.LocalAxisAngleDeg));
            Assert.Equal(1, db.ReplaceFemElementLocalAxisAngles(schemaId, new Dictionary<string, double> { ["1"] = 15 }));
        }
    }

    [Fact]
    public void Layout_FromDatabase_FeedsCheckSourceAndMosaic()
    {
        using var db = new DatabaseService(NewPath());
        var schema = CreateSchema(db);
        var blocks = db.GetLiraBlocks(schema.Id);
        var nodes = db.GetFemMeshNodes(schema.Id);
        var elements = db.GetFemMeshElements(schema.Id);
        db.ApplyLiraBlockMembers(schema.Id, blocks, blocks.Select(b => LiraBlockMemberBuilder.Build(b, nodes, elements)).ToList());

        const double z = 0.065;
        var section = new PlateSection
        {
            Tag = "Пл200", H = 0.2,
            RebarLayers =
            [
                new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = -z, Zsy = -z, Face = RebarFace.MinusN },
                new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = z, Zsy = z, Face = RebarFace.PlusN },
            ],
        };
        db.SavePlateSection(section);
        var member = db.GetFemMembers(schema.Id).Single();
        member.PlateSectionId = section.Id;
        db.SaveFemMember(member);

        // Зона в локальных координатах элемента накрывает центроид КЭ 1 (глобально x 0…2, y 0…2).
        var region = db.GetPlanarRegions(schema.Id).Single();
        var local = CScore.Planar.PlanarBoundaryFrameConverter.ToLocalPoint(region.Frame, new CScore.Planar.PlanarVector3(1, 1, 3));
        region.RebarZones.Add(new RebarZone
        {
            Name = "Усиление", Face = RebarFace.MinusN, Operation = RebarZoneOperation.Add,
            Polygon =
            [
                new() { U = local.X - 0.5, V = local.Y - 0.5 }, new() { U = local.X + 0.5, V = local.Y - 0.5 },
                new() { U = local.X + 0.5, V = local.Y + 0.5 }, new() { U = local.X - 0.5, V = local.Y + 0.5 },
            ],
            Layout = new PlateRebarLayer { InputMode = "direct", Asx = 10e-4, Asy = 0, Zsx = -z, Zsy = -z },
        });
        db.UpdatePlanarRegion(region, schema.Id);

        // Источник армирования проверки
        var data = FemCheckSchemaData.Load(db, schema.Id);
        var resolver = data.LayoutResolver(db.PlateSections);
        Assert.Equal("Раскладка: фон + Усиление", resolver.Resolve("1").Label);
        Assert.Equal("Раскладка: фон", resolver.Resolve("2").Label);
        Assert.True(resolver.Resolve("1").AxesKnown);
        Assert.Equal(0, resolver.Resolve("1").ForceAngleDeg);
        Assert.False(resolver.Resolve("2").AxesKnown);
        var scope = data.Scope(data.Members.Single());
        Assert.All(scope.Elements, e => Assert.NotNull(e.Member?.PlanarRegionId));

        // Мозаика «Раскладка OpenCS»
        var mosaicData = PlateRebarMosaicVM.ReadAll(db, schema.Id);
        Assert.NotNull(mosaicData.Layout);
        var vm = new PlateRebarMosaicVM();
        vm.Apply(mosaicData);
        vm.SelectedSource = vm.SourceOptions.Single(o => o.Kind == PlateRebarMosaicSourceKind.Layout);
        vm.SelectedComponent = vm.ComponentOptions.Single(o => Equals(o.Component, PlateRebarMosaicComponent.BottomX));
        Assert.NotNull(vm.Compute(["1", "2"]));
        vm.SetHover("1");
        Assert.Contains(15.0.ToString(), vm.HoverText);
        vm.SetHover("2");
        Assert.Contains(5.0.ToString(), vm.HoverText);
        // Без подобранной арматуры разность с ней не предлагается.
        Assert.DoesNotContain(vm.SourceOptions, o => o.Kind == PlateRebarMosaicSourceKind.LayoutDifference);

        // Правка зоны меняет ключ данных — мозаика перечитывается.
        region.RebarZones[0].Layout.Asx = 20e-4;
        db.UpdatePlanarRegion(region, schema.Id);
        Assert.NotEqual(mosaicData.Key, PlateRebarMosaicVM.ReadAll(db, schema.Id).Key);
    }
}
