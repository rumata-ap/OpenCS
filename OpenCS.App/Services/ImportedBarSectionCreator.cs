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
    /// <summary>Пользователь отказался от выбора армирования — ничего не изменено.</summary>
    public bool Cancelled { get; init; }
    /// <summary>Армирование создаваемых ЖБ-сечений.</summary>
    public ImportedBarRebarMode RebarMode { get; set; }
    /// <summary>Допуск унификации подбора (доля) — при <see cref="ImportedBarRebarMode.Selected"/>.</summary>
    public double SelectedTolerance { get; set; }
    /// <summary>КЭ с подбором и число различных армирований после унификации.</summary>
    public (int Elements, int Layouts) Unified { get; set; }
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
    /// <summary>КЭ, у которых автоматически созданное сечение без арматуры заменено армированным.</summary>
    public int Replaced { get; set; }
    /// <summary>Созданные стальные (МК) сечения — подмножество <see cref="Sections"/>.</summary>
    public List<string> SteelSections { get; } = [];
    /// <summary>Предупреждения сверки стальных сечений (площадь контура против сортамента источника).</summary>
    public List<string> Warnings { get; } = [];
    /// <summary>Причины, по которым сечение не создано, и номера КЭ.</summary>
    public List<(string Reason, List<int> Elements)> Skipped { get; } = [];
    /// <summary>Причины, по которым КЭ получили ЖБ-сечение без арматуры при выбранном армировании, и номера КЭ.</summary>
    public List<(string Reason, List<int> Elements)> WithoutRebar { get; } = [];

    /// <summary>Число КЭ, получивших сечение.</summary>
    public int AssignedElements => Assigned.Sum(a => a.Count);
}

/// <summary>Выбор армирования ЖБ-сечений: режим и допуск унификации подбора (доля, 0,2 — 20 %).</summary>
public sealed record ImportedBarRebarChoice(ImportedBarRebarMode Mode,
    double Tolerance = ImportedBarRebar.DefaultSelectedTolerance);

