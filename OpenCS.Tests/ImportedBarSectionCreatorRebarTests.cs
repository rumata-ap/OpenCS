using CScore;
using CScore.Fem;
using CScore.Import;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Армирование создаваемых ЖБ-сечений стержней по данным схемы (срез 5а, спека §5.1).</summary>
public sealed class ImportedBarSectionCreatorRebarTests : IDisposable
{
    readonly string _dbPath = Path.Combine(Path.GetTempPath(), "opencs_bar_rebar_" + Guid.NewGuid().ToString("N") + ".db");
    readonly DatabaseService _db;
    int _schemaId;

    public ImportedBarSectionCreatorRebarTests() => _db = new DatabaseService(_dbPath);

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

    /// <summary>Схема с КЭ сетки: номер → (жёсткость, ТЗА).</summary>
    List<FemElement> Mesh(string source, params (int Num, int Stiffness, string? Types)[] elements)
    {
        var schema = new FemSchema { Tag = "Схема", SourceType = source };
        _db.SaveFemSchema(schema);
        _schemaId = schema.Id;
        _db.SaveFemMeshSnapshot(schema.Id,
            [new FemMeshNode { NodeTag = "1", X = 0 }, new FemMeshNode { NodeTag = "2", X = 1 }],
            [.. elements.Select(e => new FemElement { ElemTag = e.Num.ToString(), ElemType = "beam", NodeIdsJson = "[1,2]" })]);
        var mesh = _db.GetFemMeshElements(schema.Id);
        foreach (var e in mesh)
        {
            var src = elements.Single(x => x.Num.ToString() == e.ElemTag);
            e.StiffnessNum = src.Stiffness;
            e.ReinforcementTypeIds = src.Types;
        }
        return mesh;
    }

    List<FemElement> Reloaded() => _db.GetFemMeshElements(_schemaId);

    static LiraAspBarAreas Areas(double au = 0, double as1 = 0, double as2 = 0) => new(au, au, au, au, as1, as2, 0, 0, 0, 0, 0);

    static LiraAspBar AspBar(int num, params LiraAspBarAreas[] sections) => new()
    {
        ElementId = num, ConcreteClass = "B25", RebarClass = "A500", Covers = (4, 4, 4),
        Sections = [.. sections.Select(s => new LiraAspBarSection(s, new LiraAspBarAreas(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)))],
    };

    static LiraRbtFile Rbt() => new()
    {
        BarTypes = new Dictionary<int, LiraBarReinforcementType>
        {
            [3] = new() { Id = 3, Face = LiraBarRebarFace.Bottom, Count = 3, DiameterMm = 16, BarAreaCm2 = 2.011, A = 4, ASide = 4 },
        },
    };

    FemCheckSchemaData LiraData(List<FemElement> mesh, LiraAspFile asp, LiraRbtFile? rbt = null) => new()
    {
        SourceType = "lira", Mesh = mesh, Asp = asp, Rbt = rbt,
        Stiffnesses = new Dictionary<int, LiraStiffnessRecord> { [1] = LiraBrus(1, 30, 50) },
    };

    static Func<IReadOnlyList<ImportedBarRebarMode>, ImportedBarRebarMode?> Choose(ImportedBarRebarMode mode,
        List<IReadOnlyList<ImportedBarRebarMode>>? offered = null) => modes => { offered?.Add(modes); return mode; };

    [Fact]
    public void LiraSelected_CloseSelectionsShareSection_BarsInDefinition()
    {
        // КЭ 1 и 2 после округления вверх до 0,5 см² одинаковы (углы 1,5, низ 4,5); КЭ 3 — больше.
        var mesh = Mesh("lira", (1, 1, null), (2, 1, null), (3, 1, null));
        var asp = new LiraAspFile
        {
            Bars = new Dictionary<int, LiraAspBar>
            {
                [1] = AspBar(1, Areas(1.2, 3.1), Areas(0.4, 4.2)),
                [2] = AspBar(2, Areas(1.4, 4.4)),
                [3] = AspBar(3, Areas(1.4, 6.3)),
            },
        };
        var offered = new List<IReadOnlyList<ImportedBarRebarMode>>();

        var report = ImportedBarSectionCreator.Create(_db, LiraData(mesh, asp), catalogDirectory: CatalogDirectory(),
            chooseRebar: Choose(ImportedBarRebarMode.Selected, offered));

        Assert.Equal([ImportedBarRebarMode.None, ImportedBarRebarMode.Selected], Assert.Single(offered));
        Assert.Equal(ImportedBarRebarMode.Selected, report.RebarMode);
        Assert.Empty(report.Skipped);
        Assert.Empty(report.WithoutRebar);
        Assert.Equal(["Брус 300×500 B25 A500 ASP ΣAs 10,5 см²", "Брус 300×500 B25 A500 ASP ΣAs 12,5 см²"],
            report.Sections.Order());

        var ids = Reloaded().ToDictionary(e => e.ElemTag, e => e.CrossSectionId);
        Assert.Equal(ids["1"], ids["2"]);
        Assert.NotEqual(ids["1"], ids["3"]);

        var section = _db.CrossSections.Single(s => s.Id == ids["1"]);
        Assert.True(new ParametricRcSectionProjectService(_db).TryGetDefinition(section, out var definition));
        Assert.Equal(4 + 9, definition.ExtraBars.Count);      // 4 угла + 9 точек нижней грани
        var rebar = Assert.Single(section.Areas, a => a.Category == AreaCategory.RebarGroup);
        Assert.Equal(MatType.ReSteelF, rebar.Material!.Type);
        Assert.Equal(10.5e-4, rebar.Fibers.Sum(f => f.Area), 9);
    }

