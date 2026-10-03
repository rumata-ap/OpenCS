using CScore;
using CScore.Fem;
using CScore.Import;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сечения стержней импортированных схем: параметрические ЖБ-сечения по жёсткостям и классам, назначение КЭ.</summary>
public sealed class ImportedBarSectionCreatorTests : IDisposable
{
    readonly string _dbPath = Path.Combine(Path.GetTempPath(), "opencs_bar_sections_" + Guid.NewGuid().ToString("N") + ".db");
    readonly DatabaseService _db;
    int _schemaId;

    public ImportedBarSectionCreatorTests() => _db = new DatabaseService(_dbPath);

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS", "DataSource");
    }

    static LiraStiffnessRecord LiraBrus(int num, int bCm, int hCm) =>
        new(num, LiraStiffnessParams.BarRectKind, $"Брус {bCm} X {hCm}", $"Ro:2.5 E:3e+06 B:{bCm} H:{hCm} BAR_END", 0.01);

    static LiraStiffnessRecord Scad(int num, string body, string name) =>
        new(num, ScadStiffnessParams.ScadKindCode, name, body, 0.01);

    /// <summary>Схема с КЭ сетки: номер → жёсткость (null — пластина).</summary>
    List<FemElement> Mesh(string source, params (int Num, int? Stiffness)[] elements)
    {
        var schema = new FemSchema { Tag = "Схема", SourceType = source };
        _db.SaveFemSchema(schema);
        _schemaId = schema.Id;
        _db.SaveFemMeshSnapshot(schema.Id,
            [new FemMeshNode { NodeTag = "1", X = 0 }, new FemMeshNode { NodeTag = "2", X = 1 }],
            [.. elements.Select(e => new FemElement
            {
                ElemTag = e.Num.ToString(), ElemType = e.Stiffness == null ? "shell" : "beam", NodeIdsJson = "[1,2]",
            })]);
        var mesh = _db.GetFemMeshElements(schema.Id);
        foreach (var e in mesh)
            e.StiffnessNum = elements.Single(x => x.Num.ToString() == e.ElemTag).Stiffness;
        return mesh;
    }

    static LiraAspFile Asp(params int[] elements) => new()
    {
        Bars = elements.ToDictionary(n => n, n => new LiraAspBar { ElementId = n, ConcreteClass = "B25", RebarClass = "A500" }),
    };

    FemCheckSchemaData LiraData(List<FemElement> mesh, LiraAspFile? asp) => new()
    {
        SourceType = "lira", Mesh = mesh, Asp = asp,
        Stiffnesses = new Dictionary<int, LiraStiffnessRecord> { [1] = LiraBrus(1, 30, 50), [2] = LiraBrus(2, 40, 40) },
    };

    Dictionary<string, int?> Reloaded(List<FemElement> mesh) =>
        _db.GetFemMeshElements(_schemaId).ToDictionary(e => e.ElemTag, e => e.CrossSectionId);

    [Fact]
    public void Lira_OneSectionPerSizeAndClasses_MaterialsCreated_ElementsAssigned()
    {
        var mesh = Mesh("lira", (1, 1), (2, 1), (3, 2), (4, null));
        var report = ImportedBarSectionCreator.Create(_db, LiraData(mesh, Asp(1, 2, 3)), catalogDirectory: CatalogDirectory());

        Assert.Equal(["B25", "A500 (6-40 мм)"], report.Materials);
        Assert.Equal(["Брус 300×500 B25 A500", "Брус 400×400 B25 A500"], report.Sections.Order());
        Assert.Empty(report.Skipped);
        Assert.Equal(3, report.AssignedElements);

        var sections = _db.CrossSections.ToDictionary(s => s.Tag);
        var ids = Reloaded(mesh);
        Assert.Equal(sections["Брус 300×500 B25 A500"].Id, ids["1"]);
        Assert.Equal(ids["1"], ids["2"]);
        Assert.Equal(sections["Брус 400×400 B25 A500"].Id, ids["3"]);
        Assert.Null(ids["4"]);                              // пластина не трогается

        // Сечение — бетон без арматуры, оси x ‖ Y1 (B = 300), y ‖ Z1 (H = 500).
        var concrete = Assert.Single(sections["Брус 300×500 B25 A500"].Areas);
        Assert.Equal(MatType.Concrete, concrete.Material!.Type);
        Assert.Equal(0.3, concrete.Hull!.X.Max() - concrete.Hull.X.Min(), 9);
        Assert.Equal(0.5, concrete.Hull!.Y.Max() - concrete.Hull.Y.Min(), 9);
    }

    [Fact]
    public void Lira_SecondRun_NoDuplicates_ManualAssignmentKept()
    {
        var mesh = Mesh("lira", (1, 1), (2, 1));
        var data = LiraData(mesh, Asp(1, 2));
        ImportedBarSectionCreator.Create(_db, data, catalogDirectory: CatalogDirectory());
        int sections = _db.CrossSections.Count;

        var again = ImportedBarSectionCreator.Create(_db, data, catalogDirectory: CatalogDirectory());
        Assert.Empty(again.Materials);
        Assert.Empty(again.Sections);
        Assert.Equal(2, again.AlreadyAssigned);
        Assert.Equal(sections, _db.CrossSections.Count);

        // Назначение сброшено у одного КЭ — существующее сечение используется повторно, даже переименованное.
        var section = _db.CrossSections.Single();
        section.Tag = "Ригель";
        _db.SetFemElementCrossSections([(mesh.Single(e => e.ElemTag == "2"), null)]);
        var reused = ImportedBarSectionCreator.Create(_db, data, catalogDirectory: CatalogDirectory());

        Assert.Equal(["Ригель"], reused.Reused);
        Assert.Empty(reused.Sections);
        Assert.Equal(1, reused.AlreadyAssigned);
        Assert.Equal(section.Id, Reloaded(mesh)["2"]);
    }

    [Fact]
    public void Lira_NoAsp_NoMaterialData_AndNotInAsp_Skipped()
    {
        var mesh = Mesh("lira", (1, 1), (2, 1), (3, 7));
        Assert.True(ImportedBarSectionCreator.Create(_db, LiraData(mesh, null)).NoMaterialData);

        var report = ImportedBarSectionCreator.Create(_db, LiraData(mesh, Asp(1, 3)), catalogDirectory: CatalogDirectory());
        Assert.Equal(1, report.AssignedElements);
        Assert.Contains(report.Skipped, s => s.Reason.Contains("ASP") && s.Elements.SequenceEqual([2]));
        Assert.Contains(report.Skipped, s => s.Reason.Contains("жёсткости 7 нет") && s.Elements.SequenceEqual([3]));
    }

    [Fact]
    public void Scad_ClassesFromConcreteGroups_StzSkipped()
    {
        var mesh = Mesh("scad", (814, 6), (815, 6), (816, 4), (900, 6));
        var data = new FemCheckSchemaData
        {
            SourceType = "scad", Mesh = mesh,
            ScadConcreteGroups = new ScadConcreteGroupIndex(
            [
                new ScadConcreteGroup(2, "колонны", 1, [0.04, 0.04, 0, 0], "B30", "A400", "A240", false, [0.4, 0.3], [814, 815, 816]),
            ]),
            Stiffnesses = new Dictionary<int, LiraStiffnessRecord>
            {
                [6] = Scad(6, "S0 900000 40 60 NU 0.2", "Колонны"),
                [4] = Scad(4, "STZ RUSSIAN pu_typep 13", "Швеллер"),
            },
        };

        var report = ImportedBarSectionCreator.Create(_db, data, catalogDirectory: CatalogDirectory());

        Assert.Equal(["Брус 400×600 B30 A400"], report.Sections);
        Assert.Equal(2, report.AssignedElements);
        Assert.Contains(report.Skipped, s => s.Reason.Contains("форма сечения не поддерживается") && s.Elements.SequenceEqual([816]));
        Assert.Contains(report.Skipped, s => s.Reason.Contains("ЖБ-группах SCAD") && s.Elements.SequenceEqual([900]));
        var ids = Reloaded(mesh);
        Assert.Equal(ids["814"], ids["815"]);
        Assert.NotNull(ids["814"]);
        Assert.Null(ids["816"]);
    }
}
