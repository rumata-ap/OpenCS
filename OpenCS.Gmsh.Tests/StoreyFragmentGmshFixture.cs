using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Общее для тестов фрагмента этажа на сетке Gmsh: материалы каталога, сечения П200 и К400, сервис сетки, стадии
/// постановки, временная папка с БД.
/// </summary>
static class StoreyFragmentGmshFixture
{
    public const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";

    public static string S(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Тело теста во временной папке; БД и артефакты Gmsh удаляются после.</summary>
    public static async Task InTempRoot(string prefix, Func<string, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try { await body(root); }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>Сечения проекта: плита/стена П200 (B25, А500С 5 см²/м в обе стороны у каждой грани) и колонна К400.</summary>
    public sealed record Sections(Material Concrete, Material Rebar, PlateSection Plate, CrossSection Column);

    /// <summary>Материалы каталога и сечения в БД; колонна — только в памяти (в cross_sections её нет, после
    /// <see cref="DatabaseService.LoadAll"/> добавлять заново — <see cref="Restore"/>).</summary>
    public static Sections AddSections(DatabaseService db)
    {
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
        var column = Square(9001, 0.4, concrete);
        db.CrossSections.Add(column);
        return new Sections(concrete, rebar, plate, column);
    }

    /// <summary>Вернуть сечение колонны в память после <see cref="DatabaseService.LoadAll"/>.</summary>
    public static void Restore(DatabaseService db, Sections sections)
    {
        if (!db.CrossSections.Contains(sections.Column)) db.CrossSections.Add(sections.Column);
    }

    public static GmshSettings GmshSettings(string root) => new()
    {
        ExecutablePath = Gmsh, ArtifactsPath = Path.Combine(root, "gmsh"), KeepArtifacts = false,
        ElementMode = PlanarMeshElementMode.Quads, Algorithm = 8,
    };

    /// <summary>«Построить сетку схемы» с общими шагами пластин и стержней <paramref name="step"/> и запись.</summary>
    public static async Task<FemSchemaMeshBuildResult> Mesh(DatabaseService db, FemSchema schema, double step, string root)
    {
        schema.MeshPlateStepM = step;
        schema.MeshBarStepM = step;
        db.UpdateFemSchemaMeshSteps(schema);
        var service = new FemSchemaMeshService(db, GmshSettings(root));
        var meshed = await service.BuildAsync(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), step, null,
            CancellationToken.None);
        Assert.False(meshed.HasErrors, string.Join("\n", meshed.Diagnostics));
        service.Save(schema.Id, meshed.Mesh!);
        return meshed;
    }

    /// <summary>Стадия из одного загружения; λmax = 1 явно — без него разбор параметров берёт легаси-максимум 10
    /// (<see cref="FemAnalysisParams.Parse"/>).</summary>
    public static FemAnalysisStage Single(string tag, int loadCaseId) => new()
    {
        Tag = tag, LoadFactorStep = 1, MaxLoadFactor = 1,
        LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [loadCaseId] }.ToJson(),
    };

    /// <summary>Линейная постановка CSfea схемы со стадией на каждое её загружение (записана в БД).</summary>
    public static FemAnalysis LinearByLoadCases(DatabaseService db, FemSchema schema, string tag)
    {
        var analysis = new FemAnalysis
        {
            SchemaId = schema.Id, Tag = tag, Kind = FemCsfeaRunner.LinearAnalysisKind,
            ParamsJson = new FemAnalysisParams
            {
                Stages = db.GetFemLoadCases(schema.Id).Select(lc => Single(lc.Tag, lc.Id)).ToList(),
                Csfea = new FemCsfeaParams(),
            }.ToJson(),
        };
        db.SaveFemAnalysis(analysis);
        return analysis;
    }

    public static CrossSection Square(int id, double a, Material concrete) => new()
    {
        Id = id, Tag = "К400",
        Areas =
        [
            new MaterialArea
            {
                Tag = "concrete", Category = AreaCategory.Region, MaterialId = concrete.Id,
                Hull = new Contour(new[] { -a / 2, a / 2, a / 2, -a / 2, -a / 2 }, new[] { -a / 2, -a / 2, a / 2, a / 2, -a / 2 }, "outer"),
            },
        ],
    };

    public static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS.App", "DataSource");
    }
}
