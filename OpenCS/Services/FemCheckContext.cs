using System.IO;
using System.Text;
using CScore;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Данные FEM-схемы для проверки по КЭ: конструктивные элементы, сетка, файлы армирования ЛИРЫ и SCAD.</summary>
public sealed class FemCheckSchemaData
{
    /// <summary>Идентификатор схемы.</summary>
    public int SchemaId { get; init; }
    /// <summary>Конструктивные элементы схемы.</summary>
    public IReadOnlyList<FemMember> Members { get; init; } = [];
    /// <summary>КЭ сетки схемы.</summary>
    public IReadOnlyList<FemElement> Mesh { get; init; } = [];
    /// <summary>Узлы сетки схемы.</summary>
    public IReadOnlyList<FemMeshNode> MeshNodes { get; init; } = [];
    /// <summary>Плоские регионы конструктивных элементов схемы (зоны армирования, локальные оси).</summary>
    public IReadOnlyList<CScore.Planar.PlanarRegion> Regions { get; init; } = [];
    /// <summary>Типы заданного армирования (RBT); null — файл не приложен или не читается.</summary>
    public LiraRbtFile? Rbt { get; init; }
    /// <summary>Подобранная арматура (ASP); null — файл не приложен или не читается.</summary>
    public LiraAspFile? Asp { get; init; }
    /// <summary>Программа-источник схемы (lira, scad, internal …).</summary>
    public string? SourceType { get; init; }
    /// <summary>Схема импортирована из SCAD.</summary>
    public bool IsScad => SourceType == "scad";
    /// <summary>Подобранная арматура SCAD (выгрузка плагина); null — не загружена или не читается.</summary>
    public ScadSelectedRebarFile? ScadSelected { get; init; }
    /// <summary>ЖБ-группы SCAD схемы; null — не прочитаны.</summary>
    public ScadConcreteGroupIndex? ScadConcreteGroups { get; init; }
    /// <summary>Заданное армирование SCAD схемы; null — не прочитано из .SPR.</summary>
    public ScadAssignedRebarFile? ScadAssigned { get; init; }
    /// <summary>Стальные профили жёсткостей STZ схемы SCAD; null — не прочитаны (нет вложения).</summary>
    public ScadSteelProfileIndex? ScadSteelProfiles { get; init; }
    /// <summary>Стальные группы SCAD схемы; null — не прочитаны (нет вложения).</summary>
    public ScadSteelGroupIndex? ScadSteelGroups { get; init; }
    /// <summary>Жёсткости схемы-источника по номеру (размеры сечений стержней); пусто — схема их не хранит.</summary>
    public IReadOnlyDictionary<int, LiraStiffnessRecord> Stiffnesses { get; init; } = new Dictionary<int, LiraStiffnessRecord>();
    /// <summary>Ошибки чтения файлов армирования.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Прочитать данные схемы из БД (в UI-потоке: соединение SQLite одно).</summary>
    public static FemCheckSchemaData Load(DatabaseService db, int schemaId)
    {
        var errors = new List<string>();
        LiraRbtFile? rbt = null;
        LiraAspFile? asp = null;
        if (db.GetFemSchemaReinforcementFile(schemaId) is { } rbtFile)
            try { rbt = LiraRbtReader.Read(rbtFile.Data); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            { errors.Add($"{rbtFile.FileName}: {ex.Message}"); }
        if (db.GetFemSchemaSelectedReinforcementFile(schemaId) is { } aspFile)
            try { asp = LiraAspReader.Read(aspFile.Data); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            { errors.Add($"{aspFile.FileName}: {ex.Message}"); }

        ScadSelectedRebarFile? scadSelected = null;
        ScadConcreteGroupIndex? scadGroups = null;
        if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadSelectedRebar) is { } scadFile)
            try { scadSelected = ScadRebarExportReader.Read(scadFile.Data); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            { errors.Add($"{scadFile.FileName}: {ex.Message}"); }
        if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadConcreteGroups) is { } groupsFile)
            try { scadGroups = ScadConcreteGroupIndex.FromJson(Encoding.UTF8.GetString(groupsFile.Data)); }
            catch (InvalidDataException ex) { errors.Add(ex.Message); }
        ScadAssignedRebarFile? scadAssigned = null;
        if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAssignedRebar) is { } assignedFile)
            try { scadAssigned = ScadAssignedRebarFile.FromJson(Encoding.UTF8.GetString(assignedFile.Data)); }
            catch (InvalidDataException ex) { errors.Add(ex.Message); }
        ScadSteelProfileIndex? scadSteel = null;
        if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadSteelProfiles) is { } steelFile)
            try { scadSteel = ScadSteelProfileIndex.FromJson(Encoding.UTF8.GetString(steelFile.Data)); }
            catch (InvalidDataException ex) { errors.Add(ex.Message); }
        ScadSteelGroupIndex? scadSteelGroups = null;
        if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadSteelGroups) is { } steelGroupsFile)
            try { scadSteelGroups = ScadSteelGroupIndex.FromJson(Encoding.UTF8.GetString(steelGroupsFile.Data)); }
            catch (InvalidDataException ex) { errors.Add(ex.Message); }

        var members = db.GetFemMembers(schemaId);
        // Узлы и регионы нужны раскладке OpenCS, узлы — ещё длинам КЭ стальных групп; иначе их не читаем.
        bool planar = members.Any(m => m.PlanarRegionId != null);
        bool nodes = planar || scadSteelGroups is { Groups.Count: > 0 };
        return new FemCheckSchemaData
        {
            SchemaId = schemaId,
            Members = members,
            Mesh = db.GetFemMeshElements(schemaId),
            MeshNodes = nodes ? db.GetFemMeshNodes(schemaId) : [],
            Regions = planar ? db.GetPlanarRegions(schemaId) : [],
            Rbt = rbt,
            Asp = asp,
            SourceType = db.GetFemSchemaSourceType(schemaId),
            ScadSelected = scadSelected,
            ScadConcreteGroups = scadGroups,
            ScadAssigned = scadAssigned,
            ScadSteelProfiles = scadSteel,
            ScadSteelGroups = scadSteelGroups,
            Stiffnesses = db.GetFemSchemaStiffnesses(schemaId),
            Errors = errors,
        };
    }

    /// <summary>Состав цели проверки: её КЭ сетки.</summary>
    public FemCheckScope Scope(IFemCheckable target) => target switch
    {
        FemMemberGroup group => FemCheckScope.ForGroup(group, Members, Mesh),
        FemMember member     => FemCheckScope.ForMember(member, Mesh),
        _                    => new FemCheckScope([], [], RefersToMeshElements: false),
    };

    /// <summary>Раскладка армирования OpenCS на КЭ схемы (фон + зоны регионов).</summary>
    /// <param name="sections">Пластинчатые сечения проекта.</param>
    /// <param name="fallbackSection">Сечение-фон для элементов без своего сечения.</param>
    public PlateLayoutResolver LayoutResolver(IEnumerable<PlateSection> sections, PlateSection? fallbackSection = null)
    {
        var byId = new Dictionary<int, PlateSection>();
        foreach (var s in sections) byId.TryAdd(s.Id, s);
        return new PlateLayoutResolver(Members, Regions, byId.GetValueOrDefault, Mesh, MeshNodes, fallbackSection);
    }
}

