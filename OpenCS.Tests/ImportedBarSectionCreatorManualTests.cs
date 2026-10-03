using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон создания сечений стержней на реальном проекте с импортированными схемами ЛИРЫ/SCAD.
/// OPENCS_BAR_SECTIONS_DB — путь к проекту (*.db); работа идёт на временной копии, исходный файл не меняется.
/// Без переменной тест сразу выходит.
/// </summary>
public class ImportedBarSectionCreatorManualTests(ITestOutputHelper output)
{
    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS", "DataSource");
    }

    [Fact]
    public void CreateOnRealProject()
    {
        string? source = Environment.GetEnvironmentVariable("OPENCS_BAR_SECTIONS_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        string copy = Path.Combine(Path.GetTempPath(), "opencs_bar_sections_manual_" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(source, copy);
        var db = new DatabaseService(copy);
        try
        {
            db.LoadAll();
            output.WriteLine($"Схем: {db.FemSchemas.Count} — " + string.Join(", ", db.FemSchemas.Select(s => $"«{s.Tag}»:{s.SourceType}")));
            // OPENCS_SCAD_PRF_DIR — как «Обновить данные схемы из .SPR»: профили STZ по сортаментам SCAD.
            string? prfDir = Environment.GetEnvironmentVariable("OPENCS_SCAD_PRF_DIR");
            foreach (var schema in db.FemSchemas.Where(s => s.SourceType is "lira" or "scad").ToList())
            {
                if (schema.SourceType == "scad" && !string.IsNullOrWhiteSpace(prfDir))
                {
                    var entries = OpenCS.Services.Scad.ScadSteelProfileLoader.Resolve(db.GetFemSchemaStiffnesses(schema.Id).Values, prfDir);
                    if (entries.Count > 0)
                        db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadSteelProfiles, "",
                            System.Text.Encoding.UTF8.GetBytes(CScore.Import.SteelProfileIndex.ToJson(entries)));
                    foreach (var e in entries)
                        output.WriteLine($"  STZ {e.Num} ({e.Source}): {(e.Shape is { } s ? $"{s.Name} {s.Standard}, A={s.ACm2} см², Iy={s.IyCm4}, Iz={s.IzCm4} см⁴" : e.Reason)}");
                }
                // OPENCS_SCAD_STEEL_SPR — стальные группы из .SPR (как «Обновить данные схемы из .SPR»).
                if (schema.SourceType == "scad" && Environment.GetEnvironmentVariable("OPENCS_SCAD_STEEL_SPR") is { Length: > 0 } spr)
                {
                    string dll = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR")
                                 ?? OpenCS.Services.Scad.ScadInstallLocator.FindDllDirectory()!;
                    using var session = new OpenCS.Services.Scad.ScadApiSession(OpenCS.Services.Scad.ScadApiNative.Load(dll));
                    session.Open(spr);
                    var groups = OpenCS.Services.Scad.ScadApiReader.ReadSteelGroups(session);
                    db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadSteelGroups, "",
                        System.Text.Encoding.UTF8.GetBytes(CScore.Import.ScadSteelGroupIndex.ToJson(groups)));
                    output.WriteLine($"  стальных групп SCAD: {groups.Count}");
                    // Сечения КЭ стальных групп (назначенные прежним выбором стали) — снять, чтобы сталь взялась по марке.
                    var steelIds = groups.SelectMany(g => g.ElementIds).Select(id => id.ToString()).ToHashSet();
                    db.SetFemElementCrossSections([.. db.GetFemMeshElements(schema.Id)
                        .Where(e => steelIds.Contains(e.ElemTag)).Select(e => (e, (int?)null))]);
                }
                var data = FemCheckSchemaData.Load(db, schema.Id);
                int bars =data.Mesh.Count(e => e.ElemType != "shell");
                output.WriteLine($"=== Схема «{schema.Tag}» ({schema.SourceType}): КЭ {data.Mesh.Count}, стержней {bars}, " +
                                 $"жёсткостей {data.Stiffnesses.Count}, ASP {(data.Asp != null ? "есть" : "нет")}, " +
                                 $"ЖБ-групп SCAD {(data.ScadConcreteGroups != null ? "есть" : "нет")}");
                foreach (string e in data.Errors) output.WriteLine("  ошибка: " + e);

                int sectionsBefore = db.CrossSections.Count;
                var steel = CScore.MatType.Steel;
                CScore.Material? Steel() => db.Materials.FirstOrDefault(m => m.Type == steel)
                                            ?? MaterialCatalog.CreateStructuralSteel("С245", CatalogDirectory());
                var sortament = new ProfileDB(Path.Combine(CatalogDirectory(), "Sortamenty.db3"));
                var r = ImportedBarSectionCreator.Create(db, data, catalogDirectory: CatalogDirectory(),
                    chooseSteel: Steel, steelCatalog: sortament);
                Print(r);
                Assert.Equal(sectionsBefore + r.Sections.Count, db.CrossSections.Count);

                // Назначения записаны в БД.
                var saved = db.GetFemMeshElements(schema.Id).Where(e => e.ElemType != "shell").ToList();
                int withSection = saved.Count(e => e.CrossSectionId != null);
                output.WriteLine($"  в БД стержней с сечением: {withSection} из {saved.Count}");

                // Параметры СП 16 проверки по КЭ из стальных групп — по одному КЭ на группу.
                if (data.ScadSteelGroups is { } steelGroups)
                {
                    var scope = new FemCheckScope([], [.. data.Mesh.Where(e => e.ElemType != "shell")
                        .Select(e => new FemCheckScopeElement(int.Parse(e.ElemTag), e, null))], RefersToMeshElements: true);
                    var (paramsOf, warnings) = FemCheckContext.SteelGroupParams(data, scope);
                    foreach (string w in warnings) output.WriteLine("  проверка: " + w);
                    foreach (var g in steelGroups.Groups)
                    {
                        var e = scope.Elements.First(x => x.ElemNum == g.ElementIds[0]);
                        var p = CScore.Sp16.SteelDesignParams.Parse(paramsOf!(e, new CScore.Sp16.SteelDesignParams().ToJson()));
                        var section = db.CrossSections.Single(s => s.Id == db.GetFemMeshElements(schema.Id)
                            .Single(x => x.ElemTag == e.Element.ElemTag).CrossSectionId);
                        output.WriteLine($"  КЭ {e.ElemNum} ({g.Name}): γc {p.GammaC}, lef,x {p.LefX:0.###}, lef,y {p.LefY:0.###}, " +
                                         $"lef,b {p.LefB:0.###}, λu {p.CompressionLimit}, сечение «{section.Tag}», " +
                                         $"сталь {section.Areas[0].Material!.Tag} Ry(N) {section.Areas[0].Material!.N!.Ry}");
                    }
                }

                // Повторный запуск — без новых сечений и материалов.
                var again = ImportedBarSectionCreator.Create(db, FemCheckSchemaData.Load(db, schema.Id), catalogDirectory: CatalogDirectory(),
                    chooseSteel: Steel, steelCatalog: sortament);
                output.WriteLine($"  повторно: сечений {again.Sections.Count}, материалов {again.Materials.Count}, " +
                                 $"назначено {again.AssignedElements}, уже с сечением {again.AlreadyAssigned}");
                Assert.Empty(again.Sections);
                Assert.Empty(again.Materials);
                Assert.Equal(withSection, again.AlreadyAssigned);
            }
        }
        finally
        {
            db.Dispose();
            try { File.Delete(copy); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Схема из запущенной ЛИРЫ (как «Импорт схемы из ЛИРЫ (API)») во временный проект: стальные профили вида 1018 по
    /// сортаментам установленной ЛИРЫ, затем создание сечений. OPENCS_LIRA_STEEL_E2E=1 — запуск.
    /// </summary>
    [Fact]
    public void LiraApi_SteelProfiles_Sections()
    {
        if (Environment.GetEnvironmentVariable("OPENCS_LIRA_STEEL_E2E") != "1") return;
        CScore.Import.LiraSchemaData? raw = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { raw = LiraApiSchemaReader.Read(out _, out _); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;

        string path = Path.Combine(Path.GetTempPath(), "opencs_lira_steel_e2e_" + Guid.NewGuid().ToString("N") + ".db");
        var db = new DatabaseService(path);
        try
        {
            var schema = new FemSchema { Tag = "ЛИРА (API)", SourceType = "lira" };
            db.SaveFemSchema(schema);
            db.SaveFemMeshSnapshot(schema.Id, CScore.Import.LiraSchemaConverter.ToFemMeshNodes(raw!, schema.Id),
                [.. CScore.Import.LiraSchemaConverter.ToFemMeshBarElements(raw!, schema.Id)]);
            db.SaveFemSchemaStiffnesses(schema.Id, raw!.Stiffnesses);
            var entries = LiraSteelProfileLoader.Resolve(raw.Stiffnesses, LiraSteelProfileLoader.SortamentDirectories());
            db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.LiraSteelProfiles, "",
                System.Text.Encoding.UTF8.GetBytes(CScore.Import.SteelProfileIndex.ToJson(entries)));
            foreach (var e in entries)
                output.WriteLine($"  жёсткость {e.Num} ({e.Source}): {(e.Shape is { } s ? $"{s.Kind} {s.Name} {s.Standard}, H {s.H} B {s.B} t {s.Tw} R1 {s.R1}, A={s.ACm2} см², Iy={s.IyCm4} см⁴" : e.Reason)}");

            var data = FemCheckSchemaData.Load(db, schema.Id);
            output.WriteLine($"=== КЭ {data.Mesh.Count}, жёсткостей {data.Stiffnesses.Count}, профилей {data.SteelProfiles?.Entries.Count}");
            var sortament = new ProfileDB(Path.Combine(CatalogDirectory(), "Sortamenty.db3"));
            var r = ImportedBarSectionCreator.Create(db, data, catalogDirectory: CatalogDirectory(),
                chooseSteel: () => MaterialCatalog.CreateStructuralSteel("С245", CatalogDirectory()), steelCatalog: sortament);
            Print(r);
            foreach (var s in db.CrossSections.Where(s => s.ParametricSteel != null))
                output.WriteLine($"  МК «{s.Tag}»: {s.ParametricSteel!.Definition.Kind}, каталог {(s.ParametricSteel.Definition.Catalog != null ? "есть" : "нет")}");
            Assert.Equal(entries.Count(e => e.Shape != null), entries.Count);
            Assert.NotEmpty(r.SteelSections);
        }
        finally
        {
            db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    void Print(ImportedBarSectionsReport r)
    {
        if (r.NoMaterialData) { output.WriteLine("  нет данных о классах материалов"); return; }
        output.WriteLine($"  материалы: {string.Join(", ", r.Materials)}");
        foreach (var (section, count) in r.Assigned)
            output.WriteLine($"  {section}: КЭ {count}{(r.Reused.Contains(section) ? " (существующее)" : "")}");
        output.WriteLine($"  назначено {r.AssignedElements}, уже с сечением {r.AlreadyAssigned}");
        foreach (string w in r.Warnings) output.WriteLine("  предупреждение: " + w);
        foreach (var (reason, elements) in r.Skipped)
            output.WriteLine($"  пропущено {elements.Count} ({FemCheckReadiness.FormatRanges(elements, 6)}): {reason}");
    }
}