/// <summary>
/// Создание сечений стержней импортированной схемы (ЛИРА, SCAD): профиль — из жёсткости КЭ
/// (<see cref="ImportedBarProfiles"/>), классы бетона и арматуры — из подбора ЛИРЫ или ЖБ-группы SCAD
/// (<see cref="ImportedBarRcClasses"/>), сечение — параметрическое ЖБ (<see cref="RcSectionBuilder"/>), по выбору —
/// с армированием по данным схемы (<see cref="ImportedBarRebar"/>: заданное или подобранное).
/// Стальные профили (STZ SCAD) — параметрические МК-сечения (<see cref="SteelSectionBuilder"/>) из выбранной стали.
/// Одно сечение на форму, размеры, материалы и армирование; равное существующее параметрическое сечение используется
/// повторно. Сечение назначается КЭ сетки; КЭ с уже назначенным сечением не трогаются, кроме автоматически
/// созданного сечения без арматуры при выборе армирования — оно заменяется.
/// </summary>
public static class ImportedBarSectionCreator
{
    /// <summary>Создать и назначить сечения стержневым КЭ без сечения.</summary>
    /// <param name="elements">КЭ сетки; null — все КЭ схемы. Пластины пропускаются.</param>
    /// <param name="catalogDirectory">Каталог справочника материалов; null — рядом с приложением.</param>
    /// <param name="chooseSteel">Сталь для стальных сечений: вызывается один раз, если есть стальные КЭ; новый
    /// материал (Id = 0) добавляется в проект. null или вернувший null — стальные КЭ пропускаются.</param>
    /// <param name="steelCatalog">Сортамент OpenCS для ссылки на каталог (It); null — без ссылки.</param>
    /// <param name="chooseRebar">Армирование ЖБ-сечений: вызывается один раз со списком доступных режимов
    /// (первый — <see cref="ImportedBarRebarMode.None"/>), если есть ЖБ-КЭ и данные заданного или подобранного
    /// армирования; null-результат — отмена без изменений. null — без арматуры. Подобранное армирование
    /// унифицируется с допуском выбора (<see cref="ImportedBarRebar.Cluster{T}"/>).</param>
    public static ImportedBarSectionsReport Create(
        DatabaseService db, FemCheckSchemaData data, IEnumerable<FemElement>? elements = null, string? catalogDirectory = null,
        Func<Material?>? chooseSteel = null, ISteelCatalogLookup? steelCatalog = null,
        Func<IReadOnlyList<ImportedBarRebarMode>, ImportedBarRebarChoice?>? chooseRebar = null)
    {
        bool rcData = ImportedBarRcClasses.Available(data.IsScad, data.Asp, data.ScadConcreteGroups);
        var report = new ImportedBarSectionsReport();
        var skipped = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        void Skip(string reason, int num)
        {
            if (!skipped.TryGetValue(reason, out var list)) skipped[reason] = list = [];
            list.Add(num);
        }
        var withoutRebar = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        void NoRebar(string reason, int num)
        {
            if (!withoutRebar.TryGetValue(reason, out var list)) withoutRebar[reason] = list = [];
            list.Add(num);
        }

        // Параметрические ЖБ-сечения проекта без арматуры: назначенные ими КЭ — кандидаты на замену армированным.
        var service = new ParametricRcSectionProjectService(db);
        var existing = new List<(CrossSection Section, ParametricRcSectionDefinition Definition)>();
        foreach (var s in db.CrossSections)
            if (service.TryGetDefinition(s, out var d)) existing.Add((s, d));
        var rebarless = existing
            .Where(x => x.Definition.Shape == ParametricRcShape.Rectangle && x.Definition.ExtraBars.Count == 0
                        && x.Definition.UpperRebar is not { Enabled: true } && x.Definition.LowerRebar is not { Enabled: true }
                        && x.Definition.StirrupCuts.Count == 0)
            .ToDictionary(x => x.Section.Id, x => x.Definition);

        // ── КЭ, которым можно создать сечение ───────────────────────────────────────────────────
        var classesOf = ImportedBarRcClasses.Lookup(data.IsScad, data.Asp, data.ScadConcreteGroups);
        var ready = new List<(FemElement Element, int Num, ImportedBarProfile Profile, ImportedBarRcClasses Classes, bool Replaceable)>();
        var steelReady = new List<(FemElement Element, int Num, ImportedBarProfile Profile)>();
        var rcNoData = new List<int>();
        bool steelUnresolved = false;
        foreach (var e in elements ?? data.Mesh)
        {
            if (e.ElemType == "shell") continue;
            if (!int.TryParse(e.ElemTag, NumberStyles.None, CultureInfo.InvariantCulture, out int num)) continue;
            bool replaceable = e.CrossSectionId is int sid && rebarless.ContainsKey(sid);
            if (e.CrossSectionId != null && !replaceable) { report.AlreadyAssigned++; continue; }
            // У КЭ с сечением без арматуры препятствия не пропускают его, а оставляют сечение как есть.
            void SkipOrKeep(string reason)
            {
                if (replaceable) report.AlreadyAssigned++;
                else Skip(reason, num);
            }

            var (profile, reason) = ImportedBarProfiles.Resolve(data.Stiffnesses, e.StiffnessNum, data.IsScad,
                data.SteelProfiles);
            if (profile == null)
            {
                SkipOrKeep(reason!);
                steelUnresolved |= !replaceable && e.StiffnessNum is int sn && data.Stiffnesses.TryGetValue(sn, out var st)
                                   && ImportedBarProfiles.IsSteel(st, data.IsScad);
                continue;
            }
            if (profile.Material == ImportedBarMaterial.Steel)
            {
                if (replaceable) report.AlreadyAssigned++;
                else steelReady.Add((e, num, profile));
                continue;
            }
            if (!rcData)
            {
                if (replaceable) report.AlreadyAssigned++;
                else rcNoData.Add(num);
                continue;
            }
            if (classesOf(num) is not { } classes)
            {
                SkipOrKeep(data.IsScad ? "КЭ нет в ЖБ-группах SCAD — классы материалов неизвестны"
                                       : "КЭ нет в файле подбора ASP — классы материалов неизвестны");
                continue;
            }
            if (classes.Concrete.Length == 0 || classes.Rebar.Length == 0)
            {
                SkipOrKeep("классы бетона и арматуры не заданы");
                continue;
            }
            ready.Add((e, num, profile, classes, replaceable));
        }

        if (!rcData && steelReady.Count == 0 && !steelUnresolved)
            return new ImportedBarSectionsReport { NoMaterialData = true };

        // ── Армирование: выбор один раз, до изменений в проекте ─────────────────────────────────
        var mode = ImportedBarRebarMode.None;
        double tolerance = 0;
        var modes = RebarModes(data);
        if (ready.Count > 0 && modes.Count > 1 && chooseRebar != null)
        {
            if (chooseRebar(modes) is not { } chosen) return new ImportedBarSectionsReport { Cancelled = true };
            (mode, tolerance) = (chosen.Mode, chosen.Tolerance);
        }
        report.RebarMode = mode;
        if (mode == ImportedBarRebarMode.Selected) report.SelectedTolerance = tolerance;
        if (mode == ImportedBarRebarMode.None)
        {
            report.AlreadyAssigned += ready.Count(r => r.Replaceable);
            ready.RemoveAll(r => r.Replaceable);
        }

        foreach (int num in rcNoData)
            Skip(data.IsScad ? "у схемы нет ЖБ-групп SCAD — классы материалов неизвестны"
                             : "у схемы нет файла подбора ASP — классы материалов неизвестны", num);

        // ── Материалы: недостающие классы — из справочника ──────────────────────────────────────
        var missing = LiraBarMaterialCreator.MissingClasses(db.Materials, data,
            ready.Select(r => new FemCheckScopeElement(r.Num, r.Element, null)));
        report.Materials.AddRange(LiraBarMaterialCreator.Create(db, missing, catalogDirectory).Created);

        // ── План: КЭ по ключам сечений ──────────────────────────────────────────────────────────
        var plan = new Dictionary<RcSectionKey, (ImportedBarProfile Profile, string Label, ImportedBarRebarLayout? Rebar,
            List<(FemElement Element, int Num)> Members)>();
        void Plan(FemElement element, int num, ImportedBarProfile profile, string label, int concreteId, int rebarId,
            ImportedBarRebarLayout? layout, bool replaceable)
        {
            if (layout == null && replaceable)
            {
                report.AlreadyAssigned++;     // армировать нечем — сечение без арматуры остаётся
                return;
            }
            if (replaceable) report.Replaced++;
            var key = RcSectionBuilder.Key(profile, concreteId, rebarId, layout?.Bars);
            if (!plan.TryGetValue(key, out var entry))
                plan[key] = entry = (profile, label, layout, []);
            entry.Members.Add((element, num));
        }
        // Подбор — раскладка после унификации КЭ одной формы, материалов и привязок.
        var selected = new Dictionary<(RcSectionKey Key, string Covers), List<(SelectedElement Element, double[] Areas)>>();

        foreach (var (element, num, profile, classes, replaceable) in ready)
        {
            if (MaterialCatalog.FindByClass(db.Materials, classes.Concrete, concrete: true) is not { } concrete)
            {
                if (replaceable) report.AlreadyAssigned++;
                else Skip($"класса бетона {classes.Concrete} нет в справочнике тяжёлого бетона", num);
                continue;
            }
            if (MaterialCatalog.FindByClass(db.Materials, classes.Rebar, concrete: false) is not { } rebar)
            {
                if (replaceable) report.AlreadyAssigned++;
                else Skip($"класса арматуры {classes.Rebar} нет в справочнике арматуры", num);
                continue;
            }
            // Заменяется только своё автоматическое сечение: та же форма, размеры и материалы.
            if (replaceable && !RcSectionBuilder.Matches(rebarless[element.CrossSectionId!.Value],
                    RcSectionBuilder.Key(profile, concrete.Id, rebar.Id)))
            {
                report.AlreadyAssigned++;
                continue;
            }

            string label = $"{classes.Concrete} {classes.Rebar}";
            var barProfile = LiraBarProfile.From(profile, concrete, rebar);
            if (mode == ImportedBarRebarMode.Selected)
            {
                var (areas, reason) = SelectedAreas(data, num, barProfile);
                if (areas == null)
                {
                    NoRebar(reason!, num);
                    Plan(element, num, profile, label, concrete.Id, rebar.Id, null, replaceable);
                    continue;
                }
                var groupKey = (RcSectionBuilder.Key(profile, concrete.Id, rebar.Id), areas.Covers);
                if (!selected.TryGetValue(groupKey, out var group)) selected[groupKey] = group = [];
                group.Add((new SelectedElement(element, num, profile, label, replaceable, areas), areas.Values));
                continue;
            }

            ImportedBarRebarLayout? layout = null;
            if (mode != ImportedBarRebarMode.None)
            {
                var (found, reason) = Rebar(data, mode, element, num, barProfile);
                if (found == null) NoRebar(reason!, num);
                layout = found;
            }
            Plan(element, num, profile, label, concrete.Id, rebar.Id, layout, replaceable);
        }

        // Унификация подбора: одна раскладка на группу КЭ с перерасходом не выше допуска.
        int selectedElements = 0, layouts = 0;
        foreach (var ((key, _), items) in selected)
            foreach (var (areas, members) in ImportedBarRebar.Cluster(items, tolerance))
            {
                var first = members[0];
                var (layout, reason) = first.Areas.Layout(areas);
                selectedElements += members.Count;
                if (layout != null) layouts++;
                foreach (var m in members)
                {
                    if (layout == null) NoRebar(reason!, m.Num);
                    Plan(m.Element, m.Num, m.Profile, m.Label, key.ConcreteId, key.RebarId, layout, m.Replaceable);
                }
            }
        if (mode == ImportedBarRebarMode.Selected) report.Unified = (selectedElements, layouts);

        // ── Сечения и назначение ────────────────────────────────────────────────────────────────
        // Сечение по ключу: равное существующее либо новое; null — причина, по которой его нет.
        (CrossSection? Section, string? Reason) SectionFor(RcSectionKey key, ImportedBarProfile profile, string label,
            ImportedBarRebarLayout? layout)
        {
            var section = existing.FirstOrDefault(x => RcSectionBuilder.Matches(x.Definition, key)).Section;
            if (section != null)
            {
                report.Reused.Add(section.Tag);
                return (section, null);
            }
            var (definition, reason) = RcSectionBuilder.Build(profile, key.ConcreteId, key.RebarId, label, layout);
            if (definition == null) return (null, reason);
            definition = definition with { Tag = FreeTag(db, definition.Tag) };
            section = new CrossSection
            {
                Num = db.CrossSections.Count > 0 ? db.CrossSections.Max(s => s.Num) + 1 : 1,
                Tag = definition.Tag,
            };
            var result = service.GenerateAndSave(section, definition);
            if (result.Diagnostics.Count != 0) return (null, string.Join("; ", result.Diagnostics));
            existing.Add((section, definition));
            report.Sections.Add(section.Tag);
            return (section, null);
        }

        var assignments = new List<(FemElement, int?)>();
        foreach (var (key, (profile, label, layout, members)) in plan)
        {
            var (section, reason) = SectionFor(key, profile, label, layout);
            if (section == null && layout != null)
            {
                // Армирование не легло в сечение — сечение без арматуры с предупреждением.
                foreach (var m in members) NoRebar(reason!, m.Num);
                (section, reason) = SectionFor(key with { Bars = "" }, profile, label, null);
            }
            if (section == null)
            {
                foreach (var m in members) Skip(reason!, m.Num);
                continue;
            }
            assignments.AddRange(members.Select(m => (m.Element, (int?)section.Id)));
            report.Assigned.Add((section.Tag, members.Count));
        }
        CreateSteel(db, steelReady, data.ScadSteelGroups, data.SteelProfiles, catalogDirectory, chooseSteel, steelCatalog, report, assignments, Skip);
        if (assignments.Count > 0) db.SetFemElementCrossSections(assignments);

        foreach (var (reason, nums) in skipped)
            report.Skipped.Add((reason, [.. nums.Order()]));
        foreach (var (reason, nums) in withoutRebar)
            report.WithoutRebar.Add((reason, [.. nums.Order()]));
        return report;
    }

