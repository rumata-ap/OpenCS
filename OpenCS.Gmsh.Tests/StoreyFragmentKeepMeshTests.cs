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

    /// <summary>Родитель; колонна — сечение К400 (в памяти): у её узла в плите — авто-АЖТ примыкания. <paramref name="rib"/> —
    /// ещё ребро B1 в плоскости плиты (1, 1, 3)–(5, 1, 3) тем же сечением.</summary>
    static FemSchema CreateParent(DatabaseService db, bool rib = false)
    {
        var column = AddSections(db).Column;
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
            new() { Id = -3, ElemTag = "C1a", ElemType = "beam", NodeIdsJson = "[1,2]", CrossSectionId = column.Id },
            new() { Id = -4, ElemTag = "C1b", ElemType = "beam", NodeIdsJson = "[2,3]", CrossSectionId = column.Id },
        };
        if (rib)
        {
            nodes.Add(new() { Id = -4, NodeTag = "4", X = 1, Y = 1, Z = 3 });
            nodes.Add(new() { Id = -5, NodeTag = "5", X = 5, Y = 1, Z = 3 });
            members.Add(new() { Id = -5, ElemTag = "B1", ElemType = "beam", NodeIdsJson = "[4,5]", CrossSectionId = column.Id });
        }
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

            // АЖТ колонны в сохранённой плите — тело родителя по тегам (срез 3), своих тел примыканий нет; ведущий —
            // узел перестроенной колонны.
            var parentBody = Assert.Single(db.GetFemRigidBodies(parent.Id), b => b.Origin == RigidJunctionTraceSet.Provenance);
            var bodies = db.GetFemRigidBodies(fragment.Id);
            var body = Assert.Single(bodies);
            Assert.Equal(StoreyFragmentBuilder.ParentRigidOriginPrefix + RigidJunctionTraceSet.Provenance, body.Origin);
            Assert.Equal(parentBody.MasterNodeTag, body.MasterNodeTag);
            Assert.Equal(parentBody.SlaveNodeTags.Order(), body.SlaveNodeTags.Order());
            Assert.Contains(rebuilt.Where(e => e.SourceMemberTag!.StartsWith("C1")), e => Ids(e).Contains(body.MasterNodeTag));
            Assert.All(body.SlaveNodeTags, t => Assert.Contains(t, slabNodes));

            // Повторная сборка тело не стирает.
            await Mesh(db, fragment, fragmentStep, root);
            Assert.Single(db.GetFemRigidBodies(fragment.Id), b => b.Origin == body.Origin);
        });
    }

    /// <summary>
    /// Ручная команда «Перестроить сетку КонЭ» (срез 5): снятие замка с сохранённой плиты без новой сетки — импортные КЭ
    /// плиты удалены, сгенерированная сетка стены и колонны осталась, проверка видит сетку устаревшей; сборка строит
    /// плиту заново, сетка связна.
    /// </summary>
    [Fact]
    public async Task ManualRemesh_KeptSlab_MeshOutdatedThenRebuilt()
    {
        await InTempRoot("opencs-fragment-remesh", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var parent = CreateParent(db);
            await Mesh(db, parent, 1.0, root);
            var built = StoreyFragmentService.Build(db, parent.Id, new StoreyFragmentRequest(Z1, Z2, "Этаж 2"));
            var fragment = Assert.IsType<FemSchema>(built.Schema);
            Assert.True(fragment.FragmentKeepParentMesh); // умолчание запроса — сохранять
            await Mesh(db, fragment, 1.0, root);
            var before = db.GetFemMeshElements(fragment.Id);
            var generatedBefore = before.Where(e => e.Origin != FemMember.MeshSourceImported).Select(e => e.ElemTag).Order().ToList();
            Assert.Contains(before, e => e.SourceMemberTag == "S1" && e.Origin == FemMember.MeshSourceImported);

            var service = new FemSchemaMeshService(db, GmshSettings(root));
            var plan = service.UnlockKeptMembers(fragment.Id, db.GetFemNodes(fragment.Id), db.GetFemMembers(fragment.Id), ["S1"]);
            foreach (var d in plan.Diagnostics) output.WriteLine($"{d.Code}: {d.Message}");
            Assert.Equal(["S1"], plan.UnlockedMemberTags);

            Assert.False(db.GetFemMembers(fragment.Id).Single(m => m.ElemTag == "S1").IsMeshLocked);
            var after = db.GetFemMeshElements(fragment.Id);
            Assert.DoesNotContain(after, e => e.SourceMemberTag == "S1");
            Assert.Equal(generatedBefore, after.Where(e => e.Origin != FemMember.MeshSourceImported).Select(e => e.ElemTag).Order());
            // Узлы сетки, на которые ссылаются оставшиеся КЭ, на месте.
            var nodeTags = db.GetFemMeshNodes(fragment.Id).Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
            Assert.All(after.SelectMany(Ids), t => Assert.Contains(t, nodeTags));

            var check = await service.CheckAsync(fragment.Id, db.GetFemNodes(fragment.Id), db.GetFemMembers(fragment.Id), 1.0,
                CancellationToken.None);
            Assert.False(check.IsCurrent);

            var meshed = await Mesh(db, fragment, 1.0, root);
            var elements = db.GetFemMeshElements(fragment.Id);
            var slab = elements.Where(e => e.SourceMemberTag == "S1").ToList();
            Assert.NotEmpty(slab);
            Assert.All(slab, e => Assert.NotEqual(FemMember.MeshSourceImported, e.Origin));
            Assert.Equal(1, Components(elements));
            output.WriteLine($"После перестройки: КЭ {elements.Count}, областей перестроено {meshed.RebuiltRegionCount}");

            // Узлы плиты под стеной и колонной остались импортными без импортных КЭ: записанная сетка — ровно собранная, и
            // проверка перед расчётом видит её актуальной (не пересобирает: «Мирная», 17 334 → 17 313 узлов).
            Assert.Equal(meshed.Mesh!.Nodes.Count, db.GetFemMeshNodes(fragment.Id).Count);
            var recheck = await service.CheckAsync(fragment.Id, db.GetFemNodes(fragment.Id), db.GetFemMembers(fragment.Id), 1.0,
                CancellationToken.None);
            Assert.True(recheck.IsCurrent, string.Join("\n", recheck.Diagnostics.Select(d => d.Message)));
        });
    }

    /// <summary>
    /// «Мирная»: снят замок с плиты, на ней сохранённое ребро B1. Сборка мельче (0,5 м) — узлы плиты внутри КЭ ребра, шаг 1
    /// лестницы снимает и его. Узлы прежней плиты под стеной и колонной остаются импортными без импортных КЭ: следы АЖТ не
    /// обрезаются от снятого ребра, записанная сетка — ровно собранная и актуальна для проверки перед расчётом.
    /// </summary>
    [Fact]
    public async Task ManualRemesh_KeptSlabWithKeptRib_LadderUnlocksRib_MeshCurrent()
    {
        await InTempRoot("opencs-fragment-remesh-rib", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var parent = CreateParent(db, rib: true);
            await Mesh(db, parent, 1.0, root);
            var fragment = Assert.IsType<FemSchema>(StoreyFragmentService.Build(db, parent.Id, new StoreyFragmentRequest(Z1, Z2, "Этаж 2")).Schema);
            await Mesh(db, fragment, 1.0, root);
            Assert.Contains(db.GetFemMeshElements(fragment.Id), e => e.SourceMemberTag == "B1" && e.Origin == FemMember.MeshSourceImported);

            var service = new FemSchemaMeshService(db, GmshSettings(root));
            service.UnlockKeptMembers(fragment.Id, db.GetFemNodes(fragment.Id), db.GetFemMembers(fragment.Id), ["S1"]);
            fragment.MeshPlateStepM = fragment.MeshBarStepM = 0.5;
            db.UpdateFemSchemaMeshSteps(fragment);
            var meshed = await service.BuildAsync(fragment.Id, db.GetFemNodes(fragment.Id), db.GetFemMembers(fragment.Id), 0.5, null,
                CancellationToken.None);
            foreach (var d in meshed.Diagnostics) output.WriteLine($"{(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
            Assert.False(meshed.HasErrors);
            Assert.Equal(1, meshed.LadderStep);
            Assert.Contains("B1", meshed.Unlock!.UnlockedMemberTags);
            Assert.DoesNotContain(meshed.Diagnostics, d => d.Code.StartsWith("rigid_junction_trace_kept", StringComparison.Ordinal));
            service.Save(fragment.Id, meshed.Mesh!, meshed.Unlock);

            Assert.DoesNotContain(db.GetFemMeshElements(fragment.Id), e => e.Origin == FemMember.MeshSourceImported);
            Assert.Equal(meshed.Mesh!.Nodes.Count, db.GetFemMeshNodes(fragment.Id).Count);
            var check = await service.CheckAsync(fragment.Id, db.GetFemNodes(fragment.Id), db.GetFemMembers(fragment.Id), 0.5,
                CancellationToken.None);
            Assert.True(check.IsCurrent, string.Join("\n", check.Diagnostics.Select(d => d.Message)));
        });
    }

    /// <summary>
    /// Стена на всю высоту сохранена, плита обрезана гранью призмы (y = 2) и перестроена, колонна в списке
    /// перемешивания: авто-АЖТ фрагмента строятся у перестраиваемой плиты, хотя у схемы есть сохранённая сетка.
    /// </summary>
    [Fact]
    public async Task KeptWall_RebuiltSlabAndColumn_AutoJunctionBuilt()
    {
        await InTempRoot("opencs-fragment-keep-rigid", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var parent = CreateParent(db);
            await Mesh(db, parent, 1.0, root);

            var prism = FragmentPrism.Create(3, 4.5, 1, 0, 4, 2.5);
            var built = StoreyFragmentService.Build(db, parent.Id,
                new StoreyFragmentRequest(-1, 7, "Угол", Prism: prism, KeepParentMesh: true, MeshRebuildMemberTags: ["C1a", "C1b"]));
            foreach (var d in built.Draft.Diagnostics) output.WriteLine($"{(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
            var fragment = Assert.IsType<FemSchema>(built.Schema);
            Assert.True(db.HasImportedMesh(fragment.Id));

            fragment.MeshPlateStepM = fragment.MeshBarStepM = 0.5;
            db.UpdateFemSchemaMeshSteps(fragment);
            var service = new FemSchemaMeshService(db, GmshSettings(root));
            var meshed = await service.BuildAsync(fragment.Id, db.GetFemNodes(fragment.Id), db.GetFemMembers(fragment.Id), 0.5, null,
                CancellationToken.None);
            foreach (var d in meshed.Diagnostics) output.WriteLine($"сетка {(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
            Assert.False(meshed.HasErrors);
            service.Save(fragment.Id, meshed.Mesh!);
            var nodes = db.GetFemMeshNodes(fragment.Id).ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
            var elements = db.GetFemMeshElements(fragment.Id);
            Assert.Contains(elements, e => e.SourceMemberTag!.StartsWith("W1") && e.Origin == FemMember.MeshSourceImported);
            Assert.Equal(1, Components(elements));

            var body = Assert.Single(db.GetFemRigidBodies(fragment.Id), b => b.Origin == RigidJunctionTraceSet.Provenance);
            var master = nodes[body.MasterNodeTag];
            Assert.True(Math.Abs(master.X - 3) < Tol && Math.Abs(master.Y - 3) < Tol && Math.Abs(master.Z - 3) < Tol);
            Assert.NotEmpty(body.SlaveNodeTags);
            Assert.All(body.SlaveNodeTags, t => Assert.True(Math.Abs(nodes[t].Z - 3) < Tol));
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
