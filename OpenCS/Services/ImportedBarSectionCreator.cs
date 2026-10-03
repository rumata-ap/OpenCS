using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Import;
using CScore.ParametricRc;
using CScore.ParametricSteel;
using CScore.Sp16;
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
    /// <summary>Созданные стальные (МК) сечения — подмножество <see cref="Sections"/>.</summary>
    public List<string> SteelSections { get; } = [];
    /// <summary>Предупреждения сверки стальных сечений (площадь контура против сортамента источника).</summary>
    public List<string> Warnings { get; } = [];
    /// <summary>Причины, по которым сечение не создано, и номера КЭ.</summary>
    public List<(string Reason, List<int> Elements)> Skipped { get; } = [];

    /// <summary>Число КЭ, получивших сечение.</summary>
    public int AssignedElements => Assigned.Sum(a => a.Count);
}

/// <summary>
/// Создание сечений стержней импортированной схемы (ЛИРА, SCAD): профиль — из жёсткости КЭ
/// (<see cref="ImportedBarProfiles"/>), классы бетона и арматуры — из подбора ЛИРЫ или ЖБ-группы SCAD
/// (<see cref="ImportedBarRcClasses"/>), сечение — параметрическое ЖБ без арматуры (<see cref="RcSectionBuilder"/>).
/// Стальные профили (STZ SCAD) — параметрические МК-сечения (<see cref="SteelSectionBuilder"/>) из выбранной стали.
/// Одно сечение на форму, размеры и материалы; равное существующее параметрическое сечение используется повторно.
/// Сечение назначается КЭ сетки; КЭ с уже назначенным сечением не трогаются.
/// </summary>
public static class ImportedBarSectionCreator
{
    /// <summary>Создать и назначить сечения стержневым КЭ без сечения.</summary>
    /// <param name="elements">КЭ сетки; null — все КЭ схемы. Пластины пропускаются.</param>
    /// <param name="catalogDirectory">Каталог справочника материалов; null — рядом с приложением.</param>
    /// <param name="chooseSteel">Сталь для стальных сечений: вызывается один раз, если есть стальные КЭ; новый
    /// материал (Id = 0) добавляется в проект. null или вернувший null — стальные КЭ пропускаются.</param>
    /// <param name="steelCatalog">Сортамент OpenCS для ссылки на каталог (It); null — без ссылки.</param>
    public static ImportedBarSectionsReport Create(
        DatabaseService db, FemCheckSchemaData data, IEnumerable<FemElement>? elements = null, string? catalogDirectory = null,
        Func<Material?>? chooseSteel = null, ISteelCatalogLookup? steelCatalog = null)
    {
        bool rcData = ImportedBarRcClasses.Available(data.IsScad, data.Asp, data.ScadConcreteGroups);
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
        var steelReady = new List<(FemElement Element, int Num, ImportedBarProfile Profile)>();
        var rcNoData = new List<int>();
        bool steelUnresolved = false;
        foreach (var e in elements ?? data.Mesh)
        {
            if (e.ElemType == "shell") continue;
            if (!int.TryParse(e.ElemTag, NumberStyles.None, CultureInfo.InvariantCulture, out int num)) continue;
            if (e.CrossSectionId != null) { report.AlreadyAssigned++; continue; }

            var (profile, reason) = ImportedBarProfiles.Resolve(data.Stiffnesses, e.StiffnessNum, data.IsScad,
                data.SteelProfiles);
            if (profile == null)
            {
                Skip(reason!, num);
                steelUnresolved |= e.StiffnessNum is int sn && data.Stiffnesses.TryGetValue(sn, out var st)
                                   && ImportedBarProfiles.IsSteel(st, data.IsScad);
                continue;
            }
            if (profile.Material == ImportedBarMaterial.Steel) { steelReady.Add((e, num, profile)); continue; }
            if (!rcData) { rcNoData.Add(num); continue; }
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

        if (!rcData && steelReady.Count == 0 && !steelUnresolved)
            return new ImportedBarSectionsReport { NoMaterialData = true };
        foreach (int num in rcNoData)
            Skip(data.IsScad ? "у схемы нет ЖБ-групп SCAD — классы материалов неизвестны"
                             : "у схемы нет файла подбора ASP — классы материалов неизвестны", num);

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
        CreateSteel(db, steelReady, data.ScadSteelGroups, data.SteelProfiles, catalogDirectory, chooseSteel, steelCatalog, report, assignments, Skip);
        if (assignments.Count > 0) db.SetFemElementCrossSections(assignments);

        foreach (var (reason, nums) in skipped)
            report.Skipped.Add((reason, [.. nums.Order()]));
        return report;
    }

    /// <summary>
    /// Стальные КЭ: сталь — по марке стальной группы SCAD (строка справочника СП 16 по толщине профиля), у КЭ вне
    /// групп и при неизвестной марке — выбором один раз; одно МК-сечение на (профиль, сталь), равное существующее —
    /// повторно.
    /// </summary>
    static void CreateSteel(DatabaseService db, List<(FemElement Element, int Num, ImportedBarProfile Profile)> ready,
        ScadSteelGroupIndex? groups, SteelProfileIndex? profiles, string? catalogDirectory,
        Func<Material?>? chooseSteel, ISteelCatalogLookup? catalog, ImportedBarSectionsReport report,
        List<(FemElement, int?)> assignments, Action<string, int> skip)
    {
        if (ready.Count == 0) return;

        Material Register(Material steel)
        {
            if (steel.Id != 0 && db.Materials.Contains(steel)) return steel;
            if (MaterialCatalog.FindSameSteel(db.Materials, steel) is { } same) return same;
            db.AddMaterial(steel);
            report.Materials.Add(steel.Tag);
            return steel;
        }

        Material? chosen = null;
        bool asked = false;
        Material? Chosen()
        {
            if (asked) return chosen;
            asked = true;
            return chosen = chooseSteel?.Invoke() is { } s ? Register(s) : null;
        }

        var byMark = new Dictionary<(string Mark, double Thickness, bool Shaped), Material?>();
        var warned = new HashSet<string>(StringComparer.Ordinal);
        void Warn(string text) { if (warned.Add(text)) report.Warnings.Add(text); }

        // Марка стали КЭ: стальная группа SCAD либо Steel = |…| жёсткости ЛИРЫ; null — не задана.
        (string Mark, string Label)? MarkOf(int num, ImportedBarProfile profile)
        {
            if (groups?.Find(num) is { } g)
                return (g.SteelMark, $"стальная группа SCAD {ScadSteelGroupIndex.Label(g)}");
            if (profiles?.Find(profile.StiffnessNum)?.SteelMark is { } liraMark)
                return (liraMark, $"жёсткость ЛИРЫ {profile.StiffnessNum}");
            return null;
        }

        Material? SteelOf(int num, ImportedBarProfile profile)
        {
            var shape = profile.Steel!;
            if (MarkOf(num, profile) is not var (sourceMark, label)) return Chosen();
            string mark = MaterialCatalog.NormalizeSteelMark(sourceMark);
            if (mark.Length == 0)
            {
                Warn($"{label}: марка стали не задана (Ry задано вручную) — сталь выбирается вручную");
                return Chosen();
            }
            double t = Math.Round(Math.Max(shape.Tw, shape.Tf), 6);
            bool shaped = shape.Fabrication == SteelFabrication.Rolled
                          && shape.Kind is not (SteelProfileKind.Pipe or SteelProfileKind.Box or SteelProfileKind.Round);
            if (!byMark.TryGetValue((mark, t, shaped), out var steel))
            {
                steel = MaterialCatalog.CreateStructuralSteel(mark, t, shaped, catalogDirectory) is { } created
                    ? Register(created) : null;
                byMark[(mark, t, shaped)] = steel;
            }
            if (steel != null) return steel;
            Warn($"{label}: марки {mark} для толщины {t * 1000:0.#} мм нет в справочнике СП 16 — " +
                 "сталь выбирается вручную");
            return Chosen();
        }

        var plan = new Dictionary<SteelSectionKey, (ImportedBarProfile Profile, Material Steel, List<(FemElement Element, int Num)> Members)>();
        foreach (var (element, num, profile) in ready)
        {
            if (SteelOf(num, profile) is not { } steel)
            {
                skip("сталь для стальных сечений не выбрана", num);
                continue;
            }
            var key = SteelSectionBuilder.Key(profile, steel.Id)!.Value;
            if (!plan.TryGetValue(key, out var entry)) plan[key] = entry = (profile, steel, []);
            entry.Members.Add((element, num));
        }
        var service = new ParametricSteelSectionProjectService(db);
        var existing = new List<(CrossSection Section, ParametricSteelSectionDefinition Definition)>();
        foreach (var s in db.CrossSections)
            if (service.TryGetDefinition(s, out var d)) existing.Add((s, d));

        foreach (var (key, (profile, steel, members)) in plan)
        {
            var section = existing.FirstOrDefault(x => SteelSectionBuilder.Matches(x.Definition, key)).Section;
            if (section != null)
                report.Reused.Add(section.Tag);
            else
            {
                var built = SteelSectionBuilder.Build(profile, steel.Id, catalog);
                if (built.Definition is not { } definition)
                {
                    foreach (var m in members) skip(built.Reason!, m.Num);
                    continue;
                }
                if (built.Warning != null) report.Warnings.Add(built.Warning);
                definition = definition with { Tag = FreeTag(db, $"{definition.Tag} {steel.Tag.Split(',')[0]}".Trim()) };
                section = new CrossSection
                {
                    Num = db.CrossSections.Count > 0 ? db.CrossSections.Max(s => s.Num) + 1 : 1,
                    Tag = definition.Tag,
                };
                var result = service.GenerateAndSave(section, definition);
                if (result.Diagnostics.Count != 0)
                {
                    string diagnostics = string.Join("; ", result.Diagnostics);
                    foreach (var m in members) skip(diagnostics, m.Num);
                    continue;
                }
                existing.Add((section, definition));
                report.Sections.Add(section.Tag);
                report.SteelSections.Add(section.Tag);
            }
            assignments.AddRange(members.Select(m => (m.Element, (int?)section.Id)));
            report.Assigned.Add((section.Tag, members.Count));
        }
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
