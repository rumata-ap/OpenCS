using CScore;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Итог создания материалов стержней по данным ЛИРЫ.</summary>
public sealed class LiraBarMaterialsReport
{
    /// <summary>Созданные материалы (теги).</summary>
    public List<string> Created { get; } = [];
    /// <summary>Классы, которых нет в справочнике материалов.</summary>
    public List<string> NotInCatalog { get; } = [];
}

/// <summary>
/// Материалы сечений стержней по данным схемы: сечение КЭ собирается при проверке (размеры — из жёсткости,
/// арматура — из подбора), а бетон и арматура берутся из материалов проекта по классам — у ЛИРЫ из подбора
/// (*.asp), у SCAD из ЖБ-групп. Здесь недостающие классы создаются из справочника.
/// </summary>
public static class LiraBarMaterialCreator
{
    /// <summary>Классы бетона и арматуры стержневых КЭ (подбор ЛИРЫ или ЖБ-группы SCAD), которых нет среди материалов проекта.</summary>
    public static List<(string Class, bool Concrete)> MissingClasses(
        IEnumerable<Material> materials, FemCheckSchemaData data, IEnumerable<FemCheckScopeElement> elements)
    {
        var result = new List<(string, bool)>();
        if (!ImportedBarRcClasses.Available(data.IsScad, data.Asp, data.ScadConcreteGroups)) return result;
        var classesOf = ImportedBarRcClasses.Lookup(data.IsScad, data.Asp, data.ScadConcreteGroups);
        var list = materials.ToList();
        var seen = new HashSet<(string, bool)>();
        foreach (var e in elements)
        {
            if (e.Element.ElemType == "shell" || e.ElemNum is not int num) continue;
            if (classesOf(num) is not { } classes) continue;
            foreach (var (cls, concrete) in (ReadOnlySpan<(string, bool)>)[(classes.Concrete, true), (classes.Rebar, false)])
            {
                string key = MaterialCatalog.ClassKey(cls);
                if (key.Length == 0 || !seen.Add((key, concrete))) continue;
                if (MaterialCatalog.FindByClass(list, cls, concrete) == null) result.Add((cls, concrete));
            }
        }
        return result;
    }

    /// <summary>Создать материалы недостающих классов из справочника.</summary>
    /// <param name="catalogDirectory">Каталог справочника материалов; null — рядом с приложением.</param>
    public static LiraBarMaterialsReport Create(
        DatabaseService db, IEnumerable<(string Class, bool Concrete)> classes, string? catalogDirectory = null)
    {
        var report = new LiraBarMaterialsReport();
        foreach (var (cls, concrete) in classes)
        {
            if (MaterialCatalog.FindByClass(db.Materials, cls, concrete) != null) continue;
            var created = concrete ? MaterialCatalog.CreateHeavyConcrete(cls, catalogDirectory)
                                   : MaterialCatalog.CreateRebar(cls, catalogDirectory);
            if (created == null) { report.NotInCatalog.Add(cls); continue; }
            db.AddMaterial(created);
            report.Created.Add(created.Tag);
        }
        return report;
    }
}