    /// <summary>
    /// Режимы армирования, для которых у схемы есть данные: первый — без арматуры, затем заданное (ТЗА ЛИРЫ,
    /// заданное SCAD) и подобранное (ASP ЛИРЫ, выгрузка плагина SCAD).
    /// </summary>
    public static IReadOnlyList<ImportedBarRebarMode> RebarModes(FemCheckSchemaData data)
    {
        var modes = new List<ImportedBarRebarMode> { ImportedBarRebarMode.None };
        if (data.IsScad ? data.ScadAssigned is { Rods.Count: > 0 } : data.Rbt is { BarTypes.Count: > 0 })
            modes.Add(ImportedBarRebarMode.Assigned);
        if (data.IsScad ? data.ScadSelected is { Bars.Count: > 0 } : data.Asp is { Bars.Count: > 0 })
            modes.Add(ImportedBarRebarMode.Selected);
        return modes;
    }

    /// <summary>КЭ с подобранными площадями, ожидающий унификации.</summary>
    sealed record SelectedElement(FemElement Element, int Num, ImportedBarProfile Profile, string Label, bool Replaceable,
        ImportedSelectedAreas Areas);

    /// <summary>Подобранные площади КЭ (ЛИРА — ASP, SCAD — выгрузка плагина) либо причина, по которой их нет.</summary>
    static (ImportedSelectedAreas? Areas, string? Reason) SelectedAreas(FemCheckSchemaData data, int num, LiraBarProfile profile) =>
        data.IsScad
            ? ImportedBarRebar.ScadSelectedAreas(
                data.ScadSelected != null && data.ScadSelected.Bars.TryGetValue(num, out var bar) ? bar : null,
                data.ScadConcreteGroups?.Find(num), profile)
            : ImportedBarRebar.LiraSelectedAreas(
                data.Asp != null && data.Asp.Bars.TryGetValue(num, out var asp) ? asp : null, profile);

    /// <summary>Армирование КЭ по данным схемы в выбранном режиме (кроме подобранного) либо причина, по которой его нет.</summary>
    static (ImportedBarRebarLayout? Layout, string? Reason) Rebar(
        FemCheckSchemaData data, ImportedBarRebarMode mode, FemElement element, int num, LiraBarProfile profile) =>
        (mode, data.IsScad) switch
        {
            (ImportedBarRebarMode.Assigned, false) => ImportedBarRebar.LiraAssigned(data.Rbt, element.ReinforcementTypeIds, profile),
            (ImportedBarRebarMode.Assigned, true) => ImportedBarRebar.ScadAssigned(
                data.ScadAssigned?.Rod(num), data.ScadConcreteGroups?.Find(num), profile),
            _ => (null, null),
        };

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
