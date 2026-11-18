using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.Sp63Punching;
using CScore.Sp63Punching.Fem;
using CSfea.CScoreBridge.Structural;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Продавливание по МКЭ-схеме CSfea (срез 3): плита 6×6 м толщиной 200 мм на z = 3 (сетка 0,25 м) на колонне
/// 0,4×0,4 м, низ колонны заделан; «Пост.» — −10 кПа на плиту. Сила по разрезу основания пирамиды (узловые силы КЭ)
/// обязана совпасть с силой колонны минус нагрузка основания: это одно и то же равновесие.
/// </summary>
public sealed class PunchingFemServiceTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-punching-fem-{Guid.NewGuid():N}.db");
    const double Q = -10e3, Step = 0.25, Half = 3.0, Zs = 3.0;
    const int N = 24;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    static string S(int v) => v.ToString(CultureInfo.InvariantCulture);
    static int SlabNode(int i, int j) => 10000 + j * (N + 1) + i;

    sealed record Setup(DatabaseService Db, FemSchema Schema, FemAnalysis Analysis);

    /// <summary>
    /// Схема с колонной в узле сетки (ci, cj); <paramref name="above"/> — колонна продолжается вверх до z = 6;
    /// <paramref name="c1"/> — коэффициент постели плиты, Н/м³ (0 — основания нет).
    /// </summary>
    Setup Create(int ci = N / 2, int cj = N / 2, bool above = false, double windX = 0, double c1 = 0)
    {
        var db = new DatabaseService(_path);
        var schema = new FemSchema { Tag = "Плита" };
        db.SaveFemSchema(schema);
        var frame = new Frame3D(new PlanarVector3(0, 0, Zs), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var region = PlanarRegion.CreateFromContour(new Contour([-Half, Half, Half, -Half, -Half], [-Half, -Half, Half, Half, -Half], "S1"), [], frame, "S1");
        region.MeshMaxElementSizeM = Step;
        db.AddPlanarRegion(region, schema.Id);

        var concrete = MaterialCatalog.CreateHeavyConcrete("B25", CatalogDirectory())!;
        var rebar = MaterialCatalog.CreateRebar("А500С", CatalogDirectory())!;
        db.AddMaterial(concrete);
        db.AddMaterial(rebar);
        var plate = new PlateSection
        {
            Tag = "П200", H = 0.2, ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
            RebarLayers =
            [
                new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = -0.07, Zsy = -0.07, Face = RebarFace.MinusN },
                new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = 0.07, Zsy = 0.07, Face = RebarFace.PlusN },
            ],
        };
        db.SavePlateSection(plate);
        var column = new CrossSection
        {
            Id = 9001, Tag = "К400",
            Areas =
            [
                new MaterialArea
                {
                    Tag = "concrete", Category = AreaCategory.Region, MaterialId = concrete.Id,
                    Hull = new Contour(new[] { -0.2, 0.2, 0.2, -0.2, -0.2 }, new[] { -0.2, -0.2, 0.2, 0.2, -0.2 }, "outer"),
                },
            ],
        };

        double cx = -Half + Step * ci, cy = -Half + Step * cj;
        var nodes = new List<FemNode>
        {
            new() { Id = -1, NodeTag = "1", X = cx, Y = cy, Z = 0, DofMask = 63 },
            new() { Id = -2, NodeTag = "2", X = cx, Y = cy, Z = Zs },
        };
        var members = new List<FemMember>
        {
            new() { Id = -1, ElemTag = "C1", ElemType = "beam", NodeIdsJson = "[1,2]", CrossSectionId = column.Id },
            new()
            {
                Id = -10, ElemTag = "S1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = region.Id, Kind = "plate",
                ThicknessM = 0.2, PlateSectionId = plate.Id,
            },
        };
        if (above)
        {
            nodes.Add(new FemNode { Id = -3, NodeTag = "3", X = cx, Y = cy, Z = 6 });
            members.Add(new FemMember { Id = -2, ElemTag = "C2", ElemType = "beam", NodeIdsJson = "[2,3]", CrossSectionId = column.Id });
        }
        var loadCases = new List<FemLoadCase> { new() { Id = -1, Tag = "Пост." }, new() { Id = -2, Tag = "Ветер" } };
        var load = new FemElementLoad { LoadCaseId = -1, LoadKind = FemElementLoadKinds.Uniform, Axis = "z", CoordinateSystem = "global" };
        load.SetTargetTags(Enumerable.Range(1, N * N).Select(S).ToArray());
        load.SetValues([Q]);
        var meshNodeLoads = new List<FemMeshNodeLoad>();
        if (windX != 0) meshNodeLoads.Add(new() { LoadCaseId = -2, MeshNodeTag = above ? "203" : S(SlabNode(ci, cj)), Fx = windX });
        else meshNodeLoads.Add(new() { LoadCaseId = -2, MeshNodeTag = S(SlabNode(0, 0)), Fz = -1e3 });
        db.SaveFemSchemaEdit(schema.Id, nodes, members, [], loadCases, [], [], [], null, [load], meshNodeLoads);

        var meshNodes = new List<FemMeshNode>();
        var meshElements = new List<FemElement>();
        for (int j = 0; j <= N; j++)
            for (int i = 0; i <= N; i++)
                meshNodes.Add(new FemMeshNode
                {
                    NodeTag = S(SlabNode(i, j)), X = -Half + Step * i, Y = -Half + Step * j, Z = Zs,
                    SourceNodeTag = i == ci && j == cj ? "2" : null,
                });
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
                meshElements.Add(new FemElement
                {
                    ElemTag = S(1 + j * N + i), ElemType = "shell", SourceMemberTag = "S1", ThicknessM = 0.2,
                    NodeIdsJson = $"[{SlabNode(i, j)},{SlabNode(i + 1, j)},{SlabNode(i, j + 1)},{SlabNode(i + 1, j + 1)}]",
                    FoundationC1 = c1 > 0 ? c1 : null,
                });
        string top = S(SlabNode(ci, cj));
        string Col(int k) => k == 3 ? top : S(100 + k);
        for (int k = 0; k < 3; k++)
            meshNodes.Add(new FemMeshNode { NodeTag = S(100 + k), X = cx, Y = cy, Z = k, SourceNodeTag = k == 0 ? "1" : null });
        for (int k = 0; k < 3; k++)
            meshElements.Add(new FemElement { ElemTag = S(5000 + k), ElemType = "beam", SourceMemberTag = "C1", NodeIdsJson = $"[{Col(k)},{Col(k + 1)}]" });
        if (above)
        {
            string Up(int k) => k == 3 ? top : S(200 + k);
            for (int k = 4; k <= 6; k++)
                meshNodes.Add(new FemMeshNode { NodeTag = S(200 + k), X = cx, Y = cy, Z = k, SourceNodeTag = k == 6 ? "3" : null });
            for (int k = 3; k < 6; k++)
                meshElements.Add(new FemElement { ElemTag = S(6000 + k), ElemType = "beam", SourceMemberTag = "C2", NodeIdsJson = $"[{Up(k)},{Up(k + 1)}]" });
        }
        db.SaveFemMeshSnapshot(schema.Id, meshNodes, meshElements);
        db.LoadAll();
        var parent = db.FemSchemas.Single(s => s.Id == schema.Id);
        db.CrossSections.Add(column);
        var lc = db.GetFemLoadCases(parent.Id);
        var stages = lc.Select(c => new FemAnalysisStage
        {
            Tag = c.Tag, LoadFactorStep = 1, MaxLoadFactor = 1,
            LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [c.Id] }.ToJson(),
        }).ToList();
        return new Setup(db, parent, SaveLinear(db, parent, stages));
    }

    /// <summary>
    /// Та же плита на пилоне 1,0 × 0,3 м из пластин (стенка W1 в плоскости y = 0 от x = −0,5 до 0,5, z 0…3, сетка 0,25),
    /// низ стенки заделан; «Пост.» — −10 кПа на плиту.
    /// </summary>
    Setup CreatePylon()
    {
        var db = new DatabaseService(_path);
        var schema = new FemSchema { Tag = "Плита на пилоне" };
        db.SaveFemSchema(schema);
        var slabFrame = new Frame3D(new PlanarVector3(0, 0, Zs), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var slab = PlanarRegion.CreateFromContour(new Contour([-Half, Half, Half, -Half, -Half], [-Half, -Half, Half, Half, -Half], "S1"), [], slabFrame, "S1");
        slab.MeshMaxElementSizeM = Step;
        db.AddPlanarRegion(slab, schema.Id);
        var wallFrame = new Frame3D(new PlanarVector3(-0.5, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0));
        var wallRegion = PlanarRegion.CreateFromContour(new Contour([0.0, 1, 1, 0, 0], [0.0, 0, Zs, Zs, 0], "W1"), [], wallFrame, "W1");
        wallRegion.MeshMaxElementSizeM = Step;
        db.AddPlanarRegion(wallRegion, schema.Id);

        var concrete = MaterialCatalog.CreateHeavyConcrete("B25", CatalogDirectory())!;
        var rebar = MaterialCatalog.CreateRebar("А500С", CatalogDirectory())!;
        db.AddMaterial(concrete);
        db.AddMaterial(rebar);
        PlateSection Plate(string tag, double h) => new()
        {
            Tag = tag, H = h, ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
            RebarLayers =
            [
                new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = -h / 2 + 0.03, Zsy = -h / 2 + 0.03, Face = RebarFace.MinusN },
                new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = h / 2 - 0.03, Zsy = h / 2 - 0.03, Face = RebarFace.PlusN },
            ],
        };
        var p200 = Plate("П200", 0.2);
        var p300 = Plate("С300", 0.3);
        db.SavePlateSection(p200);
        db.SavePlateSection(p300);

        var nodes = new List<FemNode>();
        for (int i = 0; i <= 4; i++)
            nodes.Add(new FemNode { Id = -1 - i, NodeTag = S(1 + i), X = -0.5 + 0.25 * i, Y = 0, Z = 0, DofMask = 63 });
        var members = new List<FemMember>
        {
            new() { Id = -10, ElemTag = "S1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = slab.Id, Kind = "plate", ThicknessM = 0.2, PlateSectionId = p200.Id },
            new() { Id = -11, ElemTag = "W1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = wallRegion.Id, Kind = "wall", ThicknessM = 0.3, PlateSectionId = p300.Id },
        };
        var loadCases = new List<FemLoadCase> { new() { Id = -1, Tag = "Пост." } };
        var load = new FemElementLoad { LoadCaseId = -1, LoadKind = FemElementLoadKinds.Uniform, Axis = "z", CoordinateSystem = "global" };
        load.SetTargetTags(Enumerable.Range(1, N * N).Select(S).ToArray());
        load.SetValues([Q]);
        db.SaveFemSchemaEdit(schema.Id, nodes, members, [], loadCases, [], [], [], null, [load], []);

        var meshNodes = new List<FemMeshNode>();
        var meshElements = new List<FemElement>();
        for (int j = 0; j <= N; j++)
            for (int i = 0; i <= N; i++)
                meshNodes.Add(new FemMeshNode { NodeTag = S(SlabNode(i, j)), X = -Half + Step * i, Y = -Half + Step * j, Z = Zs });
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
                meshElements.Add(new FemElement
                {
                    ElemTag = S(1 + j * N + i), ElemType = "shell", SourceMemberTag = "S1", ThicknessM = 0.2,
                    NodeIdsJson = $"[{SlabNode(i, j)},{SlabNode(i + 1, j)},{SlabNode(i, j + 1)},{SlabNode(i + 1, j + 1)}]",
                });
        // Стенка: 4 × 12 квадратов; верхний ряд узлов — узлы плиты на y = 0.
        string W(int i, int k) => k == 12 ? S(SlabNode(N / 2 - 2 + i, N / 2)) : S(20000 + k * 5 + i);
        for (int k = 0; k < 12; k++)
            for (int i = 0; i <= 4; i++)
                meshNodes.Add(new FemMeshNode { NodeTag = W(i, k), X = -0.5 + 0.25 * i, Y = 0, Z = 0.25 * k, SourceNodeTag = k == 0 ? S(1 + i) : null });
        for (int k = 0; k < 12; k++)
            for (int i = 0; i < 4; i++)
                meshElements.Add(new FemElement
                {
                    ElemTag = S(7000 + k * 4 + i), ElemType = "shell", SourceMemberTag = "W1", ThicknessM = 0.3,
                    NodeIdsJson = $"[{W(i, k)},{W(i + 1, k)},{W(i, k + 1)},{W(i + 1, k + 1)}]",
                });
        db.SaveFemMeshSnapshot(schema.Id, meshNodes, meshElements);
        db.LoadAll();
        var parent = db.FemSchemas.Single(s => s.Id == schema.Id);
        var lc = db.GetFemLoadCases(parent.Id);
        var stages = lc.Select(c => new FemAnalysisStage
        {
            Tag = c.Tag, LoadFactorStep = 1, MaxLoadFactor = 1,
            LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [c.Id] }.ToJson(),
        }).ToList();
        return new Setup(db, parent, SaveLinear(db, parent, stages));
    }

    [Fact]
    public void PylonShells_ContourEqualsWallActionMinusBase_EndsChecked()
    {
        var s = CreatePylon();
        using var db = s.Db;
        var (_, source, joint, forces) = Evaluate(s);
        Assert.Equal(PunchingFemJointKind.PylonShells, joint.Kind);
        var r = Assert.IsType<PunchingLoadArea.Rectangle>(joint.Area);
        Assert.Equal(1.0, r.Bx, 9);
        Assert.Equal(0.3, r.By, 9);
        var f = forces.ByKey[source.Keys.Single().Key];
        Assert.Equal(-Q * 36 / 1e3, f.ColumnAction!.Value, 2);
        Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value, 2);
        var (minus, plus, middle) = f.PylonParts!.Value;
        Assert.Equal(f.ContourForce!.Value, minus + plus + middle, 6);
        Assert.Equal(minus, plus, 2);   // симметрия

        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id,
            new PunchingFemCheckParams { SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture) }, CalcType.C);
        var j = Assert.Single(data.Joints);
        Assert.True(j.NoCheck == null, j.NoCheckText);
        var row = Assert.Single(j.Rows);
        Assert.NotNull(row.PylonEndRatio);
        Assert.True(row.Ratio >= row.PylonEndRatio!.Value - 1e-12);
    }

    /// <summary>
    /// Та же плита на стене 2,0 × 0,3 м из пластин (W1 в плоскости y = 0 от x = −1 до 1, z 0…3, сетка 0,25), у Г — ещё стена
    /// W2 в плоскости x = −1 от y = 0 до 2; низ стен заделан. «Пост.» — −10 кПа на всю плиту или (<paramref name="halfLoad"/>)
    /// только на половину y &gt; 0.
    /// </summary>
    Setup CreateWalls(bool lShape = false, bool halfLoad = false)
    {
        var db = new DatabaseService(_path);
        var schema = new FemSchema { Tag = "Плита на стенах" };
        db.SaveFemSchema(schema);
        var slabFrame = new Frame3D(new PlanarVector3(0, 0, Zs), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var slab = PlanarRegion.CreateFromContour(new Contour([-Half, Half, Half, -Half, -Half], [-Half, -Half, Half, Half, -Half], "S1"), [], slabFrame, "S1");
        slab.MeshMaxElementSizeM = Step;
        db.AddPlanarRegion(slab, schema.Id);
        var w1Frame = new Frame3D(new PlanarVector3(-1, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0));
        var w1 = PlanarRegion.CreateFromContour(new Contour([0.0, 2, 2, 0, 0], [0.0, 0, Zs, Zs, 0], "W1"), [], w1Frame, "W1");
        w1.MeshMaxElementSizeM = Step;
        db.AddPlanarRegion(w1, schema.Id);
        PlanarRegion? w2 = null;
        if (lShape)
        {
            var w2Frame = new Frame3D(new PlanarVector3(-1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0));
            w2 = PlanarRegion.CreateFromContour(new Contour([0.0, 2, 2, 0, 0], [0.0, 0, Zs, Zs, 0], "W2"), [], w2Frame, "W2");
            w2.MeshMaxElementSizeM = Step;
            db.AddPlanarRegion(w2, schema.Id);
        }

        var concrete = MaterialCatalog.CreateHeavyConcrete("B25", CatalogDirectory())!;
        var rebar = MaterialCatalog.CreateRebar("А500С", CatalogDirectory())!;
        db.AddMaterial(concrete);
        db.AddMaterial(rebar);
        PlateSection Plate(string tag, double h) => new()
        {
            Tag = tag, H = h, ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
            RebarLayers =
            [
                new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = -h / 2 + 0.03, Zsy = -h / 2 + 0.03, Face = RebarFace.MinusN },
                new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = h / 2 - 0.03, Zsy = h / 2 - 0.03, Face = RebarFace.PlusN },
            ],
        };
        var p200 = Plate("П200", 0.2);
        var p300 = Plate("С300", 0.3);
        db.SavePlateSection(p200);
        db.SavePlateSection(p300);

        var nodes = new List<FemNode>();
        for (int i = 0; i <= 8; i++)
            nodes.Add(new FemNode { Id = -1 - i, NodeTag = S(1 + i), X = -1 + 0.25 * i, Y = 0, Z = 0, DofMask = 63 });
        if (lShape)
            for (int j = 1; j <= 8; j++)
                nodes.Add(new FemNode { Id = -20 - j, NodeTag = S(20 + j), X = -1, Y = 0.25 * j, Z = 0, DofMask = 63 });
        var members = new List<FemMember>
        {
            new() { Id = -10, ElemTag = "S1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = slab.Id, Kind = "plate", ThicknessM = 0.2, PlateSectionId = p200.Id },
            new() { Id = -11, ElemTag = "W1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = w1.Id, Kind = "wall", ThicknessM = 0.3, PlateSectionId = p300.Id },
        };
        if (w2 != null)
            members.Add(new() { Id = -12, ElemTag = "W2", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = w2.Id, Kind = "wall", ThicknessM = 0.3, PlateSectionId = p300.Id });
        var loadCases = new List<FemLoadCase> { new() { Id = -1, Tag = "Пост." } };
        var load = new FemElementLoad { LoadCaseId = -1, LoadKind = FemElementLoadKinds.Uniform, Axis = "z", CoordinateSystem = "global" };
        load.SetTargetTags(Enumerable.Range(0, N * N).Where(e => !halfLoad || e / N >= N / 2).Select(e => S(1 + e)).ToArray());
        load.SetValues([Q]);
        db.SaveFemSchemaEdit(schema.Id, nodes, members, [], loadCases, [], [], [], null, [load], []);

        var meshNodes = new List<FemMeshNode>();
        var meshElements = new List<FemElement>();
        for (int j = 0; j <= N; j++)
            for (int i = 0; i <= N; i++)
                meshNodes.Add(new FemMeshNode { NodeTag = S(SlabNode(i, j)), X = -Half + Step * i, Y = -Half + Step * j, Z = Zs });
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
                meshElements.Add(new FemElement
                {
                    ElemTag = S(1 + j * N + i), ElemType = "shell", SourceMemberTag = "S1", ThicknessM = 0.2,
                    NodeIdsJson = $"[{SlabNode(i, j)},{SlabNode(i + 1, j)},{SlabNode(i, j + 1)},{SlabNode(i + 1, j + 1)}]",
                });
        // W1: 8 × 12 квадратов; верхний ряд узлов — узлы плиты на y = 0 (i = 8 … 16).
        string W1(int i, int k) => k == 12 ? S(SlabNode(N / 2 - 4 + i, N / 2)) : S(20000 + k * 9 + i);
        for (int k = 0; k < 12; k++)
            for (int i = 0; i <= 8; i++)
                meshNodes.Add(new FemMeshNode { NodeTag = W1(i, k), X = -1 + 0.25 * i, Y = 0, Z = 0.25 * k, SourceNodeTag = k == 0 ? S(1 + i) : null });
        for (int k = 0; k < 12; k++)
            for (int i = 0; i < 8; i++)
                meshElements.Add(new FemElement
                {
                    ElemTag = S(7000 + k * 8 + i), ElemType = "shell", SourceMemberTag = "W1", ThicknessM = 0.3,
                    NodeIdsJson = $"[{W1(i, k)},{W1(i + 1, k)},{W1(i, k + 1)},{W1(i + 1, k + 1)}]",
                });
        if (lShape)
        {
            // W2: в плоскости x = −1, y 0 … 2; узлы y = 0 — общие со стеной W1.
            string W2(int j, int k) => j == 0 ? W1(0, k) : k == 12 ? S(SlabNode(N / 2 - 4, N / 2 + j)) : S(30000 + k * 9 + j);
            for (int k = 0; k < 12; k++)
                for (int j = 1; j <= 8; j++)
                    meshNodes.Add(new FemMeshNode { NodeTag = W2(j, k), X = -1, Y = 0.25 * j, Z = 0.25 * k, SourceNodeTag = k == 0 ? S(20 + j) : null });
            for (int k = 0; k < 12; k++)
                for (int j = 0; j < 8; j++)
                    meshElements.Add(new FemElement
                    {
                        ElemTag = S(8000 + k * 8 + j), ElemType = "shell", SourceMemberTag = "W2", ThicknessM = 0.3,
                        NodeIdsJson = $"[{W2(j, k)},{W2(j + 1, k)},{W2(j, k + 1)},{W2(j + 1, k + 1)}]",
                    });
        }
        db.SaveFemMeshSnapshot(schema.Id, meshNodes, meshElements);
        db.LoadAll();
        var parent = db.FemSchemas.Single(s => s.Id == schema.Id);
        var lc = db.GetFemLoadCases(parent.Id);
        var stages = lc.Select(c => new FemAnalysisStage
        {
            Tag = c.Tag, LoadFactorStep = 1, MaxLoadFactor = 1,
            LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [c.Id] }.ToJson(),
        }).ToList();
        return new Setup(db, parent, SaveLinear(db, parent, stages));
    }

    /// <summary>Узлы стен и их усилия по единственному ключу.</summary>
    List<(PunchingFemJoint Joint, PunchingFemForces Forces)> EvaluateWalls(Setup s)
    {
        var data = PunchingFemService.Load(s.Db, s.Schema, new PunchingFemOptions());
        var diag = new List<FemValidationDiagnostic>();
        var source = PunchingFemService.OpenCsfea(s.Db, s.Schema, s.Analysis.Id, diag);
        Assert.True(source?.Nodal != null, string.Join("; ", diag.Select(d => d.Message)));
        var list = new List<(PunchingFemJoint, PunchingFemForces)>();
        foreach (var joint in data.Joints)
        {
            Assert.True(joint.NoCheck == null, joint.NoCheckText);
            var forces = PunchingFemService.Evaluate(data, source!, joint);
            Assert.True(forces.CutFailure == null, forces.CutFailure);
            list.Add((joint, forces.ByKey[source!.Keys.Single().Key]));
        }
        return list;
    }

    /// <summary>Новая проверка получает свободное имя: «Продавливание», затем «Продавливание 2», «… 3».</summary>
    [Fact]
    public void NewCheck_UniqueDefaultTag()
    {
        Assert.Equal("Продавливание", OpenCS.ViewModels.PunchingFemCheckVM.UniqueTag("Продавливание", ["Сталь", null]));
        Assert.Equal("Продавливание 2", OpenCS.ViewModels.PunchingFemCheckVM.UniqueTag("Продавливание", ["продавливание"]));
        Assert.Equal("Продавливание 3", OpenCS.ViewModels.PunchingFemCheckVM.UniqueTag("Продавливание", ["Продавливание", "Продавливание 2"]));
    }

    [Fact]
    public void WallEnds_Symmetric_EquilibriumAndZeroDeltaM()
    {
        var s = CreateWalls();
        using var db = s.Db;
        var ends = EvaluateWalls(s);
        Assert.Equal(2, ends.Count);
        Assert.All(ends, e => Assert.Equal(PunchingFemJointKind.WallEnd, e.Joint.Kind));
        foreach (var (_, f) in ends)
        {
            Assert.Equal(PunchingFemForceMethod.Contour, f.Method);
            Assert.True(f.F > 0 && f.F < 360, $"F = {f.F}");
            // Равновесие тела основания: действие стены в основании + нагрузка = сила по контуру + силы на гранях поперёк стены.
            Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value + f.WallExcluded!.Value, 3);
            // Симметрия относительно оси стены: моменты плиты по сторонам равны.
            Assert.True(Math.Abs(f.Mx) < 1e-3 * f.F, $"ΔM = {f.Mx}");
            // Самопроверка ΔM: оба способа на П-контуре h0/2 дают ≈ 0.
            var c = f.WallDeltaMCheck!.Value;
            Assert.True(Math.Abs(c.Plate) < 0.01 * c.Scale + 1e-6 && Math.Abs(c.Nodal) < 0.01 * c.Scale + 1e-6,
                $"ΔM пластины {c.Plate}, узловые {c.Nodal}, Σ|m| {c.Scale}");
        }
        Assert.True(Math.Abs(ends[0].Forces.F - ends[1].Forces.F) < 1e-4 * ends[0].Forces.F);   // симметрия торцов

        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id,
            new PunchingFemCheckParams { SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture) }, CalcType.C);
        Assert.Equal(2, data.Joints.Count);
        Assert.All(data.Joints, j => Assert.True(j.NoCheck == null, j.NoCheckText));
        Assert.All(data.Joints, j => Assert.Equal("wall_end", j.Kind));
        var row = data.Joints[0].Rows.Single();
        var tp = PunchingFemCheckRunner.RowParams(data.Joints[0], row)!;
        Assert.Equal("end", tp.Wall.Kind);
        Assert.Equal(0.3, tp.Wall.A1, 9);
        Assert.StartsWith("wall_end", row.Governing, StringComparison.Ordinal);
        Assert.Empty(tp.Validate());
    }

    [Fact]
    public void WallEnds_LoadOnOneSide_DeltaMFromSlabMoments()
    {
        var s = CreateWalls(halfLoad: true);
        using var db = s.Db;
        var ends = EvaluateWalls(s);
        Assert.Equal(2, ends.Count);
        foreach (var (joint, f) in ends)
        {
            Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value + f.WallExcluded!.Value, 3);
            // Нагружена сторона общей +Y: опорный момент плиты у стены с этой стороны (растяжение верха, M > 0 в конвенции
            // OpenCS) больше, чем с ненагруженной. ΔM = M(+y) − M(−y) в осях узла: у торца x = −1 ось y узла смотрит в −Y.
            Assert.True(f.Mx * Math.Sign(joint.AxisY.Y) > 0.05 * f.F * 0.3, $"ΔM = {f.Mx}, F = {f.F}");
            // Самопроверка ΔM (спека среза 5, 4.3): на П-контуре h0/2 моменты по пластинам в центрах КЭ и по узловым силам
            // разреза одного знака и в пределах допуска окна (10 %).
            var c = f.WallDeltaMCheck!.Value;
            Assert.True(Math.Sign(c.Plate) == Math.Sign(c.Nodal) && Math.Abs(c.Nodal) > 0.01 * c.Scale,
                $"ΔM пластины {c.Plate}, узловые {c.Nodal}, Σ|m| {c.Scale}");
            Assert.True(Math.Abs(c.Plate - c.Nodal) <= 0.10 * Math.Max(Math.Max(Math.Abs(c.Plate), Math.Abs(c.Nodal)), 0.1 * c.Scale) + 0.5,
                $"ΔM пластины {c.Plate}, узловые {c.Nodal}, Σ|m| {c.Scale}");
        }
        Assert.Equal(Math.Abs(ends[0].Forces.Mx), Math.Abs(ends[1].Forces.Mx), 2);
        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id,
            new PunchingFemCheckParams { SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture) }, CalcType.C);
        Assert.All(data.Joints, j => Assert.DoesNotContain(j.Warnings, w => w.Code == "punching_fem_wall_dm_mismatch"));
    }

    [Fact]
    public void LWall_CornerAndEnds_Equilibrium()
    {
        var s = CreateWalls(lShape: true);
        using var db = s.Db;
        var joints = EvaluateWalls(s);
        Assert.Equal(3, joints.Count);
        var (corner, cf) = joints.Single(j => j.Joint.Kind == PunchingFemJointKind.WallCorner);
        Assert.Equal(0.3, corner.Wall!.A1, 9);
        Assert.Equal(0.3, corner.Wall.T2, 9);
        Assert.Equal(0, cf.Mx, 12);                  // у угла моментов нет
        Assert.True(cf.F > 0);
        foreach (var (_, f) in joints)
            Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value + f.WallExcluded!.Value, 3);

        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id,
            new PunchingFemCheckParams { SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture) }, CalcType.C);
        // План: кусок стены у угла — Г из 6 точек (не выпуклая оболочка).
        var cj = data.Joints.Single(j => j.Kind == "wall_corner");
        Assert.Equal(6, cj.Area.Count);
        // План: стены W1 и W2 — линиями по всей длине (2 м), толщина 0,3 м.
        Assert.Equal(2, data.Walls.Count);
        Assert.All(data.Walls, w => Assert.Equal(2, Math.Sqrt(Math.Pow(w.B[0] - w.A[0], 2) + Math.Pow(w.B[1] - w.A[1], 2)), 6));
        Assert.All(data.Walls, w => Assert.Equal(0.3, w.T, 9));
        Assert.All(data.Joints, j => Assert.All(j.Rows, r => Assert.NotNull(r.WallExcluded)));

        // Окно результата: у стен «Стена в основании», «Грани поперёк стены» (= WallExcluded), без торцов пилона, неучтённые моменты — «—».
        var result = new CalcResult { TaskKind = PunchingFemCheckParams.NormCode, Status = "ok", DataJson = data.ToJson() };
        var vm = new OpenCS.ViewModels.PunchingFemResultVM(result, null, db.Materials);
        vm.SelectNode(cj.Node);
        Assert.True(vm.IsWallJoint);
        Assert.Equal(Loc.S("PunchingFemColWallBase", "Стена в основании, кН"), vm.RowsColumnHeader);
        Assert.Equal(Loc.S("PunchingFemColWallFaces", "Грани поперёк стены, кН"), vm.RowsMismatchHeader);
        var row = vm.Rows.First();
        Assert.Equal("—", row.Mx);
        Assert.Equal("—", row.My);
        Assert.Equal(row.Row.WallExcluded!.Value.ToString("0.0", CultureInfo.CurrentCulture), row.Mismatch);
        var ej = data.Joints.First(j => j.Kind == "wall_end");
        vm.SelectNode(ej.Node);
        Assert.Equal("ΔM", vm.RowsMxHeader);
        Assert.Equal("—", vm.Rows.First().My);
        Assert.NotEqual("—", vm.Rows.First().Mx);
    }

    static FemAnalysis SaveLinear(DatabaseService db, FemSchema schema, IReadOnlyList<FemAnalysisStage> stages)
    {
        var analysis = new FemAnalysis { SchemaId = schema.Id, Tag = "Линейный", Kind = FemCsfeaRunner.LinearAnalysisKind };
        analysis.ParamsJson = new FemAnalysisParams { Stages = [.. stages] }.ToJson();
        var input = FemCsfeaInputBuilder.Build(db, schema.Id, new FemCsfeaSetup { Stages = stages, GrossBars = true });
        Assert.DoesNotContain(input.Diagnostics, d => d.IsError);
        var adapted = FemRcModelAdapter.Adapt(input);
        Assert.False(adapted.HasErrors, string.Join("; ", adapted.Report));
        var run = RcLinearAnalysis.Run(adapted.Model, new RcLinearOptions { ShellForceAngles = adapted.ShellForceAngles });
        var summary = new FemCsfeaResultSummary
        {
            Kind = analysis.Kind, Completed = true,
            Steps = run.Cases.Select((c, i) => new FemCsfeaStepSummary(i + 1, i, 1, 1.0, false, true, 1, 0, null, Fields: true)).ToList(),
        };
        var result = new CalcResult
        {
            TaskKind = FemCsfeaRunner.TaskKind, TaskTag = analysis.Tag, Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            Status = "ok", DataJson = summary.ToJson(),
        };
        db.SaveCalcResult(result);
        db.SaveFemResultSteps(result.Id, run.Cases.Select((c, i) => (i + 1, FemCsfeaStepFieldsCodec.Pack(c.Fields))));
        analysis.ResultId = result.Id;
        analysis.Status = "ok";
        db.SaveFemAnalysis(analysis);
        return analysis;
    }

    /// <summary>Каталог материалов: вверх от сборки, иначе — от исходника теста (сборка вне репозитория).</summary>
    static string CatalogDirectory([System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        dir ??= new FileInfo(source).Directory!.Parent!;
        return Path.Combine(dir.FullName, "OpenCS.App", "DataSource");
    }

    (PunchingFemSchemaData Data, PunchingFemSource Source, PunchingFemJoint Joint, PunchingFemNodeForces Forces) Evaluate(Setup s)
    {
        var data = PunchingFemService.Load(s.Db, s.Schema, new PunchingFemOptions());
        var joint = Assert.Single(data.Joints);
        Assert.True(joint.NoCheck == null, joint.NoCheckText);
        var diag = new List<FemValidationDiagnostic>();
        var source = PunchingFemService.OpenCsfea(s.Db, s.Schema, s.Analysis.Id, diag);
        Assert.True(source != null, string.Join("; ", diag.Select(d => d.Message)));
        Assert.True(source!.Nodal != null, source.NodalUnavailable);
        var forces = PunchingFemService.Evaluate(data, source, joint);
        Assert.True(forces.CutFailure == null, forces.CutFailure);
        return (data, source, joint, forces);
    }

    [Fact]
    public void InteriorColumn_ContourEqualsColumnMinusBaseLoad()
    {
        var s = Create();
        using var db = s.Db;
        var (_, source, joint, forces) = Evaluate(s);

        var r = Assert.IsType<PunchingLoadArea.Rectangle>(joint.Area);
        Assert.Equal(0.4, r.Bx, 9);
        Assert.Equal(0.2 - 0.03, joint.H0, 9);   // верхний слой: z = 0,07 → a = 0,03

        var key = source.Keys.Single(k => k.Label == "Пост.").Key;
        var f = forces.ByKey[key];
        double total = -Q * 2 * Half * 2 * Half / 1e3;            // 360 кН
        double baseArea = Math.Pow(0.4 + 2 * joint.H0, 2);
        Assert.Equal(total, f.ColumnAction!.Value, 3);
        Assert.Equal(Q / 1e3 * baseArea, f.BaseLoad!.Value, 6);
        Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value, 3);
        Assert.Equal(PunchingFemForceMethod.Contour, f.Method);
        Assert.Equal(PunchingLoadSide.FromBelow, f.Side);
        Assert.Equal(total + Q / 1e3 * baseArea, f.F, 3);
        Assert.True(Math.Abs(f.Mx) < 1e-3 && Math.Abs(f.My) < 1e-3, $"Mx = {f.Mx}, My = {f.My}");
    }

    [Fact]
    public void EccentricLoad_ColumnMomentAndContourAgree()
    {
        var s = Create();
        using var db = s.Db;
        var (_, source, joint, forces) = Evaluate(s);
        // «Ветер»: −1 кН в углу (−3, −3): сила 1 кН, момент на плиту от колонны = −(рычаг × сила груза).
        var f = forces.ByKey[source.Keys.Single(k => k.Label == "Ветер").Key];
        Assert.Equal(1.0, f.ColumnAction!.Value, 4);
        Assert.Equal(0.0, f.BaseLoad!.Value, 9);
        Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value, 4);
        // Груз −1 кН в точке (−3, −3) от колонны: момент груза (r × F) = (−3·(−1), 3·(−1)·… ) → колонна уравновешивает.
        Assert.Equal(-3.0, f.Mx, 3);
        Assert.Equal(3.0, f.My, 3);
    }

    [Fact]
    public void EdgeColumn_OpenContour_ContourEqualsColumnMinusBaseLoad()
    {
        var s = Create(ci: 1, cj: N / 2);   // ось колонны в 0,25 м от края: грань — в 0,05 м
        using var db = s.Db;
        var (_, source, joint, forces) = Evaluate(s);
        Assert.Single(joint.Edges);
        var f = forces.ByKey[source.Keys.Single(k => k.Label == "Пост.").Key];
        double baseArea = (0.25 + 0.2 + joint.H0) * (0.4 + 2 * joint.H0);
        Assert.Equal(Q / 1e3 * baseArea, f.BaseLoad!.Value, 6);
        Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value, 3);
    }

    /// <summary>
    /// Приложенный к схеме *.asp ЛИРЫ: узел с тем же номером получает результат и контур ЛИРЫ, запись без пары —
    /// предупреждение сводки; имя файла — в результате.
    /// </summary>
    [Fact]
    public void LiraAsp_AttachedToSchema_ResultAndContourInJoint()
    {
        var s = Create();
        using var db = s.Db;
        var joint = Assert.Single(PunchingFemService.Load(db, s.Schema, new PunchingFemOptions()).Joints);
        db.SaveFemSchemaSelectedReinforcementFile(s.Schema.Id, "plita.asp", LiraAsp(
            (int.Parse(joint.NodeTag, CultureInfo.InvariantCulture), joint.Origin, 6.08f, 1.6f),
            (999999, new PlanarVector3(5, 5, Zs), 0f, 2f)));

        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id,
            new PunchingFemCheckParams { SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture) }, CalcType.C);

        Assert.Equal("plita.asp", data.LiraAspFile);
        var lira = Assert.Single(data.Joints).Lira;
        Assert.NotNull(lira);
        Assert.Equal(6.08, lira!.Asw, 4);
        Assert.Equal(1.6, lira.Ksw, 4);
        Assert.Equal(4, lira.Contour.Count);
        Assert.Equal(4 * 0.74, lira.Perimeter, 4);
        Assert.Contains(data.Warnings, w => w.Code == "punching_fem_lira_unmatched" && w.Text.Contains("узел 999999"));

        // Окно результата и строка таблицы: столбец «ЛИРА» и подпись пунктира.
        var row = new ViewModels.PunchingFemJointRowVM(data.Joints[0]);
        Assert.False(string.IsNullOrEmpty(row.LiraText));
        Assert.Contains("6", row.LiraText);
    }

    /// <summary>
    /// Основной контур по ЛИРЕ: квадрат 0,74 м из *.asp вместо контура OpenCS 0,57 м (колонна 0,4 + h0 0,17); u OpenCS —
    /// справочно; переопределение узла «opencs» возвращает контур OpenCS; узел без *.asp — предупреждение.
    /// </summary>
    [Fact]
    public void LiraContour_ReplacesMainContour_NodeOverride()
    {
        var s = Create();
        using var db = s.Db;
        var joint = Assert.Single(PunchingFemService.Load(db, s.Schema, new PunchingFemOptions()).Joints);
        var p = new PunchingFemCheckParams { SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture), LiraContour = true };

        // Без *.asp — контуры OpenCS и предупреждение.
        var noAsp = PunchingFemCheckRunner.Compute(db, s.Schema.Id, p, CalcType.C);
        Assert.False(Assert.Single(noAsp.Joints).LiraContour);
        Assert.Contains(noAsp.Warnings, w => w.Code == "punching_fem_lira_contour_no_asp");

        db.SaveFemSchemaSelectedReinforcementFile(s.Schema.Id, "plita.asp", LiraAsp((int.Parse(joint.NodeTag, CultureInfo.InvariantCulture), joint.Origin, 0f, 2f)));
        var plain = Assert.Single(PunchingFemCheckRunner.Compute(db, s.Schema.Id, p with { LiraContour = false }, CalcType.C).Joints);
        var lira = Assert.Single(PunchingFemCheckRunner.Compute(db, s.Schema.Id, p, CalcType.C).Joints);
        Assert.False(plain.LiraContour);
        Assert.True(lira.LiraContour);
        Assert.Equal("custom_0", lira.Rows.Single(r => r.Key == lira.GoverningKey).Governing);
        Assert.Equal(4 * (0.4 + joint.H0), lira.UOpenCs!.Value, 3);
        // Без моментов Кисп обратно пропорционален u: 0,57/0,74.
        Assert.Equal(plain.Ratio * (0.4 + joint.H0) / 0.74, lira.Ratio, 2);
        // «Создать задачу» по строке — тот же контур ЛИРЫ.
        var rp = PunchingFemCheckRunner.RowParams(lira, lira.Rows[0])!;
        Assert.True(rp.Options.CustomReplacesMain);
        Assert.Equal("ЛИРА", Assert.Single(rp.CustomContours).Name);

        var over = p with { Overrides = [new PunchingFemJointOverride { Node = joint.NodeTag, Contour = "opencs" }] };
        Assert.False(Assert.Single(PunchingFemCheckRunner.Compute(db, s.Schema.Id, over, CalcType.C).Joints).LiraContour);

        // Карточка узла результата: u ЛИРЫ против u OpenCS.
        var card = new OpenCS.ViewModels.PunchingFemJointRowVM(lira).ContourText;
        Assert.Contains((lira.Lira!.Perimeter * 1000).ToString("0", CultureInfo.CurrentCulture), card);
        Assert.Contains((lira.UOpenCs!.Value * 1000).ToString("0", CultureInfo.CurrentCulture), card);
        Assert.Equal("", new OpenCS.ViewModels.PunchingFemJointRowVM(plain).ContourText);

        // Подпись варианта в окне задачи строки — «Контур ЛИРЫ», а не «Контур пользователя».
        var inputs = System.Text.Json.JsonDocument.Parse("""{"customNames":["ЛИРА"]}""").RootElement;
        Assert.Equal(Loc.S("PunchingVariant_CustomLira", "Контур ЛИРЫ"),
            OpenCS.ViewModels.PunchingResultVM.CustomTitle(PunchingVariantKind.Custom, "custom_0", inputs));
        Assert.Null(OpenCS.ViewModels.PunchingResultVM.CustomTitle(PunchingVariantKind.Closed, "closed", inputs));
    }

    [Fact]
    public void CheckWindow_ColumnsWithoutSections_OfferBarSections()
    {
        var s = Create();
        using var db = s.Db;
        var column = db.CrossSections.Single(c => c.Id == 9001);
        db.CrossSections.Remove(column);
        s.Schema.SourceType = "lira";

        var vm = new OpenCS.ViewModels.PunchingFemCheckVM(db, null, s.Schema);
        Assert.True(vm.HasBarSectionsHint);
        Assert.False(vm.CanOfferBarSections);   // команды ещё нет
        int calls = 0;
        vm.CreateBarSections = _ => { calls++; db.CrossSections.Add(column); return true; };
        Assert.True(vm.CanOfferBarSections);
        vm.CreateBarSectionsCommand.Execute(null);
        Assert.Equal(1, calls);
        // Сечения назначены — узлы найдены заново, подсказки нет.
        Assert.False(vm.HasBarSectionsHint);
        Assert.Contains(vm.Joints, j => j.Checkable);

        // Своя схема: подсказка без кнопки.
        db.CrossSections.Remove(column);
        s.Schema.SourceType = "internal";
        var own = new OpenCS.ViewModels.PunchingFemCheckVM(db, null, s.Schema) { CreateBarSections = _ => true };
        Assert.True(own.HasBarSectionsHint);
        Assert.False(own.CanOfferBarSections);
    }

    [Fact]
    public void LiraContour_NodeContourMode_KeptInOverride()
    {
        var joint = new PunchingFemJoint
        {
            NodeTag = "7", Origin = default, AxisX = new(1, 0, 0), AxisY = new(0, 1, 0), Normal = new(0, 0, 1),
            Area = new PunchingLoadArea.Rectangle(0.4, 0.4),
        };
        var common = new PunchingReinforcementParams();
        var row = new OpenCS.ViewModels.PunchingFemJointPickVM(joint, new PunchingFemJointOverride { Node = "7", Contour = "lira" });
        Assert.Equal("lira", row.ContourMode);
        // Узла нет в *.asp, но выбор «ЛИРЫ» уже стоит — список доступен, чтобы его можно было вернуть.
        Assert.True(row.CanPickContour);
        Assert.Equal("lira", row.ToOverride(common)!.Contour);
        row.ContourMode = "opencs";
        Assert.Equal("opencs", row.ToOverride(common)!.Contour);
        row.ContourMode = "check";
        Assert.Null(row.ToOverride(common));
        Assert.False(row.CanPickContour);
    }

    /// <summary>*.asp v6 без пластин и стержней: записи продавливания (узел, Asw, Ksw) и квадратные контуры 0,74 м.</summary>
    static byte[] LiraAsp(params (int Node, PlanarVector3 At, float Asw, float Ksw)[] recs)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var header = new byte[272];
        System.Text.Encoding.ASCII.GetBytes("ARM-SAPFIR_6 binary v.6.0 2022").CopyTo(header, 0);
        BitConverter.GetBytes(156).CopyTo(header, 100);
        BitConverter.GetBytes(recs.Length).CopyTo(header, 240);
        BitConverter.GetBytes(120).CopyTo(header, 244);
        w.Write(header);
        for (int i = 0; i < recs.Length; i++)
        {
            var b = new byte[120];
            BitConverter.GetBytes(i + 1).CopyTo(b, 0);
            BitConverter.GetBytes(7).CopyTo(b, 8);
            System.Text.Encoding.ASCII.GetBytes("A240").CopyTo(b, 12);
            System.Text.Encoding.ASCII.GetBytes("B25").CopyTo(b, 44);
            BitConverter.GetBytes(recs[i].Asw).CopyTo(b, 76);
            BitConverter.GetBytes(recs[i].Ksw).CopyTo(b, 80);
            BitConverter.GetBytes(recs[i].Node).CopyTo(b, 104);
            w.Write(b);
        }
        foreach (var r in recs)
        {
            float x = (float)r.At.X, y = (float)r.At.Y, z = (float)r.At.Z, h = 0.37f;
            w.Write(x); w.Write(y); w.Write(z); w.Write(4);
            float[][] segs = [[x - h, y - h, x + h, y - h], [x + h, y - h, x + h, y + h], [x + h, y + h, x - h, y + h], [x - h, y + h, x - h, y - h]];
            foreach (var sg in segs) { w.Write(sg[0]); w.Write(sg[1]); w.Write(z); w.Write(sg[2]); w.Write(sg[3]); w.Write(z); }
        }
        w.Write(new byte[16]);
        return ms.ToArray();
    }

    [Fact]
    public void ColumnAbove_NetForceIsDifference()
    {
        var s = Create(above: true);
        using var db = s.Db;
        var (_, source, joint, forces) = Evaluate(s);
        Assert.Equal(2, joint.Bars.Count);
        var f = forces.ByKey[source.Keys.Single(k => k.Label == "Пост.").Key];
        // Верхняя колонна без нагрузки: вся сила плиты — в нижнюю.
        Assert.Equal(360.0, f.ColumnAction!.Value, 2);
        Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value, 3);
    }

    [Fact]
    public void CheckRunner_EnvelopeAndRowTask()
    {
        var s = Create();
        using var db = s.Db;
        var p = new PunchingFemCheckParams { SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture) };
        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id, p, CalcType.C);
        Assert.Null(data.Error);
        var j = Assert.Single(data.Joints);
        Assert.Null(j.NoCheck);
        Assert.Equal(2, j.Rows.Count);
        Assert.Equal("Пост.", j.GoverningKey);
        Assert.True(j.Ratio > 0.1, $"Кисп = {j.Ratio}");
        Assert.Equal(j.Rows.Max(r => r.Ratio), data.Ratio);
        Assert.Contains(j.Status, new[] { "concrete", "reinforcement", "not_ensured" });
        Assert.Equal(j.Passed, data.Passed);

        // Задача по строке — та же проверка ядра с полной трассировкой.
        var row = j.Rows.Single(r => r.Key == "Пост.");
        var tp = PunchingFemCheckRunner.RowParams(j, row)!;
        Assert.Empty(tp.Validate());
        var st = PunchingRunner.ResolveStrengths(tp, db.Materials, CalcType.C, out var errors);
        Assert.Empty(errors);
        Assert.Equal(row.Ratio, PunchingChecker.Check(tp.ToInput(st.Rbt, st.Rsw)).Utilization, 12);
        Assert.Equal("Пост.", tp.Source!.Key);

        // Круговой проход JSON; Кисп — и под общим ключом utilization (консоль).
        Assert.Contains("\"utilization\":", data.ToJson());
        var back = PunchingFemCheckData.Parse(data.ToJson())!;
        Assert.Equal(data.Ratio, back.Ratio);
        Assert.Equal(row.F, back.Joints[0].Rows.Single(r => r.Key == "Пост.").F);
    }

    [Fact]
    public void CheckRunner_CrossLayout_DesignPerJoint()
    {
        var s = Create();
        using var db = s.Db;
        string src = s.Analysis.Id.ToString(CultureInfo.InvariantCulture);
        // Rbt подбирается так, чтобы бетона не хватало (Кисп по бетону ≈ 1,3): Fb и Mb пропорциональны Rbt.
        var bare = PunchingFemCheckRunner.Compute(db, s.Schema.Id,
            new PunchingFemCheckParams { SourceRef = src, ConcreteFromSchema = false, RbtOverride = 1050 }, CalcType.C);
        double k0 = Assert.Single(bare.Joints).Rows.Max(r => r.RatioConcrete);
        Assert.True(k0 > 0.05, $"Кисп по бетону = {k0}");

        // Крест 3 линии × 50 мм в обе стороны, ряды 60 + 4·50 мм (h0 ≈ 160 мм: h0/3 ≤ 60 ≤ h0/2, 50 ≤ h0/3).
        var p = new PunchingFemCheckParams
        {
            SourceRef = src, ConcreteFromSchema = false, RbtOverride = 1050 * k0 / 1.3,
            Reinforcement = new PunchingReinforcementParams
            {
                Enabled = true, Diameter = 0.006, Layout = "cross", RswOverride = 300_000,
                FirstRow = 0.06, RowStep = 0.05, RowCount = 5,
                CrossX = new PunchingCrossArmParams { Lines = 3, Step = 0.05 },
                CrossY = new PunchingCrossArmParams { Lines = 3, Step = 0.05 },
            },
        };
        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id, p, CalcType.C);
        Assert.Null(data.Error);
        var j = Assert.Single(data.Joints);
        Assert.True(j.Rows.Max(r => r.RatioConcrete) > 1.0);
        Assert.Contains(j.DesignState, new[] { "by_calculation", "by_threshold" });
        Assert.Equal("Пост.", j.DesignKey);
        Assert.Null(j.QswReq);                       // у креста — требования по направлениям
        Assert.True(j.QswXReq > 0 && j.QswYReq > 0, $"qsw,x = {j.QswXReq}, qsw,y = {j.QswYReq}");
        Assert.NotNull(j.BarDiameter);
        Assert.NotNull(j.DesignSufficient);

        // Круговой проход JSON и строка таблицы узлов: «qsw,x / qsw,y», подбор в карточке.
        var back = PunchingFemCheckData.Parse(data.ToJson())!.Joints.Single();
        Assert.Equal(j.QswXReq, back.QswXReq);
        Assert.Equal(j.DesignState, back.DesignState);
        var row = new OpenCS.ViewModels.PunchingFemJointRowVM(back);
        Assert.Contains("/", row.Qsw);
        Assert.Contains("Пост.", row.DesignText);
        Assert.Contains("qsw,x", row.DesignText);

        // Задача по строке несёт ту же раскладку.
        var tp = PunchingFemCheckRunner.RowParams(j, j.Rows.Single(r => r.Key == "Пост."))!;
        Assert.Equal("cross", tp.Reinforcement.Layout);
        Assert.Equal(5, tp.Reinforcement.RowCount);
    }

    [Fact]
    public void CheckRunner_ExcludedJoint_NoAnalysis_Error()
    {
        var s = Create();
        using var db = s.Db;
        var none = PunchingFemCheckRunner.Compute(db, s.Schema.Id, new PunchingFemCheckParams(), CalcType.C);
        Assert.NotNull(none.Error);
        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id, new PunchingFemCheckParams
        {
            SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture),
            Overrides = [new PunchingFemJointOverride { Node = S(SlabNode(N / 2, N / 2)), Excluded = true }],
        }, CalcType.C);
        Assert.Empty(data.Joints);
        Assert.Contains(data.Warnings, w => w.Code == "punching_fem_no_joints");
    }

    [Fact]
    public void Report_SummaryAndGoverningDetail()
    {
        var s = Create();
        using var db = s.Db;
        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id,
            new PunchingFemCheckParams { SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture) }, CalcType.C);
        Assert.Equal(S(SlabNode(N / 2, N / 2)), data.GoverningNode);
        Assert.False(string.IsNullOrEmpty(data.GoverningDetail));
        var result = new CalcResult { TaskKind = PunchingFemCheckParams.NormCode, TaskTag = "Пр", Status = "ok", DataJson = data.ToJson() };
        var doc = new OpenCS.Reporting.PunchingFemCheckReportProvider().Build(new OpenCS.Reporting.ReportContext(
            new CalcTask { Kind = PunchingFemCheckParams.NormCode, Tag = "Пр", CalcType = CalcType.C }, result));
        var headings = doc.Blocks.OfType<OpenCS.Reporting.ReportHeading>().Select(h => h.Text).ToList();
        Assert.Contains("Ведомость узлов", headings);
        Assert.Contains(headings, h => h.StartsWith("Определяющий узел", StringComparison.Ordinal));
        Assert.Contains("Источник усилий", headings);
        Assert.Contains(headings, h => h.Contains("Расчётная схема", StringComparison.Ordinal));
    }

    [Fact]
    public void ResultVM_SelectsGoverningRowWithFullTrace()
    {
        var s = Create();
        using var db = s.Db;
        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id,
            new PunchingFemCheckParams { SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture) }, CalcType.C);
        var result = new CalcResult { TaskKind = PunchingFemCheckParams.NormCode, Status = "ok", DataJson = data.ToJson() };
        var vm = new OpenCS.ViewModels.PunchingFemResultVM(result, null, db.Materials);
        var joint = Assert.Single(vm.Joints);
        Assert.Same(joint, vm.SelectedJoint);
        Assert.Equal(2, vm.Rows.Count);
        Assert.True(vm.SelectedRow!.Governs);
        Assert.NotNull(vm.Detail);
        Assert.False(vm.Detail!.HasError, vm.Detail.ErrorText);
        Assert.NotEmpty(vm.Detail.Rows);
        Assert.Equal(data.Joints[0].Ratio, vm.Detail.Rows.Max(r => r.KValue), 9);
        vm.SelectedRow = vm.Rows[1];
        Assert.NotNull(vm.Detail);
        Assert.Single(vm.Levels);
    }

    [Fact]
    public void CheckVM_FindsJointsAndBuildsParams()
    {
        var s = Create();
        using var db = s.Db;
        var vm = new OpenCS.ViewModels.PunchingFemCheckVM(db, null, s.Schema);
        var pick = Assert.Single(vm.Joints);
        Assert.True(pick.Checkable);
        Assert.Same(pick, vm.SelectedJoint);
        Assert.NotNull(vm.Scene);
        Assert.False(vm.HasErrors, vm.ErrorText);
        Assert.Equal(s.Analysis.Id, vm.Analysis!.Id);
        Assert.Empty(vm.Validate());

        pick.Ax = 50;
        pick.Include = false;
        var p = vm.ToParams();
        var o = Assert.Single(p.Overrides);
        Assert.True(o.Excluded);
        Assert.Equal(0.05, o.Ax!.Value, 12);

        var check = new FemCheck();
        vm.ApplyTo(check);
        Assert.Equal(PunchingFemCheckParams.NormCode, check.NormCode);
        Assert.Equal(s.Schema.Id, check.SchemaId);
        var back = new OpenCS.ViewModels.PunchingFemCheckVM(db, check);
        Assert.False(Assert.Single(back.Joints).Include);
        Assert.Equal(50, back.Joints[0].Ax);
    }

    /// <summary>
    /// Разметка окна параметров загружается без ресурсов приложения (конвертеры объявлены в окне). Окно результата
    /// встраивает панель одиночной задачи со стилем строк приложения — его проверяет ручной прогон.
    /// </summary>
    [Fact]
    public void Dialog_LoadsOnStaThread()
    {
        var s = Create();
        using var db = s.Db;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new OpenCS.Views.PunchingFemCheckDialog(db, null, s.Schema);
                dialog.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.True(error == null, error?.ToString());
    }

    void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Компонента программы (N Qy₁ Qz₁ Mк My₁ Mz₁) ← компонента осей сетки и знак: Y₁ = −Z, Z₁ = Y
    /// (обратное к <see cref="CScore.Import.ImportedBarAxes.ToMesh(CScore.Submodel.Dof6)"/>).
    /// </summary>
    static readonly (int Mesh, double Sign)[] ToProgram = [(0, 1), (2, -1), (1, 1), (3, 1), (5, -1), (4, 1)];

    /// <summary>
    /// Снимок «как SCAD» из полей линейного результата (как в тестах масок): шаг i — загружение программы № i; стержни —
    /// силы сечений в знаках SCAD, пластины — в осях выдачи CSfea.
    /// </summary>
    void ScadSnapshotFromCsfea(DatabaseService db, FemSchema schema, FemAnalysis analysis, IReadOnlyList<FemLoadCase> lc)
    {
        var nodes = db.GetFemMeshNodes(schema.Id).ToDictionary(n => n.NodeTag);
        var members = db.GetFemMembers(schema.Id).ToDictionary(m => m.ElemTag);
        var regionAxes = FemCsfeaInputBuilder.RegionAxes([.. members.Values], db.GetPlanarRegions(schema.Id));
        foreach (var e in db.GetFemMeshElements(schema.Id))
        {
            var g = CScore.Fem.Loads.FemElementGeometry.Of(e, nodes)!;
            if (g.IsShell)
            {
                var axis = FemRcModelAdapter.ShellForceAxis(e, g, members, regionAxes);
                var (x1, _, n) = g.ShellFrame();
                double deg = Math.Atan2(x1.Cross(axis).Dot(n), x1.Dot(axis)) * 180 / Math.PI;
                Exec(FormattableString.Invariant($"UPDATE fem_elements SET local_axis_angle_deg={deg:R} WHERE schema_id={schema.Id} AND elem_tag='{e.ElemTag}'"));
            }
            else
                Exec(FormattableString.Invariant($"UPDATE fem_elements SET beam_rotation_deg={FemRcModelAdapter.BeamRotationDeg(e, members):R} WHERE schema_id={schema.Id} AND elem_tag='{e.ElemTag}'"));
        }
        var c = CScore.Fem.Fragment.Masks.ImportedForceConvention.Scad;
        var loads = new List<CScore.Fem.Fragment.Masks.ImportedResultLoad>();
        for (int i = 1; i <= lc.Count; i++)
        {
            var f = FemCsfeaStepFieldsCodec.Unpack(db.GetFemResultStep(analysis.ResultId!.Value, i)!);
            var bars = new double[f.BeamIds.Length * CScore.Fem.Fragment.Masks.ImportedResultLoad.BarComponents];
            for (int b = 0; b < f.BeamIds.Length; b++)
                for (int k = 0; k < 6; k++)
                {
                    // Сила узла на КЭ: у узла 1 −S(0), у узла 2 +S(L); программа = c · S в осях программы: CSfea — в осях
                    // сетки (Y = Z1, Z = −Y1), поэтому компонента программы k берётся из компоненты сетки ToProgram[k] со знаком.
                    var (m, sg) = ToProgram[k];
                    bars[12 * b + k] = -c.Bar[k] * sg * f.BeamForces[RcSecantStepFields.BeamForceComponents * b + m];
                    bars[12 * b + 6 + k] = c.Bar[k] * sg * f.BeamForces[RcSecantStepFields.BeamForceComponents * b + 6 + m];
                }
            var shells = new double[f.ShellIds.Length * CScore.Fem.Fragment.Masks.ImportedResultLoad.ShellComponents];
            for (int sh = 0; sh < f.ShellIds.Length; sh++)
                for (int k = 0; k < 8; k++)
                    shells[8 * sh + k] = c.Shell[k] * f.ShellForces[RcSecantStepFields.ShellForceComponents * sh + k];
            loads.Add(new CScore.Fem.Fragment.Masks.ImportedResultLoad
            {
                Num = i, Name = $"Загружение {i}", NodeNums = f.NodeIds, Displacements = f.Displacements,
                BarNums = f.BeamIds, BarForces = bars, ShellNums = f.ShellIds, ShellForces = shells,
            });
            Exec($"UPDATE fem_load_cases SET origin='import:scad', source_load_num={i} WHERE id={lc[i - 1].Id}");
        }
        var snapshot = new CScore.Fem.Fragment.Masks.ImportedResultSnapshot
        {
            Program = CScore.Fem.Fragment.Masks.ImportedResultSnapshot.ProgramScad, SourceName = "плита.SPR",
            TakenAt = "2026-11-15T12:00:00", Loads = loads,
        };
        db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadMaskResults, "плита.SPR",
            CScore.Fem.Fragment.Masks.ImportedResultSnapshotCodec.Encode(snapshot));
    }

    /// <summary>
    /// Снимок SCAD из того же линейного расчёта и сочетание 1,2·«Пост.» + «Ветер»: сила по колоннам — суперпозиция
    /// загружений CSfea; справочный разрез по усилиям пластин — того же порядка, что разрез по узловым силам.
    /// </summary>
    [Fact]
    public void Imported_Scad_CombinationAndShellCut()
    {
        var s = Create();
        using var db = s.Db;
        var lc = db.GetFemLoadCases(s.Schema.Id);
        var csfea = Evaluate(s);
        var post = csfea.Forces.ByKey[csfea.Source.Keys.Single(k => k.Label == "Пост.").Key];
        var wind = csfea.Forces.ByKey[csfea.Source.Keys.Single(k => k.Label == "Ветер").Key];

        ScadSnapshotFromCsfea(db, s.Schema, s.Analysis, lc);
        var def = new FemLoadDefinition { SchemaId = s.Schema.Id, Tag = "РСН1" };
        def.SetExpression(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = [new FemLoadTerm { LoadCaseId = lc[0].Id, Coefficient = 1.2 }, new FemLoadTerm { LoadCaseId = lc[1].Id, Coefficient = 1.0 }],
        });
        db.SaveFemLoadDefinition(def);

        var diag = new List<FemValidationDiagnostic>();
        var source = PunchingFemService.OpenImported(db, s.Schema, CScore.Fem.Fragment.Masks.FragmentMaskSet.SourceScad, diag);
        Assert.True(source != null, string.Join("; ", diag.Select(d => d.Message)));
        var (key, label) = Assert.Single(source!.Keys);
        Assert.Equal("РСН1", label);
        var data = PunchingFemService.Load(db, s.Schema, new PunchingFemOptions());
        var f = PunchingFemService.Evaluate(data, source, Assert.Single(data.Joints)).ByKey[key];
        Assert.Equal(PunchingFemForceMethod.Column, f.Method);
        Assert.Equal(1.2 * post.ColumnAction!.Value + wind.ColumnAction!.Value, f.ColumnAction!.Value, 3);
        Assert.Equal(1.2 * post.BaseLoad!.Value + wind.BaseLoad!.Value, f.BaseLoad!.Value, 6);
        Assert.Equal(1.2 * post.Mx + wind.Mx, f.Mx, 3);
        Assert.NotNull(f.ContourForce);
        double cut = 1.2 * post.ContourForce!.Value + wind.ContourForce!.Value;
        Assert.True(Math.Abs(f.ContourForce!.Value - cut) < 0.25 * cut, $"разрез по пластинам {f.ContourForce} против узлового {cut}");

        // Проверка целиком по снимку.
        var check = PunchingFemCheckRunner.Compute(db, s.Schema.Id,
            new PunchingFemCheckParams { SourceKind = CScore.Fem.Fragment.Masks.FragmentMaskSet.SourceScad }, CalcType.C);
        Assert.Null(check.Error);
        Assert.Equal("РСН1", Assert.Single(check.Joints).GoverningKey);
        Assert.Contains("SCAD", check.Source);
    }

    /// <summary>
    /// Плита на упругом основании C1: у CSfea в нагрузку основания входит реакция основания (−K·u), у снимка — тот же
    /// отпор по перемещениям снимка; нагрузка основания и сила по колоннам совпадают.
    /// </summary>
    [Fact]
    public void Imported_FoundationC1_BaseLoadIncludesSoilReaction()
    {
        var s = Create(c1: 2e6);
        using var db = s.Db;
        var lc = db.GetFemLoadCases(s.Schema.Id);
        var csfea = Evaluate(s);
        var post = csfea.Forces.ByKey[csfea.Source.Keys.Single(k => k.Label == "Пост.").Key];

        ScadSnapshotFromCsfea(db, s.Schema, s.Analysis, lc);
        var diag = new List<FemValidationDiagnostic>();
        var source = PunchingFemService.OpenImported(db, s.Schema, CScore.Fem.Fragment.Masks.FragmentMaskSet.SourceScad, diag);
        Assert.True(source != null, string.Join("; ", diag.Select(d => d.Message)));
        Assert.DoesNotContain(diag, d => d.Code == "punching_fem_foundation_no_displacements");
        var data = PunchingFemService.Load(db, s.Schema, new PunchingFemOptions());
        var f = PunchingFemService.Evaluate(data, source!, Assert.Single(data.Joints)).ByKey[source!.Keys.Single(k => k.Label == "Пост.").Key];

        Assert.NotEqual(0, post.BaseLoad!.Value);
        Assert.Equal(post.BaseLoad!.Value, f.BaseLoad!.Value, 6);
        Assert.Equal(post.ColumnAction!.Value, f.ColumnAction!.Value, 3);
        Assert.Equal(post.Mx, f.Mx, 3);
        Assert.Equal(post.My, f.My, 3);
    }

    /// <summary>
    /// Сочетание программы, разложенное на загружения (подбор коэффициентов строки РСУ/РСН ЛИРЫ): ключ по номерам
    /// загружений снимка → усилия узла = суперпозиция загружений, включая нагрузку основания.
    /// </summary>
    [Fact]
    public void Imported_WithKeys_ProgramCombination()
    {
        var s = Create();
        using var db = s.Db;
        var lc = db.GetFemLoadCases(s.Schema.Id);
        ScadSnapshotFromCsfea(db, s.Schema, s.Analysis, lc);

        var diag = new List<FemValidationDiagnostic>();
        var source = PunchingFemService.OpenImported(db, s.Schema, CScore.Fem.Fragment.Masks.FragmentMaskSet.SourceScad, diag);
        Assert.True(source != null, string.Join("; ", diag.Select(d => d.Message)));
        Assert.NotNull(source!.ProgramLoadCases);
        Assert.Equal(lc[0].Id, source.ProgramLoadCases![1]);
        var data = PunchingFemService.Load(db, s.Schema, new PunchingFemOptions());
        var joint = Assert.Single(data.Joints);
        var perCase = PunchingFemService.Evaluate(data, source, joint);
        var post = perCase.ByKey[source.Keys.Single(k => k.Label == "Пост.").Key];
        var wind = perCase.ByKey[source.Keys.Single(k => k.Label == "Ветер").Key];

        var key = CScore.Fem.Fragment.Masks.FragmentMaskKey.OfStageTerms(
            [(source.ProgramLoadCases[1], 0.95), (source.ProgramLoadCases[2], -0.9)]);
        var combined = source.WithKeys([(key, "РСУ э.1 с1 к18")]);
        Assert.Equal("РСУ э.1 с1 к18", Assert.Single(combined.Keys).Label);
        var f = PunchingFemService.Evaluate(data, combined, joint).ByKey[key];
        Assert.Equal(0.95 * post.ColumnAction!.Value - 0.9 * wind.ColumnAction!.Value, f.ColumnAction!.Value, 3);
        Assert.Equal(0.95 * post.BaseLoad!.Value - 0.9 * wind.BaseLoad!.Value, f.BaseLoad!.Value, 6);
        Assert.Equal(0.95 * post.Mx - 0.9 * wind.Mx, f.Mx, 3);
        Assert.Equal(0.95 * post.My - 0.9 * wind.My, f.My, 3);
    }

    /// <summary>
    /// Наборы из ЛИРЫ: стержень колонны у плиты → сечение ЛИРЫ; строки загружений, РСН и РСУ — усилия узла по
    /// разложенным сочетаниям (суперпозиция загружений снимка); повторный запуск обновляет набор и задачу узла.
    /// </summary>
    [Fact]
    public void LiraForceSets_RowsByFittedCombinations_UpdateOnRerun()
    {
        var s = Create();
        using var db = s.Db;
        var lc = db.GetFemLoadCases(s.Schema.Id);
        ScadSnapshotFromCsfea(db, s.Schema, s.Analysis, lc);
        var diag = new List<FemValidationDiagnostic>();
        var source = PunchingFemService.OpenImported(db, s.Schema, CScore.Fem.Fragment.Masks.FragmentMaskSet.SourceScad, diag)!;
        var data = PunchingFemService.Load(db, s.Schema, new PunchingFemOptions());
        var joint = Assert.Single(data.Joints);

        var warnings = new List<string>();
        var bars = PunchingLiraForceSets.Bars(data, data.Joints, warnings);
        Assert.Empty(warnings);
        Assert.Equal([(5002, false)], bars);

        // Усилия ЛИРЫ в сечении у плиты (значения условные: по ним только раскладываются сочетания).
        var at = new CScore.Import.LiraBarSection(5002, 2);
        double[] v1 = [-40, 0.01, 2.3, -0.8, 0.5, -0.2], v2 = [-12, 0.02, 0.7, 2.1, 0.1, 0.7];
        double[] Comb(double a, double b) => v1.Zip(v2, (x, y) => a * x + b * y).ToArray();
        var lira = new PunchingLiraInput("плита",
            new Dictionary<CScore.Import.LiraBarSection, IReadOnlyDictionary<int, IReadOnlyList<double>>>
            {
                [at] = new Dictionary<int, IReadOnlyList<double>> { [1] = v1, [2] = v2 },
            },
            [new CScore.Import.LiraRsuRow(at, 1, 18, "А1", [1, 2], Comb(1.2, 0.9))],
            [new CScore.Import.LiraRsnRow(1, "Осн.", new Dictionary<CScore.Import.LiraBarSection, IReadOnlyList<double>> { [at] = Comb(1.0, 1.0) })],
            new Dictionary<int, string> { [1] = "Пост." });
        var check = new PunchingFemCheckParams { SourceKind = CScore.Fem.Fragment.Masks.FragmentMaskSet.SourceScad };

        var outcome = PunchingLiraForceSets.Build(data, source, data.Joints, lira, check, [], []);
        Assert.Empty(outcome.Warnings);
        var js = Assert.Single(outcome.Joints);
        Assert.Equal($"Продавливание, узел {joint.NodeTag}", js.Set.Tag);
        Assert.Equal((2, 1, 1), (js.LoadCases, js.Rsn, js.Rsu));
        var items = js.Set.Items;
        Assert.Equal(["Загр. 1 «Пост.»", "Загр. 2", "РСН 1 «Осн.»", "РСУ э.5002 с2 к18 А1"], items.Select(i => i.Label));
        Assert.Equal(items[0].N + items[1].N, items[2].N, 2);
        Assert.Equal(1.2 * items[0].N + 0.9 * items[1].N, items[3].N, 2);
        Assert.Equal(1.2 * items[0].Mx + 0.9 * items[1].Mx, items[3].Mx, 2);
        Assert.Equal(PunchingBatchRunner.Kind, js.Task.Kind);
        var p = PunchingTaskParams.Parse(js.Task.ParamsJson);
        Assert.Equal(PunchingBatchRunner.DominantSign(items) >= 0 ? "from_below" : "from_above", p.Forces.LoadSide);

        // Повторный запуск: тот же набор и та же задача.
        js.Set.Id = 7;
        js.Task.Id = 3;
        js.Task.ForceSetId = 7;
        var again = Assert.Single(PunchingLiraForceSets.Build(data, source, data.Joints, lira, check, [js.Set], [js.Task]).Joints);
        Assert.Same(js.Set, again.Set);
        Assert.Same(js.Task, again.Task);
        Assert.Equal(4, again.Set.Items.Count);
    }

    [Fact]
    public void TaskParams_RunCore()
    {
        var s = Create();
        using var db = s.Db;
        var (data, source, joint, forces) = Evaluate(s);
        var (key, label) = source.Keys.Single(k => k.Label == "Пост.");
        var check = new PunchingFemCheckParams { ConcreteMaterialId = 0 };
        var p = PunchingFemService.TaskParams(joint, forces.ByKey[key], check, s.Schema.Tag, label);
        Assert.Empty(p.Validate());
        Assert.True(p.Forces.MomentsAreMloc);
        Assert.Equal("contour", p.Source!.Method);
        var st = PunchingRunner.ResolveStrengths(p, db.Materials, CalcType.C, out var errors);
        Assert.Empty(errors);
        var result = PunchingChecker.Check(p.ToInput(st.Rbt, st.Rsw));
        Assert.True(result.GoverningIndex >= 0);
        Assert.Equal(forces.ByKey[key].F, result.Fred, 3);   // усилия задачи округлены до 1 Н
    }
}