    [Fact]
    public void LiraAssigned_TypesOfElement_NoTypes_WithoutRebarWarning()
    {
        var mesh = Mesh("lira", (1, 1, "3"), (2, 1, null));
        var asp = new LiraAspFile { Bars = new Dictionary<int, LiraAspBar> { [1] = AspBar(1, Areas()), [2] = AspBar(2, Areas()) } };

        var report = ImportedBarSectionCreator.Create(_db, LiraData(mesh, asp, Rbt()), catalogDirectory: CatalogDirectory(),
            chooseRebar: Choose(ImportedBarRebarMode.Assigned));

        Assert.Equal(["Брус 300×500 B25 A500", "Брус 300×500 B25 A500 ТЗА 3 ΣAs 6,03 см²"], report.Sections.Order());
        var (reason, elements) = Assert.Single(report.WithoutRebar);
        Assert.Contains("не назначены ТЗА", reason);
        Assert.Equal([2], elements);
        Assert.Equal(2, report.AssignedElements);
    }

    [Fact]
    public void SecondRunWithRebar_ReplacesAutoRebarless_KeepsManual()
    {
        var mesh = Mesh("lira", (1, 1, "3"), (2, 1, "3"), (3, 1, "3"));
        var asp = new LiraAspFile
        {
            Bars = new Dictionary<int, LiraAspBar> { [1] = AspBar(1, Areas()), [2] = AspBar(2, Areas()), [3] = AspBar(3, Areas()) },
        };
        var data = LiraData(mesh, asp, Rbt());
        var first = ImportedBarSectionCreator.Create(_db, data, catalogDirectory: CatalogDirectory());
        Assert.Equal(ImportedBarRebarMode.None, first.RebarMode);
        var rebarless = Assert.Single(_db.CrossSections);

        // КЭ 3 вручную получает другое сечение — его не трогаем.
        var manual = new CrossSection { Num = 99, Tag = "Ручное" };
        _db.SaveCrossSection(manual);
        var reloaded = Reloaded();
        _db.SetFemElementCrossSections([(reloaded.Single(e => e.ElemTag == "3"), manual.Id)]);
        reloaded = Reloaded();
        foreach (var e in reloaded) { e.StiffnessNum = 1; e.ReinforcementTypeIds = "3"; }

        var second = ImportedBarSectionCreator.Create(_db, LiraData(reloaded, asp, Rbt()), catalogDirectory: CatalogDirectory(),
            chooseRebar: Choose(ImportedBarRebarMode.Assigned));

        Assert.Equal(2, second.Replaced);
        Assert.Equal(1, second.AlreadyAssigned);
        var ids = Reloaded().ToDictionary(e => e.ElemTag, e => e.CrossSectionId);
        Assert.NotEqual(rebarless.Id, ids["1"]);
        Assert.Equal(ids["1"], ids["2"]);
        Assert.Equal(manual.Id, ids["3"]);

        // Повторный запуск с тем же армированием ничего не меняет.
        reloaded = Reloaded();
        foreach (var e in reloaded) { e.StiffnessNum = 1; e.ReinforcementTypeIds = "3"; }
        var third = ImportedBarSectionCreator.Create(_db, LiraData(reloaded, asp, Rbt()), catalogDirectory: CatalogDirectory(),
            chooseRebar: Choose(ImportedBarRebarMode.Assigned));
        Assert.Empty(third.Sections);
        Assert.Equal(0, third.Replaced);
        Assert.Equal(3, third.AlreadyAssigned);
    }

