using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Import;
using CScore.ParametricRc;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Что сделано при создании сечений стержней по данным схемы.</summary>
public sealed class ImportedBarSectionsReport
{
    /// <summary>У схемы нет данных о классах материалов: у ЛИРЫ — подбора (ASP), у SCAD — ЖБ-групп.</summary>
    public bool NoMaterialData { get; init; }
    /// <summary>Созданные материалы.</summary>
    public List<string> Materials { get; } = [];
    /// <summary>Созданные сечения.</summary>
    public List<string> Sections { get; } = [];
    /// <summary>Существующие сечения проекта, использованные повторно.</summary>
    public List<string> Reused { get; } = [];
    /// <summary>Назначенные сечения и число получивших их КЭ.</summary>
    public List<(string Section, int Count)> Assigned { get; } = [];
    /// <summary>Стержневые КЭ, у которых сечение уже было, — не тронуты.</summary>
    public int AlreadyAssigned { get; set; }
    /// <summary>Причины, по которым сечение не создано, и номера КЭ.</summary>
    public List<(string Reason, List<int> Elements)> Skipped { get; } = [];

    /// <summary>Число КЭ, получивших сечение.</summary>
    public int AssignedElements => Assigned.Sum(a => a.Count);
}

/// <summary>
/// Создание сечений стержней импортированной схемы (ЛИРА, SCAD): профиль — из жёсткости КЭ
/// (<see cref="ImportedBarProfiles"/>), классы бетона и арматуры — из подбора ЛИРЫ или ЖБ-группы SCAD
/// (<see cref="ImportedBarRcClasses"/>), сечение — параметрическое ЖБ без арматуры (<see cref="RcSectionBuilder"/>).
/// Одно сечение на форму, размеры и материалы; равное существующее параметрическое сечение используется повторно.
/// Сечение назначается КЭ сетки; КЭ с уже назначенным сечением не трогаются.
/// </summary>
public static class ImportedBarSectionCreator
{
    /// <summary>Создать и назначить сечения стержневым КЭ без сечения.</summary>
    /// <param name="elements">КЭ сетки; null — все КЭ схемы. Пластины пропускаются.</param>
    /// <param name="catalogDirectory">Каталог справочника материалов; null — рядом с приложением.</param>
    public static ImportedBarSectionsReport Create(
        DatabaseService db, FemCheckSchemaData data, IEnumerable<FemElement>? elements = null, string? catalogDirectory = null)
    {
        if (!ImportedBarRcClasses.Available(data.IsScad, data.Asp, data.ScadConcreteGroups))
            return new ImportedBarSectionsReport { NoMaterialData = true };

        var report = new ImportedBarSectionsReport();
        var skipped = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        void Skip(string reason, int num)
        {
            if (!skipped.TryGetValue(reason, out var list)) skipped[reason] = list = [];
            list.Add(num);
        }

        // ── КЭ, которым можно создать сечение ───────────────────────────────────────────────────
        var classesOf = ImportedBarRcClasses.Lookup(data.IsScad, data.Asp, data.ScadConcreteGroups);
        var ready = new List<(FemElement Element, int Num, ImportedBarProfile Profile, ImportedBarRcClasses Classes)>();
        foreach (var e in elements ?? data.Mesh)
        {
            if (e.ElemType == "shell") continue;
            if (!int.TryParse(e.ElemTag, NumberStyles.None, CultureInfo.InvariantCulture, out int num)) continue;
            if (e.CrossSectionId != null) { report.AlreadyAssigned++; continue; }

            var (profile, reason) = ImportedBarProfiles.Resolve(data.Stiffnesses, e.StiffnessNum, data.IsScad);
            if (profile == null) { Skip(reason!, num); continue; }
            if (classesOf(num) is not { } classes)
            {
                Skip(data.IsScad ? "КЭ нет в ЖБ-группах SCAD — классы материалов неизвестны"
                                 : "КЭ нет в файле подбора ASP — классы материалов неизвестны", num);
                continue;
            }
            if (classes.Concrete.Length == 0 || classes.Rebar.Length == 0)
            {
                Skip("классы бетона и арматуры не заданы", num);
                continue;
            }
            ready.Add((e, num, profile, classes));
        }

        // ── Материалы: недостающие классы — из справочника ──────────────────────────────────────
        var missing = LiraBarMaterialCreator.MissingClasses(db.Materials, data,
            ready.Select(r => new FemCheckScopeElement(r.Num, r.Element, null)));
        report.Materials.AddRange(LiraBarMaterialCreator.Create(db, missing, catalogDirectory).Created);

        // ── План: КЭ по ключам сечений ──────────────────────────────────────────────────────────
        var plan = new Dictionary<RcSectionKey, (ImportedBarProfile Profile, string Label, List<FemElement> Elements)>();
        foreach (var (element, num, profile, classes) in ready)
        {
            if (MaterialCatalog.FindByClass(db.Materials, classes.Concrete, concrete: true) is not { } concrete)
            {
                Skip($"класса бетона {classes.Concrete} нет в справочнике тяжёлого бетона", num);
                continue;
            }
            if (MaterialCatalog.FindByClass(db.Materials, classes.Rebar, concrete: false) is not { } rebar)
            {
                Skip($"класса арматуры {classes.Rebar} нет в справочнике арматуры", num);
                continue;
            }
            var key = RcSectionBuilder.Key(profile, concrete.Id, rebar.Id);
            if (!plan.TryGetValue(key, out var entry))
                plan[key] = entry = (profile, $"{classes.Concrete} {classes.Rebar}", []);
            entry.Elements.Add(element);
        }

        // ── Сечения и назначение ────────────────────────────────────────────────────────────────
        var service = new ParametricRcSectionProjectService(db);
        var existing = new List<(CrossSection Section, ParametricRcSectionDefinition Definition)>();
        foreach (var s in db.CrossSections)
            if (service.TryGetDefinition(s, out var d)) existing.Add((s, d));

        var assignments = new List<(FemElement, int?)>();
        foreach (var (key, (profile, label, members)) in plan)
        {
            var section = existing.FirstOrDefault(x => RcSectionBuilder.Matches(x.Definition, key)).Section;
            if (section != null)
                report.Reused.Add(section.Tag);
            else
            {
                var (definition, reason) = RcSectionBuilder.Build(profile, key.ConcreteId, key.RebarId, label);
                if (definition == null)
                {
                    foreach (var e in members) Skip(reason!, int.Parse(e.ElemTag, CultureInfo.InvariantCulture));
                    continue;
                }
                definition = definition with { Tag = FreeTag(db, definition.Tag) };
                section = new CrossSection
                {
                    Num = db.CrossSections.Count > 0 ? db.CrossSections.Max(s => s.Num) + 1 : 1,
                    Tag = definition.Tag,
                };
                var result = service.GenerateAndSave(section, definition);
                if (result.Diagnostics.Count != 0)
                {
                    string diagnostics = string.Join("; ", result.Diagnostics);
                    foreach (var e in members) Skip(diagnostics, int.Parse(e.ElemTag, CultureInfo.InvariantCulture));
                    continue;
                }
                existing.Add((section, definition));
                report.Sections.Add(section.Tag);
            }
            assignments.AddRange(members.Select(e => (e, (int?)section.Id)));
            report.Assigned.Add((section.Tag, members.Count));
        }
        if (assignments.Count > 0) db.SetFemElementCrossSections(assignments);

        foreach (var (reason, nums) in skipped)
            report.Skipped.Add((reason, [.. nums.Order()]));
        return report;
    }

    /// <summary>Имя сечения, не занятое в проекте: «Брус 300×500 B25 A500», при занятом — «… (2)».</summary>
    static string FreeTag(DatabaseService db, string tag)
    {
        var used = db.CrossSections.Select(s => s.Tag).ToHashSet(StringComparer.Ordinal);
        if (!used.Contains(tag)) return tag;
        for (int n = 2; ; n++)
            if (!used.Contains($"{tag} ({n})")) return $"{tag} ({n})";
    }
}