/// <summary>Подготовка проверки по КЭ: наборы усилий цели, сечения, источники армирования, готовность.</summary>
public static class FemCheckContext
{
    /// <summary>Проверка пластин (иначе — стержней).</summary>
    public static bool IsPlate(FemCheck check) => check.NormCode == "rc_plate_check";

    /// <summary>
    /// Наборы усилий цели: привязанные к ней самой и наборы той же схемы, в которых есть строки по её КЭ
    /// (набор может быть шире цели — импорт на группу, проверка по элементу из того же кБ).
    /// </summary>
    /// <returns>Набор и число его строк, относящихся к цели.</returns>
    public static List<(ForceSet Set, int Rows)> TargetForceSets(
        IEnumerable<ForceSet> all, IFemCheckable target, int schemaId, FemCheckScope scope, bool isPlate)
    {
        var numbers = scope.Elements
            .Where(e => (e.Element.ElemType == "shell") == isPlate && e.ElemNum.HasValue)
            .Select(e => e.ElemNum!.Value).ToHashSet();

        var result = new List<(ForceSet, int)>();
        foreach (var fs in all)
        {
            bool bound = target switch
            {
                FemMember element    => fs.SourceElementId == element.Id,
                FemMemberGroup group => fs.SourceMemberId == group.Id,
                _ => false,
            };
            if (!bound && fs.SourceSchemaId != schemaId) continue;

            // Статистика по номерам КЭ — без загрузки строк набора.
            var stats = fs.ElementStats(isPlate);
            int rows = stats.ByElement.Where(p => numbers.Contains(p.Key)).Sum(p => p.Value);
            int withoutNumber = stats.WithoutElement;
            // Строки без номера КЭ относятся к цели, только если набор привязан к ней самой.
            if (bound) result.Add((fs, rows + withoutNumber));
            else if (rows > 0) result.Add((fs, rows));
        }
        return result;
    }

