using System.Reflection;
using System.Text.Json;
using CScore;
using CScore.Sp16;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Служба расчётных задач: требования видов (сечение, набор, строка, тип расчёта), строгая проверка параметров
/// (неизвестное поле и неверный тип — ошибка, а не молчаливое умолчание), диапазоны, номер, копия, правка с результатами.
/// </summary>
[Collection("Loc language")]
public sealed class CalcTaskAuthoringTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_tasks_{Guid.NewGuid():N}");
    public CalcTaskAuthoringTests()
    {
        Directory.CreateDirectory(_dir);
        Loc.SetLanguage("ru-RU");
    }
    public void Dispose()
    {
        Loc.SetLanguage(null);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    DatabaseService NewDb(string name) => new(Path.Combine(_dir, name));

    sealed record Project(DatabaseService Db, CrossSection Section, PlateSection Plate, ForceSet Bar, ForceSet Shell);

    Project Seed(string name)
    {
        var db = NewDb(name);
        var section = new CrossSection { Tag = "К1" };
        db.SaveCrossSection(section);
        db.CrossSections.Add(section);
        var plate = new PlateSection { Tag = "Плита", H = 0.2 };
        db.SavePlateSection(plate);
        if (!db.PlateSections.Contains(plate)) db.PlateSections.Add(plate);
        var bar = ForceSetAuthoringTests.Bar();
        Assert.Empty(ForceSetAuthoring.Save(db, bar));
        var shell = ForceSetAuthoringTests.Shell();
        Assert.Empty(ForceSetAuthoring.Save(db, shell));
        return new Project(db, section, plate, bar, shell);
    }

    static void NoErrors(IReadOnlyList<FieldError> errors) =>
        Assert.True(errors.Count == 0, string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));

    static CalcTask Task(string kind, int section, string json = "{}", int set = 0, int item = 0, CalcType type = CalcType.C) =>
        new() { Tag = "Т", Kind = kind, SectionId = section, ForceSetId = set, ForceItemId = item, CalcType = type, ParamsJson = json };

    [Fact]
    public void Kinds_CoverRunner()
    {
        foreach (var kind in TaskRunner.KindList)
            Assert.True(CalcTaskKinds.Get(kind) != null, kind);
        Assert.NotNull(CalcTaskKinds.Get("steel_check_batch"));
    }

    /// <summary>
    /// JSON, который пишут классы параметров (ToJson), проходит строгую проверку формы: имена полей совпадают с теми,
    /// что читает расчёт, — иначе служба отвергала бы задачи, созданные окном.
    /// </summary>
    [Fact]
    public void ParamsWrittenByClasses_PassShapeCheck()
    {
        foreach (var kind in CalcTaskKinds.Names)
        {
            var spec = CalcTaskKinds.Get(kind)!;
            foreach (var pt in spec.ParamTypes)
            {
                string json = Sample(pt.Type);
                var errors = CalcTaskAuthoring.ValidateParamsShape(kind, json);
                Assert.True(errors.Count == 0, $"{kind}/{pt.Type.Name}: {string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"))}\n{json}");
            }
        }
    }

    /// <summary>JSON класса по умолчанию — его ToJson (если есть) или сериализация; у η — с включённым η.</summary>
    static string Sample(Type t)
    {
        if (t == typeof(LimitForceParams))
            return new LimitForceParams { N = -100, Mx = 10, EtaEnabled = true, EtaL = 3 }.ToJson();
        object o = Activator.CreateInstance(t)!;
        var toJson = t.GetMethod("ToJson", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
        return toJson != null ? (string)toJson.Invoke(o, null)! : JsonSerializer.Serialize(o, t);
    }

    [Fact]
    public void Params_UnknownField_And_WrongType_AreErrors()
    {
        var unknown = CalcTaskAuthoring.ValidateParams("strain_state", """{"N":-100,"EtaL2":3}""");
        Assert.Contains(unknown, e => e.Path == "params.EtaL2");

        var wrong = CalcTaskAuthoring.ValidateParams("strain_state", """{"N":"12,5"}""");
        Assert.Contains(wrong, e => e.Path.StartsWith("params", StringComparison.Ordinal));

        var shell = CalcTaskAuthoring.ValidateParams("shell_simpl_wa_uls", """{"mx":"abc"}""");
        Assert.Contains(shell, e => e.Path == "params.mx");

        Assert.Empty(CalcTaskAuthoring.ValidateParams("strain_state", """{"N":-100,"Mx":12.5,"My":0,"batchSource":{"taskId":3,"num":2}}"""));
        Assert.Empty(CalcTaskAuthoring.ValidateParams("punching", "{}"));
    }

    [Fact]
    public void Params_Ranges()
    {
        Assert.Contains(CalcTaskAuthoring.ValidateParams("strain_state", """{"etaEnabled":true,"etaL":0}"""), e => e.Path == "params.etaL");
        Assert.Contains(CalcTaskAuthoring.ValidateParams("crack_width",
            new CrackWidthTaskParams { AcrcUltLong = 0.3, AcrcUltShort = 0.4, Phi2 = 0.5, ForcesMode = "share", LongShare = 1.5 }.ToJson()),
            e => e.Path == "params.LongShare");
        Assert.Contains(CalcTaskAuthoring.ValidateParams("torsion_fem", new TorsionParams { ElementSize = 0 }.ToJson()),
            e => e.Path == "params.element_size");
        Assert.Contains(CalcTaskAuthoring.ValidateParams("steel_check", (new SteelDesignParams() with { LefX = 0 }).ToJson()),
            e => e.Path == "params.LefX");
        Assert.Contains(CalcTaskAuthoring.ValidateParams("shell_simpl_wa_sls", System.Text.Json.JsonSerializer.Serialize(new ShellSimplParams { Phi2 = -1 })),
            e => e.Path == "params.phi2");
    }

    [Fact]
    public void Validate_RefsByKind()
    {
        var p = Seed("refs.db");
        using var db = p.Db;
        int row = p.Bar.Items[1].Id;
        NoErrors(CalcTaskAuthoring.Validate(db, Task("limit_force", p.Section.Id, "{}", p.Bar.Id, row)));
        NoErrors(CalcTaskAuthoring.Validate(db, Task("strain_state_batch", p.Section.Id, "{}", p.Bar.Id)));
        NoErrors(CalcTaskAuthoring.Validate(db, Task("shell_simpl_wa_uls_batch", p.Plate.Id, "{}", p.Shell.Id)));

        Assert.Contains(CalcTaskAuthoring.Validate(db, Task("strain_state_batch", p.Section.Id)), e => e.Path == "forceSet");
        Assert.Contains(CalcTaskAuthoring.Validate(db, Task("strain_state_batch", p.Section.Id, "{}", p.Shell.Id)), e => e.Path == "forceSet");
        Assert.Contains(CalcTaskAuthoring.Validate(db, Task("strain_state_batch", p.Section.Id, "{}", p.Bar.Id, row)), e => e.Path == "row");
        Assert.Contains(CalcTaskAuthoring.Validate(db, Task("limit_force", p.Section.Id, "{}", p.Bar.Id, 99999)), e => e.Path == "row");
        Assert.Contains(CalcTaskAuthoring.Validate(db, Task("shell_strain_state", 4242)), e => e.Path == "section");
        Assert.Contains(CalcTaskAuthoring.Validate(db, Task("load_history", p.Section.Id)), e => e.Path == "section");
        Assert.Contains(CalcTaskAuthoring.Validate(db, Task("punching", p.Section.Id)), e => e.Path == "section");
        Assert.Contains(CalcTaskAuthoring.Validate(db, Task("cracking", p.Section.Id)), e => e.Path == "calcType");
        Assert.Contains(CalcTaskAuthoring.Validate(db, Task("rc_check", p.Section.Id, "{}", p.Bar.Id)), e => e.Path == "row");
        Assert.Contains(CalcTaskAuthoring.Validate(db, Task("nope", p.Section.Id)), e => e.Path == "kind");
        var noName = Task("strain_state", p.Section.Id);
        noName.Tag = " ";
        Assert.Contains(CalcTaskAuthoring.Validate(db, noName), e => e.Path == "tag");
    }

    [Fact]
    public void RowRequired_DependsOnParams()
    {
        var steelNoForces = Task("steel_bending", 1, new SteelDesignParams().ToJson());
        Assert.True(CalcTaskAuthoring.RowRequired(steelNoForces));
        var steelManual = Task("steel_bending", 1, (new SteelDesignParams() with { ManualForces = new SteelManualForces { Mx = 10 } }).ToJson());
        Assert.False(CalcTaskAuthoring.RowRequired(steelManual));
        Assert.False(CalcTaskAuthoring.RowRequired(Task("steel_constructive", 1, new SteelDesignParams().ToJson())));
        Assert.Contains(CalcTaskAuthoring.ValidateFields(steelNoForces), e => e.Path == "row");
    }

    [Fact]
    public void Save_Numbering_Duplicate_Update()
    {
        var p = Seed("save.db");
        using var db = p.Db;
        var a = Task("strain_state", p.Section.Id, """{"N":-100,"Mx":12.5,"My":0}""");
        NoErrors(CalcTaskAuthoring.Save(db, a));
        Assert.Equal(1, a.Num);
        Assert.Equal("Задача 2", CalcTaskAuthoring.DefaultTag("strain_state", CalcTaskAuthoring.NextNum(db)));

        var bad = Task("strain_state", p.Section.Id, """{"N":"abc"}""");
        Assert.NotEmpty(CalcTaskAuthoring.Save(db, bad));
        Assert.Equal(0, bad.Id);

        db.SaveCalcResult(new CalcResult { TaskId = a.Id, TaskKind = a.Kind, TaskTag = a.Tag, Created = "2026-11-24 12:00:00" });
        Assert.Equal(1, CalcTaskAuthoring.ResultCount(db, a));

        var copy = CalcTaskAuthoring.Duplicate(db, a);
        Assert.Equal("Т (копия)", copy.Tag);
        Assert.Equal(2, copy.Num);
        Assert.Equal(a.ParamsJson, copy.ParamsJson);
        Assert.Equal(0, CalcTaskAuthoring.ResultCount(db, copy));

        // Только имя — результаты остаются и при «удалить устаревшие».
        var renamed = new CalcTask(); CalcTaskAuthoring.CopyInto(a, renamed); renamed.Tag = "Новое имя";
        Assert.False(CalcTaskAuthoring.InputsChanged(a, renamed));
        NoErrors(CalcTaskAuthoring.Update(db, a, renamed, deleteResults: true));
        Assert.Equal(1, CalcTaskAuthoring.ResultCount(db, a));

        // Входные данные изменились, результаты оставить.
        var edited = new CalcTask(); CalcTaskAuthoring.CopyInto(a, edited); edited.ParamsJson = """{"N":-200,"Mx":12.5,"My":0}""";
        Assert.True(CalcTaskAuthoring.InputsChanged(a, edited));
        NoErrors(CalcTaskAuthoring.Update(db, a, edited, deleteResults: false));
        Assert.Equal(1, CalcTaskAuthoring.ResultCount(db, a));

        // И удалить.
        var edited2 = new CalcTask(); CalcTaskAuthoring.CopyInto(a, edited2); edited2.ParamsJson = """{"N":-300,"Mx":12.5,"My":0}""";
        NoErrors(CalcTaskAuthoring.Update(db, a, edited2, deleteResults: true));
        Assert.Equal(0, CalcTaskAuthoring.ResultCount(db, a));
        Assert.Contains("-300", a.ParamsJson);

        // Неверная правка не меняет задачу.
        var broken = new CalcTask(); CalcTaskAuthoring.CopyInto(a, broken); broken.ParamsJson = """{"N":"x"}""";
        Assert.NotEmpty(CalcTaskAuthoring.Update(db, a, broken, deleteResults: true));
        Assert.Contains("-300", a.ParamsJson);

        // JSON с другим порядком полей — те же входные данные.
        var reordered = new CalcTask(); CalcTaskAuthoring.CopyInto(a, reordered); reordered.ParamsJson = """{"My":0,"Mx":12.5,"N":-300}""";
        Assert.False(CalcTaskAuthoring.InputsChanged(a, reordered));
    }

    /// <summary>Длительный набор ширины раскрытия (ForceSetLongId) — тоже ссылка: удалить такой набор нельзя.</summary>
    [Fact]
    public void CrackWidthLongSet_CountsAsUsage()
    {
        var p = Seed("long.db");
        using var db = p.Db;
        var json = new CrackWidthTaskParams { AcrcUltLong = 0.3, AcrcUltShort = 0.4, Phi2 = 0.5, ForcesMode = "force_item_long",
            LongShare = 1, ForceSetLongId = p.Bar.Id, ForceItemLongId = p.Bar.Items[0].Id }.ToJson();
        var t = Task("crack_width", p.Section.Id, json, type: CalcType.N);
        NoErrors(CalcTaskAuthoring.Save(db, t));
        var usage = db.ForceSetReferences(p.Bar.Id);
        Assert.Contains("Т", usage.Tasks);
        Assert.Contains(p.Bar.Items[0].Id, usage.ItemIds);
    }
}
