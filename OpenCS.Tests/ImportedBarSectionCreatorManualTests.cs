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
            foreach (var schema in db.FemSchemas.Where(s => s.SourceType is "lira" or "scad").ToList())
            {
                var data = FemCheckSchemaData.Load(db, schema.Id);
                int bars = data.Mesh.Count(e => e.ElemType != "shell");
                output.WriteLine($"=== Схема «{schema.Tag}» ({schema.SourceType}): КЭ {data.Mesh.Count}, стержней {bars}, " +
                                 $"жёсткостей {data.Stiffnesses.Count}, ASP {(data.Asp != null ? "есть" : "нет")}, " +
                                 $"ЖБ-групп SCAD {(data.ScadConcreteGroups != null ? "есть" : "нет")}");
                foreach (string e in data.Errors) output.WriteLine("  ошибка: " + e);

                int sectionsBefore = db.CrossSections.Count;
                var r = ImportedBarSectionCreator.Create(db, data, catalogDirectory: CatalogDirectory());
                Print(r);
                Assert.Equal(sectionsBefore + r.Sections.Count, db.CrossSections.Count);

                // Назначения записаны в БД.
                var saved = db.GetFemMeshElements(schema.Id).Where(e => e.ElemType != "shell").ToList();
                int withSection = saved.Count(e => e.CrossSectionId != null);
                output.WriteLine($"  в БД стержней с сечением: {withSection} из {saved.Count}");

                // Повторный запуск — без новых сечений и материалов.
                var again = ImportedBarSectionCreator.Create(db, FemCheckSchemaData.Load(db, schema.Id), catalogDirectory: CatalogDirectory());
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

    void Print(ImportedBarSectionsReport r)
    {
        if (r.NoMaterialData) { output.WriteLine("  нет данных о классах материалов"); return; }
        output.WriteLine($"  материалы: {string.Join(", ", r.Materials)}");
        foreach (var (section, count) in r.Assigned)
            output.WriteLine($"  {section}: КЭ {count}{(r.Reused.Contains(section) ? " (существующее)" : "")}");
        output.WriteLine($"  назначено {r.AssignedElements}, уже с сечением {r.AlreadyAssigned}");
        foreach (var (reason, elements) in r.Skipped)
            output.WriteLine($"  пропущено {elements.Count} ({FemCheckReadiness.FormatRanges(elements, 6)}): {reason}");
    }
}