    /// <summary>Сечения и источники армирования проверки. Вызывать в UI-потоке: читает коллекции проекта.</summary>
    public static FemPerElementInputs BuildInputs(
        AppViewModel app, FemCheck check, IFemCheckable target, FemCheckSchemaData data, FemCheckScope scope,
        IReadOnlyList<ForceSet> lookupForceSets)
    {
        if (IsPlate(check))
        {
            var schemaGroups = app.FemSchemas.FirstOrDefault(s => s.Id == data.SchemaId)?.MemberGroups;
            int? plateId = TargetPlateSectionId(target, scope, schemaGroups ?? [], out _);
            var template = app.PlateSections.FirstOrDefault(s => s.Id == plateId);
            var sources = new List<IPlateElementSectionSource>();
            if (template != null)
                foreach (string key in PlateCheckParams.Parse(check.ParamsJson).GetRebarSources())
                    switch (key)
                    {
                        case FemCheckRebarSource.Section:
                            sources.Add(new TemplatePlateSectionSource(template));
                            break;
                        case FemCheckRebarSource.Assigned when data.IsScad:
                            sources.Add(data.ScadAssigned is { Plates.Count: > 0 }
                                ? new ScadAssignedPlateSectionSource(template, data.ScadAssigned, data.ScadConcreteGroups)
                                : new UnavailablePlateSectionSource(key, Loc.S("FemCheckNoScadAssigned")));
                            break;
                        case FemCheckRebarSource.Selected when data.IsScad:
                            sources.Add(data.ScadSelected != null
                                ? new ScadSelectedPlateSectionSource(template, data.ScadSelected, data.ScadConcreteGroups)
                                : new UnavailablePlateSectionSource(key, Loc.S("FemCheckNoScadSelected")));
                            break;
                        case FemCheckRebarSource.Assigned:
                            sources.Add(data.Rbt != null
                                ? new LiraAssignedPlateSectionSource(template, data.Rbt, data.Asp)
                                : new UnavailablePlateSectionSource(key, Loc.S("FemCheckNoRbt")));
                            break;
                        case FemCheckRebarSource.Selected:
                            sources.Add(data.Asp != null
                                ? new LiraSelectedPlateSectionSource(template, data.Asp)
                                : new UnavailablePlateSectionSource(key, Loc.S("FemCheckNoAsp")));
                            break;
                        case FemCheckRebarSource.Layout:
                            sources.Add(new LayoutPlateSectionSource(template, data.LayoutResolver(app.PlateSections, template)));
                            break;
                    }

            return new FemPerElementInputs
            {
                PlateTemplate = template,
                PlateSources = sources,
                ConcreteMat = template == null ? null : app.Materials.FirstOrDefault(m => m.Id == template.ConcreteMaterialId),
                RebarMat = template == null ? null : app.Materials.FirstOrDefault(m => m.Id == template.RebarMaterialId),
                LookupForceSets = lookupForceSets,
            };
        }

        // Сечение цели: у элемента — его собственное; у группы — первое назначенное у её элементов
        // (у групп импортированных схем — у КЭ сетки).
        int? targetSectionId = target switch
        {
            FemMember element => element.CrossSectionId,
            _ => scope.RefersToMeshElements
                ? scope.Elements.Select(e => e.Element.CrossSectionId).FirstOrDefault(id => id != null)
                : scope.Members.Select(m => m.CrossSectionId).FirstOrDefault(id => id != null),
        };
        var sections = new Dictionary<int, CrossSection>();
        foreach (var s in app.CrossSections) sections.TryAdd(s.Id, s);
        var targetSection = targetSectionId is int id ? sections.GetValueOrDefault(id) : null;
        var (elementParams, paramWarnings) = check.NormCode == "steel_check"
            ? SteelGroupParams(data, scope)
            : (null, []);
        return new FemPerElementInputs
        {
            BarElementParams = elementParams,
            Warnings = paramWarnings,
            TargetBarSection = targetSection,
            BarSectionById = sid => sections.GetValueOrDefault(sid),
            BarSources = check.NormCode == "rc_check"
                ? BarSources(app.Materials, BarCheckParams.Parse(check.ParamsJson).RebarSources, data,
                    sections.GetValueOrDefault, targetSection, lookupForceSets)
                : [],
            // rc_check меняет состояние сечения — считаем на клонах; стальная проверка опирается
            // на привязку параметрического профиля, которую клон не несёт.
            ParallelBars = check.NormCode == "rc_check",
            BarSelectedAsCm2 = data.IsScad
                ? data.ScadSelected?.Bars.Where(b => b.Value.Envelope?.LongitudinalSum != null)
                      .ToDictionary(b => b.Key, b => b.Value.Envelope!.LongitudinalSum!.Value)
                  ?? new Dictionary<int, double>()
                : data.Asp?.Bars.ToDictionary(b => b.Key, b => b.Value.Envelope.LongitudinalSum)
                  ?? new Dictionary<int, double>(),
        };
    }

