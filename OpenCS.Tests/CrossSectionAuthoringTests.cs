using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using System.IO;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Служба сечений и областей пула: проверка, сохранение, удаление используемого, снимок для «*».</summary>
[Collection("Loc language")]
public sealed class CrossSectionAuthoringTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_section_{Guid.NewGuid():N}");
    public CrossSectionAuthoringTests()
    {
        Directory.CreateDirectory(_dir);
        Loc.SetLanguage("ru-RU");
    }
    public void Dispose()
    {
        Loc.SetLanguage(null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    static readonly CalcSettings Calc = new();

    /// <summary>Проект с B25, A500C и контуром 400×600 (центр в начале координат).</summary>
    (DatabaseService Db, Material Concrete, Material Rebar, Contour Rect) Project(string name)
    {
        var db = new DatabaseService(Path.Combine(_dir, name));
        var b25 = MaterialCatalog.CreateHeavyConcrete("B25")!;
        var a500 = MaterialCatalog.CreateRebar("A500C")!;
        Assert.Empty(MaterialAuthoring.Save(db, b25));
        Assert.Empty(MaterialAuthoring.Save(db, a500));
        var rect = ContourAuthoring.FromPoints([(-0.2, -0.3), (0.2, -0.3), (0.2, 0.3), (-0.2, 0.3)], "K", ContourType.Hull);
        Assert.Empty(ContourAuthoring.Save(db, rect));
        return (db, b25, a500, rect);
    }

    static MaterialArea Region(Material m, Contour hull, string tag = "Бетон")
    {
        var a = new MaterialArea { Tag = tag, Category = AreaCategory.Region, Material = m, MaterialId = m.Id };
        a.Hull = hull;
        a.PoolContour = hull;
        a.PoolContourId = hull.Id;
        a.SetWKT();
        return a;
    }

    static MaterialArea Bars(Material m, MaterialArea? host, string tag, params (double X, double Y, double D)[] bars) => new()
    {
        Tag = tag, Category = AreaCategory.RebarGroup, Material = m, MaterialId = m.Id, HostArea = host,
        Fibers = bars.Select(b => Fiber.CreatePoint(b.D, b.X, b.Y)).ToList()
    };

    [Fact]
    public void ValidateArea_MaterialKindContourBars()
    {
        var (db, b25, a500, rect) = Project("v.db");
        using var owner = db;

        var noMat = new MaterialArea { Tag = "X", Category = AreaCategory.Region };
        var errs = CrossSectionAuthoring.ValidateArea(noMat, db.Materials, "areas[0]");
        Assert.Contains(errs, e => e.Path == "areas[0].material");
        Assert.Contains(errs, e => e.Path == "areas[0].contour");

        var rebarAsRegion = Region(a500, rect);
        Assert.Contains(CrossSectionAuthoring.ValidateArea(rebarAsRegion, db.Materials), e => e.Path == "material");

        var concrete = Region(b25, rect);
        Assert.Empty(CrossSectionAuthoring.ValidateArea(concrete, db.Materials));

        var concreteAsBars = Bars(b25, concrete, "Низ", (0, -0.25, 0.025));
        Assert.Contains(CrossSectionAuthoring.ValidateArea(concreteAsBars, db.Materials), e => e.Path == "material");

        var bad = Bars(a500, concrete, "Низ", (0, -0.25, 0.025), (0.5, 0, 0.025), (0, 0, 0));
        errs = CrossSectionAuthoring.ValidateArea(bad, db.Materials);
        Assert.Contains(errs, e => e.Path == "bars[1]");      // вне бетона носителя
        Assert.Contains(errs, e => e.Path == "bars[2].d");
        Assert.DoesNotContain(errs, e => e.Path == "bars[0]");

        var empty = Bars(a500, null, "Пусто");
        Assert.Contains(CrossSectionAuthoring.ValidateArea(empty, db.Materials), e => e.Path == "bars");

        var stirrups = new MaterialArea { Tag = "Х", Category = AreaCategory.Stirrups, Material = a500, MaterialId = a500.Id };
        Assert.Contains(CrossSectionAuthoring.ValidateArea(stirrups, db.Materials), e => e.Path == "stirrups");
        stirrups.Stirrups.Add(new StirrupGroup { SpacingM = 0, Elements = [Loop(0.01)] });
        errs = CrossSectionAuthoring.ValidateArea(stirrups, db.Materials);
        Assert.Contains(errs, e => e.Path == "stirrups[0].spacing");
        stirrups.Stirrups[0].SpacingM = 0.15;
        stirrups.Stirrups[0].Elements[0].BarDiameterM = -1;
        Assert.Contains(CrossSectionAuthoring.ValidateArea(stirrups, db.Materials), e => e.Path == "stirrups[0].elements[0].d");
    }

    static StirrupElement Loop(double d)
    {
        var line = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15], [-0.25, -0.25, 0.25, 0.25, -0.25], "loop");
        return new StirrupElement { CenterlineContour = line, BarDiameterM = d, BarAreaM2 = Math.PI * d * d / 4 };
    }

    [Fact]
    public void ValidateSection_EmptyDuplicateHostOutside()
    {
        var (db, b25, a500, rect) = Project("s.db");
        using var owner = db;
        var empty = new CrossSection { Tag = "С" };
        Assert.Contains(CrossSectionAuthoring.Validate(empty, db.Materials, []), e => e.Path == "areas");

        var concrete = Region(b25, rect);
        var other = Region(b25, rect, "Бетон");
        var bars = Bars(a500, concrete, "Низ", (0, -0.25, 0.025));
        var s = new CrossSection { Tag = "С", Areas = [other, bars] };
        var errs = CrossSectionAuthoring.Validate(s, db.Materials, []);
        Assert.Contains(errs, e => e.Path == "areas[1].host");
        s.Areas = [concrete, other, bars];
        Assert.Contains(CrossSectionAuthoring.Validate(s, db.Materials, []), e => e.Path == "areas[1].tag");
    }

    [Fact]
    public void Save_InvalidWritesNothing_ValidWithHost_Reload_BarCircles()
    {
        string path = Path.Combine(_dir, "save.db");
        var (db, b25, a500, rect) = Project("save.db");
        int sectionId, concreteId;
        using (db)
        {
            var concrete = Region(b25, rect);
            var bars = Bars(a500, concrete, "Низ", (-0.15, -0.25, 0.025), (0.15, -0.25, 0.025));
            var bad = new CrossSection { Tag = "", Areas = [concrete, bars] };
            Assert.Contains(CrossSectionAuthoring.Save(db, bad, Calc, [concrete, bars]), e => e.Path == "tag");
            Assert.Equal(0, concrete.Id);
            Assert.Empty(db.CrossSections);
            Assert.Empty(db.MaterialAreas);

            var s = new CrossSection { Tag = "Б-1", Areas = [concrete, bars] };
            Assert.Empty(CrossSectionAuthoring.Save(db, s, Calc, [concrete, bars]));
            Assert.True(s.Id > 0 && concrete.Id > 0 && bars.Id > 0);
            Assert.Equal(concrete.Id, bars.HostAreaId);
            Assert.Equal(2, db.MaterialAreas.Count);
            Assert.Equal(2, db.Circles.Count(c => c.GeometrySet == CrossSectionAuthoring.BarCircleSet(bars)));
            Assert.NotEmpty(bars.Diagramms);
            sectionId = s.Id;
            concreteId = concrete.Id;
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var again = new DatabaseService(path);
        again.LoadAll();
        var sec = Assert.Single(again.CrossSections);
        Assert.Equal(sectionId, sec.Id);
        Assert.Equal(["Бетон", "Низ"], sec.Areas.Select(a => a.Tag));
        Assert.Equal(concreteId, sec.Areas[1].HostAreaId);
        Assert.Equal(0.24, Math.Abs(new GeoProps(sec.Areas[0].Hull!).A), 9);
    }

    [Fact]
    public void TryDelete_UsedAreaAndSection_Refused()
    {
        var (db, b25, a500, rect) = Project("d.db");
        using var owner = db;
        var concrete = Region(b25, rect);
        var bars = Bars(a500, concrete, "Низ", (0, -0.25, 0.025));
        var s = new CrossSection { Tag = "Б-1", Areas = [concrete, bars] };
        Assert.Empty(CrossSectionAuthoring.Save(db, s, Calc, [concrete, bars]));
        var staged = new StagedSection { Tag = "Стадии" };
        staged.Parts.Add(new SectionPart { Tag = "Сборная", SourceSectionId = s.Id });
        Assert.Empty(CrossSectionAuthoring.Save(db, staged, Calc));

        Assert.False(CrossSectionAuthoring.TryDeleteArea(db, concrete, out var usedBy));
        Assert.Contains(usedBy, u => u.Contains("Б-1"));
        Assert.Contains(usedBy, u => u.Contains("Низ"));       // носитель группы
        Assert.False(CrossSectionAuthoring.TryDeleteSection(db, s, out usedBy));
        Assert.Contains(usedBy, u => u.Contains("Стадии"));
        Assert.Equal(2, db.CrossSections.Count);

        Assert.True(CrossSectionAuthoring.TryDeleteSection(db, staged, out _));
        Assert.True(CrossSectionAuthoring.TryDeleteSection(db, s, out _));
        Assert.True(CrossSectionAuthoring.TryDeleteArea(db, bars, out _));
        Assert.Empty(db.Circles);                                // круги группы удалены вместе с ней
        Assert.True(CrossSectionAuthoring.TryDeleteArea(db, concrete, out _));
        Assert.Empty(db.MaterialAreas);
    }

    [Fact]
    public void Staged_PartChecks()
    {
        var (db, b25, a500, rect) = Project("st.db");
        using var owner = db;
        var staged = new StagedSection { Tag = "Ст" };
        staged.Parts.Add(new SectionPart { Tag = "", SourceSectionId = 999 });
        staged.Parts.Add(new SectionPart { Tag = "Своя" });
        var errs = CrossSectionAuthoring.Validate(staged, db.Materials, db.CrossSections);
        Assert.Contains(errs, e => e.Path == "staged.parts[0].tag");
        Assert.Contains(errs, e => e.Path == "staged.parts[0].section");
        Assert.Contains(errs, e => e.Path == "staged.parts[1].areas");
    }

    [Fact]
    public void AreaSnapshot_Matches_RestoresEditedFields()
    {
        var (db, b25, a500, rect) = Project("snap.db");
        using var owner = db;
        var concrete = Region(b25, rect);
        var bars = Bars(a500, concrete, "Низ", (0, -0.25, 0.025));
        bars.SigSp = 0;
        var snap = AreaSnapshot.Capture(bars);
        Assert.True(snap.Matches(bars));

        bars.Tag = "Верх";
        bars.SigSp = 500;
        bars.Fibers.Add(Fiber.CreatePoint(0.012, 0.1, 0.25));
        bars.HostArea = null;
        Assert.False(snap.Matches(bars));

        snap.RestoreTo(bars, db.Diagrams, Calc);
        Assert.True(snap.Matches(bars));
        Assert.Equal("Низ", bars.Tag);
        Assert.Equal(0, bars.SigSp);
        Assert.Single(bars.Fibers);
        Assert.Same(concrete, bars.HostArea);
    }
}
