using CScore;
using CScore.Fem;
using CScore.Import;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Автосоздание материалов и пластинчатых сечений целей по данным ЛИРЫ (подбор ASP + фоновые ТЗА).</summary>
public class LiraPlateSectionCreatorTests
{
    static DatabaseService NewDb() => new(Path.Combine(Path.GetTempPath(),
        "opencs_lira_sections_" + Guid.NewGuid().ToString("N") + ".db"));

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS", "DataSource");
    }

    static FemElement Shell(int num, string typeIds, string? memberTag = null) =>
        new() { ElemTag = num.ToString(), ElemType = "shell", ReinforcementTypeIds = typeIds, SourceMemberTag = memberTag };

    static LiraPlateRebarLayer TzaLayer(LiraPlateRebarSlot slot) => new()
    {
        Slot = slot, A = 4,
        Terms = [new LiraPlateRebarTerm(1, 10, Math.PI * 100 / 400.0, 300)],
    };

    /// <summary>ТЗА 1 — фон ⌀10 шаг 300 у обеих граней; ТЗА 2 — усиление низа.</summary>
    static LiraRbtFile Rbt() => new()
    {
        PlateTypes = new Dictionary<int, LiraPlateReinforcementType>
        {
            [1] = new()
            {
                Id = 1, Kind = LiraRbtReader.KindPlateSimple, Binding = LiraRebarBinding.Centroid,
                Layers = [TzaLayer(LiraPlateRebarSlot.XT), TzaLayer(LiraPlateRebarSlot.XB),
                          TzaLayer(LiraPlateRebarSlot.YT), TzaLayer(LiraPlateRebarSlot.YB)],
            },
            [2] = new()
            {
                Id = 2, Kind = LiraRbtReader.KindPlateSimple, Binding = LiraRebarBinding.Centroid,
                Layers = [TzaLayer(LiraPlateRebarSlot.XB)],
            },
        },
    };

    static LiraAspFile Asp(string concrete, params int[] elements) => new()
    {
        Plates = elements.ToDictionary(n => n, n => new LiraAspPlate(n, 1, 1, 1, 1, 0, 0.2f, "A500", concrete)),
    };

    static LiraPlateNominalRebar? NoQuestions(LiraPlateNominalRebar suggested) =>
        throw new InvalidOperationException("Фон есть у обеих граней — запроса привязки быть не должно");

    // ── Справочник ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("A500 (6-40 мм)", "A500")]
    [InlineData("А500С", "A500")]          // кириллица
    [InlineData(" b25 ", "B25")]
    [InlineData("B22,5", "B22.5")]
    [InlineData("Вр500 (3-5 мм)", "BP500")]
    [InlineData("", "")]
    public void ClassKey_IgnoresDiameterRangeSuffixAndAlphabet(string tag, string expected) =>
        Assert.Equal(expected, MaterialCatalog.ClassKey(tag));

    [Fact]
    public void Catalog_CreatesConcreteAndRebarWithAllCalcTypes()
    {
        var concrete = MaterialCatalog.CreateHeavyConcrete("B25", CatalogDirectory())!;
        Assert.Equal((MatType.Concrete, "B25"), (concrete.Type, concrete.Tag));
        Assert.Equal(-14_500, concrete.C!.Fc);
        Assert.Equal(-18_500, concrete.N!.Fc);
        Assert.Equal(CalcType.NL, concrete.NL!.TypeCalc);
        Assert.Equal(0.6 * concrete.C.Fc / concrete.C.E, concrete.C.Ec1, 12);
        Assert.Equal(4, concrete.MaterialChars.Count);
        Assert.NotNull(concrete.GetDiagramms(DiagrammType.L3));

        var rebar = MaterialCatalog.CreateRebar("А500С", CatalogDirectory())!;
        Assert.Equal((MatType.ReSteelF, "A500 (6-40 мм)"), (rebar.Type, rebar.Tag));
        Assert.Equal(435_000, rebar.C!.Ft);
        Assert.Equal(500_000, rebar.N!.Ft);

        Assert.Null(MaterialCatalog.CreateHeavyConcrete("B27", CatalogDirectory()));
        Assert.Null(MaterialCatalog.CreateRebar("", CatalogDirectory()));
    }

    // ── Создание сечений ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_MakesMaterialsAndSectionOnce_AndAssignsToTargets()
    {
        using var db = NewDb();
        var schema = new FemSchema { Tag = "1-lin", SourceType = "lira" };
        db.SaveFemSchema(schema);
        var slab = new FemMemberGroup { SchemaId = schema.Id, Tag = "ПЛИТА №56", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[10,11]" };
        var other = new FemMemberGroup { SchemaId = schema.Id, Tag = "ПЛИТА №57", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[12]" };
        var bars = new FemMemberGroup { SchemaId = schema.Id, Tag = "Колонны", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[99]" };
        var data = new FemCheckSchemaData
        {
            SchemaId = schema.Id,
            Mesh = [Shell(10, "1"), Shell(11, "1 2"), Shell(12, "1"), new FemElement { ElemTag = "99", ElemType = "beam" }],
            Asp = Asp("B25", 10, 11, 12),
            Rbt = Rbt(),
        };

        var report = LiraPlateSectionCreator.Create(db, data, [slab, bars], NoQuestions, CatalogDirectory());

        Assert.Equal(["B25", "A500 (6-40 мм)"], report.Materials);
        Assert.Equal(["ЛИРА h200 B25 A500 · ТЗА 1"], report.Sections);
        Assert.Equal([("ПЛИТА №56", "ЛИРА h200 B25 A500 · ТЗА 1")], report.Assigned);
        Assert.Empty(report.Skipped);
        Assert.Null(bars.PlateSectionId);                  // стержневая цель не трогается

        var section = Assert.Single(db.PlateSections);
        Assert.Equal(section.Id, slab.PlateSectionId);
        Assert.Equal(0.2, section.H);
        Assert.Equal(db.Materials.Single(m => m.Type == MatType.Concrete).Id, section.ConcreteMaterialId);
        Assert.Equal(db.Materials.Single(m => m.Type == MatType.ReSteelF).Id, section.RebarMaterialId);
        Assert.Equal(2, section.RebarLayers.Count);
        Assert.True(slab.Id > 0);                          // группа сохранена вместе с назначением

        // Вторая цель с теми же данными: те же материалы и то же сечение; цель с сечением не трогается.
        var again = LiraPlateSectionCreator.Create(db, data, [slab, other], NoQuestions, CatalogDirectory());

        Assert.Empty(again.Materials);
        Assert.Empty(again.Sections);
        Assert.Equal([("ПЛИТА №57", section.Tag)], again.Assigned);
        Assert.Equal(section.Id, other.PlateSectionId);
        Assert.Equal(2, db.Materials.Count);
        Assert.Single(db.PlateSections);
    }

    [Fact]
    public void Create_EditedSectionWithSameName_IsNotReused()
    {
        using var db = NewDb();
        var schema = new FemSchema { Tag = "1-lin", SourceType = "lira" };
        db.SaveFemSchema(schema);
        var a = new FemMemberGroup { SchemaId = schema.Id, Tag = "А", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[10]" };
        var b = new FemMemberGroup { SchemaId = schema.Id, Tag = "Б", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[11]" };
        var data = new FemCheckSchemaData
        {
            SchemaId = schema.Id, Mesh = [Shell(10, "1"), Shell(11, "1")], Asp = Asp("B25", 10, 11), Rbt = Rbt(),
        };
        LiraPlateSectionCreator.Create(db, data, [a], NoQuestions, CatalogDirectory());
        var first = db.PlateSections.Single();
        first.RebarLayers[0].Asx *= 2;                     // пользователь поправил сечение
        db.SavePlateSection(first);

        var report = LiraPlateSectionCreator.Create(db, data, [b], NoQuestions, CatalogDirectory());

        Assert.Equal(["ЛИРА h200 B25 A500 · ТЗА 1 (2)"], report.Sections);
        Assert.NotEqual(first.Id, b.PlateSectionId);
    }

    [Fact]
    public void Create_ReportsWhatCannotBeDone()
    {
        using var db = NewDb();
        var schema = new FemSchema { Tag = "1-lin", SourceType = "lira" };
        db.SaveFemSchema(schema);
        var slab = new FemMemberGroup { SchemaId = schema.Id, Tag = "ПЛИТА", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[10]" };
        FemElement[] mesh = [Shell(10, "1")];

        Assert.True(LiraPlateSectionCreator.Create(db, new FemCheckSchemaData { SchemaId = schema.Id, Mesh = mesh },
            [slab], NoQuestions, CatalogDirectory()).NoAsp);

        // Класса бетона нет в справочнике — цель пропущена с причиной, сечение не создано.
        var unknown = LiraPlateSectionCreator.Create(db,
            new FemCheckSchemaData { SchemaId = schema.Id, Mesh = mesh, Asp = Asp("B27", 10), Rbt = Rbt() },
            [slab], NoQuestions, CatalogDirectory());
        Assert.Contains("B27", Assert.Single(unknown.Skipped).Reason);
        Assert.Empty(db.PlateSections);
        Assert.Null(slab.PlateSectionId);

        // ТЗА не приложены, от ввода привязки отказались — ничего не создано.
        var cancelled = LiraPlateSectionCreator.Create(db,
            new FemCheckSchemaData { SchemaId = schema.Id, Mesh = mesh, Asp = Asp("B25", 10) },
            [slab], _ => null, CatalogDirectory());
        Assert.True(cancelled.Cancelled);
        Assert.Empty(db.Materials);
    }
}
