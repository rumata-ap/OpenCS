using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit.Abstractions;
using static OpenCS.Gmsh.Tests.StoreyFragmentGmshFixture;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Фрагмент с сохранённой сеткой родителя на реальном Gmsh (спека 2026-11-10-fragment-keep-parent-mesh, срез 2):
/// родитель — плита 6×6 м на z = 3, стена y = 6 на всю высоту 0…6, колонна (3, 3) с узлом в плите; сетка Gmsh 1 м.
/// Фрагмент 1,5…4,5 м: плита — сетка и теги родителя, обрезанные стена и колонна — новая сетка, итоговая сетка связна.
/// </summary>
public sealed class StoreyFragmentKeepMeshTests(ITestOutputHelper output)
{
    const double Z1 = 1.5, Z2 = 4.5, Tol = 1e-6;

    static FemSchema CreateParent(DatabaseService db)
    {
        var schema = new FemSchema { Tag = "Каркас" };
        db.SaveFemSchema(schema);
        var slab = PlanarRegion.CreateFromContour(new Contour { X = [0, 6, 6, 0], Y = [0, 0, 6, 6] },
            frame: new Frame3D(new PlanarVector3(0, 0, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), tag: "S1");
        var wall = PlanarRegion.CreateFromContour(new Contour { X = [0, 6, 6, 0], Y = [0, 0, 6, 6] },
            frame: new Frame3D(new PlanarVector3(0, 6, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0)), tag: "W1");
        db.AddPlanarRegion(slab, schema.Id);
        db.AddPlanarRegion(wall, schema.Id);
        var nodes = new List<FemNode>
        {
            new() { Id = -1, NodeTag = "1", X = 3, Y = 3, Z = 0, DofMask = 63 },
            new() { Id = -2, NodeTag = "2", X = 3, Y = 3, Z = 3 },
            new() { Id = -3, NodeTag = "3", X = 3, Y = 3, Z = 6 },
        };
        var members = new List<FemMember>
        {
            new() { Id = -1, ElemTag = "S1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = slab.Id, Kind = "plate", ThicknessM = 0.2 },
            new() { Id = -2, ElemTag = "W1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = wall.Id, Kind = "wall", ThicknessM = 0.2 },
            new() { Id = -3, ElemTag = "C1a", ElemType = "beam", NodeIdsJson = "[1,2]" },
            new() { Id = -4, ElemTag = "C1b", ElemType = "beam", NodeIdsJson = "[2,3]" },
        };
        db.SaveFemSchemaEdit(schema.Id, nodes, members, [], [new FemLoadCase { Id = -1, Tag = "Пост." }], [], [], [], [], [], []);
        return db.FemSchemas.Single(s => s.Id == schema.Id);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.4)]
    public async Task KeptSlab_RebuiltWallAndColumn_ConnectedMesh(double fragmentStep)
    {
        await InTempRoot("opencs-fragment-keep", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var parent = CreateParent(db);
            await Mesh(db, parent, 1.0, root);
            var parentSlab = db.GetFemMeshElements(parent.Id).Where(e => e.SourceMemberTag == "S1").Select(e => e.ElemTag).ToHashSet();

            var built = StoreyFragmentService.Build(db, parent.Id, new StoreyFragmentRequest(Z1, Z2, "Этаж 2", KeepParentMesh: true));
            foreach (var d in built.Draft.Diagnostics) output.WriteLine($"{(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
            var fragment = Assert.IsType<FemSchema>(built.Schema);
            Assert.True(db.HasImportedMesh(fragment.Id));

            var meshed = await Mesh(db, fragment, fragmentStep, root);
            foreach (var d in meshed.Diagnostics) output.WriteLine($"сетка {(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
            Assert.Empty(meshed.ImportedMembersAtFault);
            var nodes = db.GetFemMeshNodes(fragment.Id).ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
            var elements = db.GetFemMeshElements(fragment.Id);
            output.WriteLine($"Фрагмент: узлов {nodes.Count}, КЭ {elements.Count}");

            // Плита — КЭ и теги родителя.
            var slab = elements.Where(e => e.SourceMemberTag == "S1").ToList();
            Assert.Equal(parentSlab.Order(), slab.Select(e => e.ElemTag).Order());
            Assert.All(slab, e => Assert.Equal(FemMember.MeshSourceImported, e.Origin));
            var slabNodes = slab.SelectMany(Ids).ToHashSet(StringComparer.Ordinal);

            // Стена и колонна перестроены и стоят на узлах плиты: каждый узел новой сетки на отметке плиты — узел плиты.
            var rebuilt = elements.Where(e => e.Origin != FemMember.MeshSourceImported).ToList();
            Assert.Contains(rebuilt, e => e.SourceMemberTag!.StartsWith("W1"));
            Assert.Contains(rebuilt, e => e.SourceMemberTag!.StartsWith("C1"));
            Assert.All(rebuilt.SelectMany(Ids).Where(t => Math.Abs(nodes[t].Z - 3) < Tol), t => Assert.Contains(t, slabNodes));

            // Связность: все КЭ — одна компонента по общим узлам.
            Assert.Equal(1, Components(elements));
        });
    }

    /// <summary>
    /// Высечение стены на всю высоту (призма x −1…7, y 5…7, полоса −1…7): стена делится плитой по ряду узлов сетки — оба
    /// куска сохраняют КЭ родителя; кусок плиты обрезан гранью призмы — перестроен и стоит на узлах стены.
    /// </summary>
    [Fact]
    public async Task KeptFullHeightWall_RebuiltSlabPiece_ConnectedMesh()
    {
        await InTempRoot("opencs-fragment-keep-wall", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var parent = CreateParent(db);
            await Mesh(db, parent, 1.0, root);
            var parentWall = db.GetFemMeshElements(parent.Id).Where(e => e.SourceMemberTag == "W1").Select(e => e.ElemTag).ToHashSet();

            var prism = FragmentPrism.Create(3, 6, 1, 0, 4, 1);
            var built = StoreyFragmentService.Build(db, parent.Id,
                new StoreyFragmentRequest(-1, 7, "Стена", Prism: prism, KeepParentMesh: true, MeshRebuildMemberTags: ["C1a", "C1b"]));
            foreach (var d in built.Draft.Diagnostics) output.WriteLine($"{(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
            var fragment = Assert.IsType<FemSchema>(built.Schema);

            var meshed = await Mesh(db, fragment, 0.5, root);
            Assert.Empty(meshed.ImportedMembersAtFault);
            var nodes = db.GetFemMeshNodes(fragment.Id).ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
            var elements = db.GetFemMeshElements(fragment.Id);

            var wall = elements.Where(e => e.SourceMemberTag!.StartsWith("W1")).ToList();
            Assert.Equal(parentWall.Order(), wall.Select(e => e.ElemTag).Order());
            Assert.All(wall, e => Assert.Equal(FemMember.MeshSourceImported, e.Origin));
            var wallNodes = wall.SelectMany(Ids).ToHashSet(StringComparer.Ordinal);
            var slab = elements.Where(e => e.SourceMemberTag!.StartsWith("S1")).ToList();
            Assert.NotEmpty(slab);
            Assert.All(slab, e => Assert.NotEqual(FemMember.MeshSourceImported, e.Origin));
            Assert.All(slab.SelectMany(Ids).Where(t => Math.Abs(nodes[t].Y - 6) < Tol), t => Assert.Contains(t, wallNodes));
            Assert.Equal(1, Components(elements));
        });
    }

    static IEnumerable<string> Ids(FemElement e) => JsonSerializer.Deserialize<int[]>(e.NodeIdsJson)!.Select(i => i.ToString());

    /// <summary>Число компонент связности сетки (КЭ связаны, если у них общий узел).</summary>
    static int Components(IReadOnlyList<FemElement> elements)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        string Find(string x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }
        foreach (var e in elements)
        {
            var ids = Ids(e).ToList();
            foreach (var t in ids) parent.TryAdd(t, t);
            foreach (var t in ids.Skip(1)) parent[Find(t)] = Find(ids[0]);
        }
        return parent.Keys.Select(Find).Distinct().Count();
    }
}