    /// <summary>
    /// Параметры СП 16 стальных КЭ из стальных групп SCAD (только для проверки по КЭ): γc, расчётные длины,
    /// раскрепления, предельные гибкости поверх параметров проверки. КЭ вне групп и КЭ без длины — параметры
    /// проверки. Нет стальных групп — (null, []).
    /// </summary>
    public static (Func<FemCheckScopeElement, string, string?>? Params, List<string> Warnings) SteelGroupParams(
        FemCheckSchemaData data, FemCheckScope scope)
    {
        if (data.ScadSteelGroups is not { Groups.Count: > 0 } groups) return (null, []);

        var nodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in data.MeshNodes) nodes.TryAdd(n.NodeTag, n);
        var bars = new Dictionary<int, FemElement>();
        foreach (var e in data.Mesh)
            if (e.ElemType != "shell" && int.TryParse(e.ElemTag, out int num)) bars.TryAdd(num, e);
        double? LengthOf(int num)
        {
            if (!bars.TryGetValue(num, out var e) || FemMeshTopology.ReadNodeTags(e) is not { Count: 2 } tags
                || !nodes.TryGetValue(tags[0], out var a) || !nodes.TryGetValue(tags[1], out var b)) return null;
            double l = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
            return l > 0 ? l : null;
        }
        double? LengthIn(ScadSteelGroup g, int num) => ImportedSteelDesign.Length(g, num, LengthOf);

        // Сводка — по КЭ цели.
        int withParams = 0, noLength = 0;
        var gammaN = new SortedDictionary<int, ScadSteelGroup>();
        foreach (var e in scope.Elements)
        {
            if (e.Element.ElemType == "shell" || e.ElemNum is not int num || groups.Find(num) is not { } g) continue;
            if (LengthIn(g, num) == null) { noLength++; continue; }
            withParams++;
            if (Math.Abs(g.GammaN - 1) > 1e-9 && g.GammaN > 0) gammaN.TryAdd(g.Num, g);
        }
        var warnings = new List<string>();
        if (withParams > 0) warnings.Add(string.Format(Loc.S("FemCheckSteelGroupParams"), withParams));
        if (noLength > 0) warnings.Add(string.Format(Loc.S("FemCheckSteelGroupNoLength"), noLength));
        foreach (var g in gammaN.Values)
            warnings.Add(string.Format(Loc.S("FemCheckSteelGroupGammaN"), ScadSteelGroupIndex.Label(g), g.GammaN));

