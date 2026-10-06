using System.Text.Json;
using System.Windows.Media.Media3D;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Выбор КЭ импортированной сетки в 3D-виде редактора: что выбирается и как подсвечивается.</summary>
public sealed class Fem3DMeshPickTests
{
    static FemMeshNode N(string tag, double x, double y, double z) =>
        new() { NodeTag = tag, X = x, Y = y, Z = z, Origin = FemMember.MeshSourceImported };

    /// <summary>Четырёхузловой КЭ хранится, как в ЛИРЕ, «1 2 4 3».</summary>
    static FemElement E(string tag, string type, params int[] nodes) =>
        new() { ElemTag = tag, ElemType = type,
                NodeIdsJson = JsonSerializer.Serialize(nodes.Length == 4 ? new[] { nodes[0], nodes[1], nodes[3], nodes[2] } : nodes),
                ThicknessM = type == "shell" ? 0.2 : null, Origin = FemMember.MeshSourceImported };

    /// <summary>Колонна 1–2–3 (КЭ 1, 2) и плита 3-4-5-6 (КЭ 3) с кБ на каждую.</summary>
    static (DatabaseService Db, FemSchema Schema, string Path) CreateSchema()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opencs_mesh_pick_" + Guid.NewGuid().ToString("N") + ".db");
        var db = new DatabaseService(path);
        var schema = new FemSchema { Tag = "Схема", SourceType = "lira" };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id,
            [N("1", 0, 0, 0), N("2", 0, 0, 1.5), N("3", 0, 0, 3), N("4", 2, 0, 3), N("5", 2, 2, 3), N("6", 0, 2, 3)],
            [E("1", "beam", 1, 2), E("2", "beam", 2, 3), E("3", "shell", 3, 4, 5, 6)]);
        db.SaveFemSchemaConstructiveBlocks(schema.Id,
        [
            new LiraConstructiveBlockRecord(1, "КОЛОННА", "1 этаж", "К-1", "", [1, 2]),
            new LiraConstructiveBlockRecord(2, "ПЛИТА", "1 этаж", "", "", [3]),
        ]);
        return (db, schema, path);
    }

    static Fem3DVM LoadEditView(DatabaseService db, FemSchema schema)
    {
        var vm = new Fem3DVM(schema, db) { EditMode = true, Selection = new FemSchemaSelectionVM() };
        vm.LoadAsync().GetAwaiter().GetResult();
        return vm;
    }

    static void Cleanup(DatabaseService db, string path)
    {
        db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(path); } catch (IOException) { }
    }

    [Fact]
    public void PureImport_AllMeshElementsPickable()
    {
        var (db, schema, path) = CreateSchema();
        try
        {
            var vm = LoadEditView(db, schema);

            Assert.True(vm.MeshIsSchema);
            Assert.True(vm.HasPickableMesh);
            Assert.Equal(["1", "2"], vm.MeshPickBars.Select(b => b.Tag).Order());
            Assert.NotNull(vm.ShellMesh);
            Assert.Equal("3", vm.MeshShellTagAt(vm.ShellMesh!, 0));
        }
        finally { Cleanup(db, path); }
    }

    [Fact]
    public void WithBlockMembers_MeshIsUnderlay_StillPickable_AndHighlighted()
    {
        var (db, schema, path) = CreateSchema();
        try
        {
            var nodes = db.GetFemMeshNodes(schema.Id);
            var elements = db.GetFemMeshElements(schema.Id);
            var blocks = db.GetLiraBlocks(schema.Id);
            db.ApplyLiraBlockMembers(schema.Id, blocks, blocks.Select(b => LiraBlockMemberBuilder.Build(b, nodes, elements)).ToList());

            var vm = LoadEditView(db, schema);

            Assert.False(vm.MeshIsSchema);
            Assert.True(vm.HasPickableMesh);
            Assert.Equal(["1", "2"], vm.MeshPickBars.Select(b => b.Tag).Order());
            Assert.Equal("3", vm.MeshShellTagAt(vm.ShellMesh!, 0));

            var (bars, shells, outlines) = vm.MeshSelectionGeometry(["1", "3", "нет"], 0.01);
            Assert.Equal([new Point3D(0, 0, 0), new Point3D(0, 0, 1.5)], bars);
            // Пластина — две копии по обе стороны (±0,01 по нормали), по два треугольника.
            Assert.NotNull(shells);
            Assert.Equal(8, shells!.Positions.Count);
            Assert.Equal(12, shells.TriangleIndices.Count);
            Assert.Equal([3.01, 2.99], shells.Positions.Select(p => Math.Round(p.Z, 6)).Distinct().Order().Reverse());
            // Контур по обходу 3-4-5-6, а не по порядку хранения.
            Assert.Equal(8, outlines.Count);
            Assert.Equal(new Point3D(2, 0, 3), outlines[1]);
            Assert.Equal(new Point3D(2, 2, 3), outlines[3]);
        }
        finally { Cleanup(db, path); }
    }
}