    [Fact]
    public void Cancelled_NothingChanged_AndNoChoiceWithoutRebarData()
    {
        var mesh = Mesh("lira", (1, 1, null));
        var asp = new LiraAspFile { Bars = new Dictionary<int, LiraAspBar> { [1] = AspBar(1, Areas(1, 2)) } };

        var cancelled = ImportedBarSectionCreator.Create(_db, LiraData(mesh, asp), catalogDirectory: CatalogDirectory(),
            chooseRebar: _ => null);
        Assert.True(cancelled.Cancelled);
        Assert.Empty(_db.CrossSections);
        Assert.Empty(_db.Materials);
        Assert.All(Reloaded(), e => Assert.Null(e.CrossSectionId));

        // У SCAD-схемы без выгрузки и заданного армирования выбора нет — сечения без арматуры.
        var scadMesh = Mesh("scad", (814, 6, null));
        var data = new FemCheckSchemaData
        {
            SourceType = "scad", Mesh = scadMesh,
            ScadConcreteGroups = new ScadConcreteGroupIndex(
                [new ScadConcreteGroup(2, "колонны", 1, [0.04, 0.04, 0, 0], "B30", "A400", "A240", false, [0.4, 0.3], [814])]),
            Stiffnesses = new Dictionary<int, LiraStiffnessRecord>
            {
                [6] = new(6, ScadStiffnessParams.ScadKindCode, "Колонны", "S0 900000 40 60 NU 0.2", 0.01),
            },
        };
        bool asked = false;
        var report = ImportedBarSectionCreator.Create(_db, data, catalogDirectory: CatalogDirectory(),
            chooseRebar: _ => { asked = true; return ImportedBarRebarMode.Selected; });
        Assert.False(asked);
        Assert.Equal(["Брус 400×600 B30 A400"], report.Sections);
    }

    [Fact]
    public void Scad_SelectedEnvelope_AndAssignedStrongestPart()
    {
        var group = new ScadConcreteGroup(2, "ригели", 1, [0.05, 0.05, 0, 0], "B30", "A400", "A240", false, [0.4, 0.3], [814, 815]);
        ScadAssignedRodPart Part(int no, int n, int d) => new(no, 50,
            new ScadRodFace(new ScadBarSet(n, d), null, null, 0), new ScadRodFace(new ScadBarSet(2, 12), null, null, 0),
            null, null, null, null);
        FemCheckSchemaData Data(List<FemElement> mesh) => new()
        {
            SourceType = "scad", Mesh = mesh,
            ScadConcreteGroups = new ScadConcreteGroupIndex([group]),
            ScadSelected = new ScadSelectedRebarFile
            {
                Bars = new Dictionary<int, ScadSelectedBar>
                {
                    [814] = new(814, [new ScadSelectedBarSection(3.1, 1.0, 0, 0, null, null), new ScadSelectedBarSection(2.0, 2.2, 0, 0, null, null)]),
                },
            },
            ScadAssigned = new ScadAssignedRebarFile([], [new ScadAssignedRod(4, "Ригели", [814, 815], [Part(1, 2, 16), Part(2, 3, 25)])]),
            Stiffnesses = new Dictionary<int, LiraStiffnessRecord>
            {
                [6] = new(6, ScadStiffnessParams.ScadKindCode, "Ригели", "S0 900000 40 60 NU 0.2", 0.01),
            },
        };

        var mesh = Mesh("scad", (814, 6, null), (815, 6, null));
        var offered = new List<IReadOnlyList<ImportedBarRebarMode>>();
        var selected = ImportedBarSectionCreator.Create(_db, Data(mesh), catalogDirectory: CatalogDirectory(),
            chooseRebar: Choose(ImportedBarRebarMode.Selected, offered));
        Assert.Equal([ImportedBarRebarMode.None, ImportedBarRebarMode.Assigned, ImportedBarRebarMode.Selected], offered.Single());
        // 814: низ 3,5 + верх 2,5; 815 нет в выгрузке — без арматуры.
        Assert.Equal(["Брус 400×600 B30 A400", "Брус 400×600 B30 A400 подбор SCAD ΣAs 6 см²"], selected.Sections.Order());
        Assert.Contains("подборе SCAD", Assert.Single(selected.WithoutRebar).Reason);

        var mesh2 = Mesh("scad", (814, 6, null), (815, 6, null));
        var assigned = ImportedBarSectionCreator.Create(_db, Data(mesh2), catalogDirectory: CatalogDirectory(),
            chooseRebar: Choose(ImportedBarRebarMode.Assigned));
        var tag = Assert.Single(assigned.Sections);
        Assert.StartsWith("Брус 400×600 B30 A400 SCAD «Ригели» уч.2 ΣAs", tag);
        Assert.Equal(2, assigned.AssignedElements);
        var section = _db.CrossSections.Single(s => s.Tag == tag);
        Assert.Equal(3, section.Areas.Single(a => a.Category == AreaCategory.RebarGroup).Fibers.Count(f => Math.Abs(f.Diameter - 0.025) < 1e-9));
    }
}