        return ((e, baseJson) =>
        {
            if (e.ElemNum is not int num || groups.Find(num) is not { } g || LengthIn(g, num) is not double l) return null;
            return ImportedSteelDesign.Apply(CScore.Sp16.SteelDesignParams.Parse(baseJson), g, l).ToJson();
        }, warnings);
    }

    /// <summary>
    /// Источники сечения стержневых КЭ по ключам проверки; пустой список ключей — источники не выбирались
    /// (сечение проекта, как до появления источников у стержней).
    /// </summary>
    public static List<IBarElementSectionSource> BarSources(
        IEnumerable<Material> materials, IReadOnlyList<string> keys, FemCheckSchemaData data,
        Func<int, CrossSection?> sectionById, CrossSection? targetSection, IEnumerable<ForceSet>? forceSets = null)
    {
        var sources = new List<IBarElementSectionSource>(keys.Count);
        if (keys.Count == 0) return sources;

        var project = new ProjectBarSectionSource(FemCheckRebarSource.Section, sectionById, targetSection);
        var byClass = materials.ToList();
        var context = new LiraBarSectionContext(data.Stiffnesses, data.Asp,
            c => MaterialCatalog.FindByClass(byClass, c, concrete: true),
            c => MaterialCatalog.FindByClass(byClass, c, concrete: false), project.Find);

        var scadContext = data.IsScad
            ? new ScadBarSectionContext(data.Stiffnesses, data.ScadSelected, data.ScadConcreteGroups,
                c => MaterialCatalog.FindByClass(byClass, c, concrete: true),
                c => MaterialCatalog.FindByClass(byClass, c, concrete: false), project.Find)
            : null;

        foreach (string key in keys)
            switch (key)
            {
                case FemCheckRebarSource.Section:
                    sources.Add(project);
                    break;
                case FemCheckRebarSource.Assigned when scadContext != null:
                    sources.Add(data.ScadAssigned is { Rods.Count: > 0 }
                        ? new ScadAssignedBarSectionSource(scadContext, data.ScadAssigned,
                            ScadAssignedBarRebarSource.SectionCounts(data.ScadSelected, forceSets))
                        : new UnavailableBarSectionSource(key, Loc.S("FemCheckNoScadAssigned")));
                    break;
                case FemCheckRebarSource.Selected when scadContext != null:
                    sources.Add(data.ScadSelected != null
                        ? new ScadSelectedBarSectionSource(scadContext)
                        : new UnavailableBarSectionSource(key, Loc.S("FemCheckNoScadSelected")));
                    break;
                case FemCheckRebarSource.Assigned:
                    sources.Add(data.Rbt != null
                        ? new LiraAssignedBarSectionSource(context, data.Rbt)
                        : new UnavailableBarSectionSource(key, Loc.S("FemCheckNoRbt")));
                    break;
                case FemCheckRebarSource.Selected:
                    sources.Add(data.Asp != null
                        ? new LiraSelectedBarSectionSource(context)
                        : new UnavailableBarSectionSource(key, Loc.S("FemCheckNoAsp")));
                    break;
            }
        return sources;
    }

    /// <summary>
    /// Пластинчатое сечение цели. Если у самой цели оно не задано, берётся у «родственной» цели с теми же КЭ:
    /// у элемента из кБ — сечение группы, в которую входят его КЭ (усилия и сечение обычно заданы на группе
    /// кБ), у группы — сечение конструктивного элемента, которому принадлежат её КЭ.
    /// </summary>
    /// <param name="inheritedFrom">Имя цели, у которой взято сечение; null — сечение собственное или его нет.</param>
    public static int? TargetPlateSectionId(
        IFemCheckable target, FemCheckScope scope, IEnumerable<FemMemberGroup> schemaGroups, out string? inheritedFrom)
    {
        inheritedFrom = null;
        switch (target)
        {
            case FemMember { PlateSectionId: int own }:
                return own;
            case FemMemberGroup { PlateSectionId: int own }:
                return own;

            case FemMember:
            {
                // Группа с сечением, покрывающая больше всего КЭ элемента.
                var tags = scope.Elements.Select(e => e.Element.ElemTag).ToHashSet(StringComparer.Ordinal);
                var best = schemaGroups
                    .Where(g => g.PlateSectionId != null)
                    .Select(g => (Group: g, Common: FemCheckScope.GroupTags(g).Count(tags.Contains)))
                    .Where(x => x.Common > 0)
                    .OrderByDescending(x => x.Common)
                    .FirstOrDefault();
                inheritedFrom = best.Group?.Tag;
                return best.Group?.PlateSectionId;
            }

            case FemMemberGroup:
            {
                var owner = scope.Elements
                    .Where(e => e.Member?.PlateSectionId != null)
                    .GroupBy(e => e.Member!)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key;
                inheritedFrom = owner?.ElemTag;
                return owner?.PlateSectionId;
            }
        }
        return null;
    }

    /// <summary>Отображаемое имя источника армирования.</summary>
    public static string SourceName(string key) => key switch
    {
        FemCheckRebarSource.Section  => Loc.S("FemCheckSourceSection"),
        FemCheckRebarSource.Assigned => Loc.S("FemCheckSourceAssigned"),
        FemCheckRebarSource.Selected => Loc.S("FemCheckSourceSelected"),
        FemCheckRebarSource.Layout   => Loc.S("FemCheckSourceLayout"),
        _ => key,
    };

    /// <summary>Строка состояния диалога: «Усилия: 412 из 480 КЭ · армирование (заданное): 480 из 480».</summary>
    public static string ReadinessLine(FemCheckReadiness r, bool isPlate)
    {
        var parts = new List<string> { string.Format(Loc.S("FemCheckReadyForces"), r.ElementsWithForces, r.ElementsTotal) };
        foreach (var s in r.Sources)
            parts.Add(isPlate
                ? string.Format(Loc.S("FemCheckReadyRebar"), SourceName(s.Source), s.Ready, r.ElementsTotal)
                : s.Source.Length > 0
                    ? string.Format(Loc.S("FemCheckReadySectionsSource"), SourceName(s.Source), s.Ready, r.ElementsTotal)
                    : string.Format(Loc.S("FemCheckReadySections"), s.Ready, r.ElementsTotal));
        string line = string.Join(" · ", parts);
        return r.BlockingReason == null ? line : line + "\n" + string.Format(Loc.S("FemCheckReadyBlocked"), r.BlockingReason);
    }

    /// <summary>Подробности неполного покрытия для окна подтверждения запуска.</summary>
    public static string ReadinessDetails(FemCheckReadiness r, bool isPlate)
    {
        var sb = new StringBuilder();
        if (r.ElementsWithoutForces.Count > 0)
            sb.AppendLine(string.Format(Loc.S("FemCheckIncompleteNoForces"), r.ElementsWithoutForces.Count,
                FemCheckReadiness.FormatRanges(r.ElementsWithoutForces)));
        foreach (var s in r.Sources.Where(s => s.Ready < r.ElementsTotal))
        {
            string ranges = FemCheckReadiness.FormatRanges(s.NotReady);
            sb.AppendLine(isPlate
                ? string.Format(Loc.S("FemCheckIncompleteNoRebar"), SourceName(s.Source), r.ElementsTotal - s.Ready, ranges)
                : s.Source.Length > 0
                    ? string.Format(Loc.S("FemCheckIncompleteNoSectionSource"), SourceName(s.Source), r.ElementsTotal - s.Ready, ranges)
                    : string.Format(Loc.S("FemCheckIncompleteNoSection"), r.ElementsTotal - s.Ready, ranges));
            foreach (var (reason, count) in s.Reasons)
                sb.AppendLine(string.Format(Loc.S("FemCheckIncompleteReason"), reason, count));
        }
        if (r.RowsOutsideTarget > 0)
            sb.AppendLine(string.Format(Loc.S("FemCheckIncompleteOutside"), r.RowsOutsideTarget));
        return sb.ToString().TrimEnd();
    }
}
