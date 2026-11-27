using System.Globalization;
using System.IO;
using System.Text;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Сводка переноса ГУ для окна сводки и консоли: заголовок, таблица, сообщения (непереносимое, резолвер).</summary>
public sealed record FemBoundarySummary(string Header, IReadOnlyList<FemBoundaryTransferRow> Rows, IReadOnlyList<string> Messages);

/// <summary>ΣF перенесённого загружения (кН) для сверки с программой-источником.</summary>
public sealed record FemLoadCaseTotal(string Case, int? SourceNum, double FxKn, double FyKn, double FzKn,
    IReadOnlyList<string> Diagnostics);

/// <summary>Итог переноса нагрузок: ΣF загружений и признак «величины расчётные, а в настройках — нормативные».</summary>
public sealed record FemLoadTransferOutcome(IReadOnlyList<FemLoadCaseTotal> Totals, bool AskDesignLoadValues);

/// <summary>
/// Где выполнять тяжёлую работу импорта без обращений к базе и журналу (перенос нагрузок, ΣF, разбор файла): окно —
/// в пуле потоков, консоль — сразу.
/// </summary>
public interface IImportWork
{
    Task<T> Run<T>(Func<T> work);
}

/// <summary>Работа импорта — сразу в вызывающем потоке (консоль, тесты).</summary>
public sealed class InlineImportWork : IImportWork
{
    public static readonly InlineImportWork Instance = new();
    public Task<T> Run<T>(Func<T> work) => Task.FromResult(work());
}

/// <summary>Работа импорта — в пуле потоков (окно: интерфейс не замирает на крупных схемах).</summary>
public sealed class BackgroundImportWork : IImportWork
{
    public static readonly BackgroundImportWork Instance = new();
    public Task<T> Run<T>(Func<T> work) => Task.Run(work);
}

/// <summary>
/// Запись импорта схем ЛИРЫ и SCAD — общая для меню «Импорт» и консоли (<c>import schema</c>, <c>import boundary</c>,
/// <c>import loads</c>). Разбор файлов — в CScore (<see cref="LiraTextReader"/>, <see cref="LiraFileParser"/>,
/// <see cref="ScadTextParser"/>, …), чтение через COM/DLL — в <see cref="LiraApiSchemaReader"/> и <c>Scad.ScadApiReader</c>.
/// Служба не знает ни про WPF, ни про дерево проекта: окно после вызова обновляет дерево и показывает сводку.
/// <para>
/// Новая схема создаётся через <see cref="Create"/>: при ошибке на любом шаге (сетка, ГУ, нагрузки, профили) созданная
/// схема удаляется — в проекте не остаётся пустой или неполной схемы с именем источника.
/// </para>
/// <para>
/// Перенос нагрузок разбит на фазы, чтобы окно могло считать тяжёлое в фоне: <see cref="PrepareLiraLoads"/> (чтение
/// базы) → функция переноса (без базы) → <see cref="SaveLoadTransfer"/> (запись) → функция ΣF (без базы) →
/// <see cref="FinishLoadTransfer"/>. Консоль вызывает их подряд.
/// </para>
/// </summary>
public sealed class FemSchemaImport(DatabaseService db, ILogService log, LiraImportSettings lira, CalcSettings calc)
{
    readonly DatabaseService _db = db;
    readonly ILogService _log = log;

    /// <summary>Настройки импорта ЛИРЫ (т → кН, каталог текстовых файлов).</summary>
    public LiraImportSettings Lira { get; } = lira;

    // ── Каркас ─────────────────────────────────────────────────────────────────

    /// <summary>Создаёт схему и заполняет её; исключение — схема удаляется, исключение пробрасывается.</summary>
    public FemSchema Create(string tag, string sourceType, string? sourcePath, Action<FemSchema> fill)
    {
        var schema = NewSchema(tag, sourceType, sourcePath);
        try { fill(schema); return schema; }
        catch (Exception ex) { RollBack(schema, ex); throw; }
    }

    /// <summary>То же для заполнения с ожиданием (перенос нагрузок в фоне).</summary>
    public async Task<FemSchema> CreateAsync(string tag, string sourceType, string? sourcePath, Func<FemSchema, Task> fill)
    {
        var schema = NewSchema(tag, sourceType, sourcePath);
        try { await fill(schema); return schema; }
        catch (Exception ex) { RollBack(schema, ex); throw; }
    }

    FemSchema NewSchema(string tag, string sourceType, string? sourcePath)
    {
        var schema = new FemSchema { Tag = tag, SourceType = sourceType, SourcePath = sourcePath };
        _db.SaveFemSchema(schema);
        return schema;
    }

    void RollBack(FemSchema schema, Exception cause)
    {
        try
        {
            _db.DeleteFemSchema(schema);
            _log.Warning(string.Format(Loc.S("FemImportRolledBack"), schema.Tag, cause.Message));
        }
        catch (Exception ex)
        {
            _log.Error(string.Format(Loc.S("FemImportRollbackFailed"), schema.Tag, ex.Message));
        }
    }

    /// <summary>Сохраняет сетку и группы импорта; номера, которых нет в сетке (КЭ пропущенных типов), из групп
    /// убираются с предупреждением.</summary>
    public void SaveMesh(FemSchema schema, CScore.Fem.Import.FemImportResult result)
    {
        int pruned = result.PruneMissingGroupTags();
        if (pruned > 0) _log.Warning(string.Format(Loc.S("FemImportGroupTagsPruned"), pruned));
        var warnings = _db.SaveFemImport(schema.Id, result);
        foreach (var w in warnings.Take(20)) _log.Warning(w.Message);
        if (warnings.Count > 20) _log.Warning(string.Format(Loc.S("FemImportMoreWarnings"), warnings.Count - 20));
    }

    /// <summary>Сбрасывает результаты постановок схемы; возвращает число сброшенных.</summary>
    public int InvalidateAnalyses(FemSchema schema)
    {
        int invalidated = 0;
        foreach (var analysis in schema.Analyses.Where(a => a.ResultId != null))
        {
            analysis.InvalidateResult();
            _db.SaveFemAnalysis(analysis);
            invalidated++;
        }
        return invalidated;
    }

    // ── ЛИРА: сетка и вложения ──────────────────────────────────────────────────

    /// <summary>
    /// Сетка и группы КЭ импорта ЛИРЫ; возвращает число групп. <paramref name="allGroups"/> — кроме групп по жёсткостям,
    /// ещё по кБ и ТЗА (API и текст ЛИРЫ; у CSV и .lir этих данных нет).
    /// </summary>
    public int SaveLiraMesh(FemSchema schema, LiraSchemaData raw, bool allGroups = true)
    {
        var meshNodes = LiraSchemaConverter.ToFemMeshNodes(raw, schema.Id);
        var meshElements = LiraSchemaConverter.ToFemMeshBarElements(raw, schema.Id)
            .Concat(LiraSchemaConverter.ToFemMeshShellElements(raw, schema.Id)).ToArray();
        var groups = LiraSchemaConverter.ToFemMemberGroupsByStiffness(raw, schema.Id)
            .Concat(LiraSchemaConverter.ToFemMemberGroupsByPlateStiffness(raw, schema.Id));
        if (allGroups)
            groups = groups.Concat(LiraSchemaConverter.ToFemMemberGroupsByConstructiveBlocks(raw, schema.Id))
                .Concat(LiraSchemaConverter.ToFemMemberGroupsByReinforcementTypes(raw, schema.Id));
        var memberGroups = groups.ToArray();
        SaveMesh(schema, CScore.Fem.Import.FemImportResult.MeshOnly(meshNodes, meshElements, memberGroups));
        return memberGroups.Length;
    }

    /// <summary>Закрепления узлов и C1 пластин из данных ЛИРЫ (без текстового файла).</summary>
    public void SaveLiraSupports(FemSchema schema, LiraSchemaData raw)
    {
        var supports = LiraSchemaConverter.ToFemMeshNodeSupports(raw);
        var props = LiraSchemaConverter.ToFemElementBoundaryProps(raw);
        if (supports.Length == 0 && props.Count == 0) return;
        _db.SaveFemBoundary(schema.Id, LiraSchemaConverter.BoundaryOrigin, supports, [], [], props.Count > 0 ? props : null);
        if (supports.Length > 0) _log.Info(string.Format(Loc.S("LiraSupportsImported"), supports.Length));
        if (props.Count > 0) _log.Info(string.Format(Loc.S("LiraFoundationImported"), props.Count));
        WarnFoundationBeyondC1(raw);
    }

    /// <summary>Основание сложнее C1 (C2, Winkler-Pasternak) не переносится — предупреждение.</summary>
    public void WarnFoundationBeyondC1(LiraSchemaData raw)
    {
        if (raw.PlateFoundationBeyondC1 > 0)
            _log.Warning(string.Format(Loc.S("LiraFoundationBeyondC1"), raw.PlateFoundationBeyondC1));
    }

    /// <summary>Блоки, жёсткости, стальные профили и единицы импорта ЛИРЫ.</summary>
    public void SaveLiraExtras(FemSchema schema, LiraSchemaData raw)
    {
        _db.SaveFemSchemaConstructiveBlocks(schema.Id, raw.ConstructiveBlocks);
        _db.SaveFemSchemaStiffnesses(schema.Id, raw.Stiffnesses);
        SaveLiraSteelProfiles(schema.Id, raw.Stiffnesses);
        if (raw.Units != null) SaveLiraUnits(schema.Id, raw.Units);
    }

    /// <summary>Единицы характеристик материалов ЛИРЫ — вложение схемы.</summary>
    public void SaveLiraUnits(int schemaId, LiraUnits units) =>
        SaveSourceFile(schemaId, FemSchemaSourceFileKind.LiraUnits, units.ToJson());

    /// <summary>Стальные профили жёсткостей ЛИРЫ по сортаментам; профили без сортамента — предупреждение.</summary>
    public void SaveLiraSteelProfiles(int schemaId, IEnumerable<LiraStiffnessRecord> stiffnesses)
    {
        var entries = LiraSteelProfileLoader.Resolve(stiffnesses, LiraSteelProfileLoader.SortamentDirectories());
        if (entries.Count == 0) return;
        _db.SaveFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.LiraSteelProfiles, "",
            Encoding.UTF8.GetBytes(SteelProfileIndex.ToJson(entries)));
        var failed = entries.Where(e => e.Shape == null).ToList();
        _log.Info(string.Format(Loc.S("LiraSteelProfilesLoaded"), entries.Count - failed.Count, entries.Count));
        foreach (var e in failed)
            _log.Warning(string.Format(Loc.S("LiraSteelProfileFailed"), e.Num, e.Source, e.Reason));
    }

    /// <summary>Итог импорта ЛИРЫ для журнала (оси стержней по умолчанию — предупреждение).</summary>
    public string LiraDone(LiraSchemaData raw, int groupCount)
    {
        int bars = raw.Elements.Count(e => e.NodeIds.Length == 2);
        int shells = raw.Elements.Count(e => e.NodeIds.Length is 3 or 4);
        if (bars > 0) _log.Warning(Loc.S("LiraBarAxesDefault"));
        string done = string.Format(Loc.S("ImportLiraSchemaSuccess"), raw.Nodes.Count, bars, shells, groupCount);
        if (raw.ConstructiveBlocks.Count > 0) done += string.Format(Loc.S("ImportLiraBlocksSuffix"), raw.ConstructiveBlocks.Count);
        return done;
    }

    /// <summary>Схема из CSV ЛИРЫ или .lir (<see cref="LiraFileParser"/>): сетка, группы по жёсткостям, закрепления, C1.</summary>
    public (FemSchema Schema, string Done) ImportLiraMeshOnly(LiraSchemaData raw, string tag, string? sourcePath)
    {
        int groups = 0;
        var schema = Create(tag, "lira", sourcePath, s =>
        {
            groups = SaveLiraMesh(s, raw, allGroups: false);
            SaveLiraSupports(s, raw);
            _db.SaveFemSchemaStiffnesses(s.Id, raw.Stiffnesses);
        });
        return (schema, LiraDone(raw, groups));
    }

    // ── ЛИРА: текстовый файл ────────────────────────────────────────────────────

    /// <summary>Свой каталог текстовых файлов из настроек импорта ЛИРЫ; не задан или не существует — null.</summary>
    public string? OwnLiraTextDirectory() => Lira.TextFileDirectory is { } d && Directory.Exists(d) ? d : null;

    /// <summary>Каталоги поиска текстового файла: свой каталог из настроек (первым) и каталоги Data версий ЛИРЫ.</summary>
    public List<LiraDataDirectory> LiraTextDirectories()
    {
        var dirs = LiraTextFileLocator.DataDirectories(LiraTextFileLocator.PublicDocuments);
        if (OwnLiraTextDirectory() is { } own) dirs.Insert(0, new LiraDataDirectory(own, null));
        return dirs;
    }

    /// <summary>Текстовый файл схемы <paramref name="title"/>: в своём каталоге, иначе — по каталогам Data (версия
    /// открытой ЛИРЫ, затем самый свежий).</summary>
    public string? FindLiraText(string? title, List<LiraDataDirectory> dirs, int? version) =>
        (OwnLiraTextDirectory() is { } own ? LiraTextFileLocator.Find(title, [new LiraDataDirectory(own, null)], null) : null)
        ?? LiraTextFileLocator.Find(title, dirs, version);

    /// <summary>
    /// Читает текстовый файл ЛИРЫ (разбор — через <paramref name="work"/>); замечания разбора — в журнал; ошибка
    /// чтения — в журнал и null (повреждённый файл не роняет импорт: ГУ и нагрузки дочитываются командой).
    /// </summary>
    public async Task<LiraTextSchema?> ReadLiraTextAsync(string path, IImportWork work)
    {
        LiraTextSchema txt;
        try { txt = await work.Run(() => LiraTextReader.Read(path)); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warning(string.Format(Loc.S("LiraTextReadError"), path, ex.Message));
            return null;
        }
        foreach (var w in txt.Warnings.Take(10)) _log.Warning($"{Path.GetFileName(path)}: {w}");
        int more = txt.Warnings.Count - 10 + txt.SuppressedWarnings;
        if (more > 0) _log.Warning(string.Format(Loc.S("LiraTextMoreWarnings"), Path.GetFileName(path), more));
        _log.Info(string.Format(Loc.S("LiraTextRead"), path, txt.Nodes.Count, txt.Elements.Count, txt.Loads.Count));
        return txt;
    }

    /// <summary>Текстовый файл старше файла схемы .lir — ГУ и нагрузки могут быть устаревшими (предупреждение).</summary>
    public void WarnIfLiraTextStale(string path, string? lirPath)
    {
        if (lirPath == null || !File.Exists(lirPath)) return;
        DateTime txtTime = File.GetLastWriteTime(path), lirTime = File.GetLastWriteTime(lirPath);
        if (txtTime < lirTime.AddMinutes(-1))
            _log.Warning(string.Format(Loc.S("LiraTextStale"), Path.GetFileName(path), txtTime.ToString("dd.MM.yyyy HH:mm"),
                Path.GetFileName(lirPath), lirTime.ToString("dd.MM.yyyy HH:mm")));
    }

    /// <summary>Текст «файл не от этой схемы» для журнала и окна.</summary>
    public static string MismatchMessage(FemSchema schema, string path, LiraTextMeshMatch match) =>
        string.Format(Loc.S("LiraTextMismatch"), Path.GetFileName(path), schema.Tag, match.Missing, match.Different,
            match.Example ?? (match.Nodes == 0 ? Loc.S("LiraTextNoImportedNodes") : ""));

    /// <summary>
    /// Геометрия, жёсткости, блоки и C1 схемы из текстового файла ЛИРЫ (и <c>Work\&lt;Title&gt;.ald</c> рядом, если есть).
    /// Без COM и без ЛИРЫ — работает везде. Замечания — в журнал.
    /// </summary>
    public async Task<LiraSchemaData> LiraTextToSchemaData(LiraTextSchema txt, string path, IImportWork work)
    {
        string? aldPath = LiraAldFile.PathFor(path);
        LiraAldFile? ald = null;
        if (aldPath != null)
            try { ald = await work.Run(() => LiraAldFile.Read(aldPath)); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { _log.Warning(string.Format(Loc.S("LiraAldReadError"), aldPath, ex.Message)); }
        else _log.Warning(string.Format(Loc.S("LiraAldNotFound"), Path.GetFileNameWithoutExtension(path) + ".ald"));
        var report = new List<string>();
        double tonToKn = Lira.TonToKnFactor;
        var raw = await work.Run(() => LiraTextSchemaConverter.ToSchemaData(txt, ald, tonToKn, report));
        foreach (var line in report) _log.Warning(line);
        return raw;
    }

    /// <summary>ГУ текстового файла в сеточный уровень схемы (заменяет перенесённые раньше; ручные не трогает).</summary>
    /// <param name="foundationC1">C1 пластин, прочитанный не из текста (API); null — нет.</param>
    public FemBoundarySummary TransferLiraBoundary(FemSchema schema, LiraTextSchema txt, IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> elements, IReadOnlyDictionary<string, double>? foundationC1)
    {
        var result = LiraBoundaryTransfer.Transfer(txt, meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal),
            ElementTypes(elements), Lira.TonToKnFactor, foundationC1);
        return SaveBoundaryTransfer(schema, result, LiraBoundaryTransfer.Origin, meshNodes);
    }

    /// <summary>C1 пластин импорта (прочитанный из API) — сохраняется при повторном переносе ГУ из текста.</summary>
    public static Dictionary<string, double>? ImportedFoundationC1(IEnumerable<FemElement> elements)
    {
        var c1 = elements.Where(e => e.Origin == FemMember.MeshSourceImported && e.ElemType == "shell" && e.FoundationC1 is > 0)
            .GroupBy(e => e.ElemTag, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().FoundationC1!.Value, StringComparer.Ordinal);
        return c1.Count > 0 ? c1 : null;
    }

    /// <summary>
    /// Фаза 1 переноса нагрузок текстового файла: повороты сечений стержней по осям ЛИРЫ и чтение базы. Возвращает
    /// перенос без обращений к базе (можно в фоне).
    /// </summary>
    public Func<FemLoadTransferResult> PrepareLiraLoads(FemSchema schema, LiraTextSchema txt, IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> elements)
    {
        ApplyLiraBarAxes(schema, txt, meshNodes, elements);
        var tags = meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
        var types = ElementTypes(elements);
        var cases = schema.LoadCases.ToList();
        var elementLoads = _db.GetFemElementLoads(schema.Id);
        var nodeLoads = _db.GetFemMeshNodeLoads(schema.Id);
        double tonToKn = Lira.TonToKnFactor;
        return () =>
        {
            int next = 0;
            return LiraLoadTransfer.Transfer(txt, tags, types, tonToKn, cases, elementLoads, nodeLoads, () => --next);
        };
    }

    /// <summary>
    /// Повороты сечений импортных стержней — по осям ЛИРЫ с углами документа 17: местные нагрузки по Y1/Z1
    /// прикладываются в осях сетки. Схемы, импортированные до 23.10.2026, углов не имели — исправляются здесь.
    /// </summary>
    void ApplyLiraBarAxes(FemSchema schema, LiraTextSchema txt, IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> elements)
    {
        var nodes = meshNodes.GroupBy(n => n.NodeTag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var rotations = new Dictionary<string, double>(StringComparer.Ordinal);
        var changed = new List<(FemElement Element, double Deg)>();
        foreach (var e in elements.Where(e => e.ElemType == "beam" && e.Origin == FemMember.MeshSourceImported))
        {
            if (e.Node1 is not { } n1 || e.Node2 is not { } n2
                || !nodes.TryGetValue(n1.ToString(CultureInfo.InvariantCulture), out var a)
                || !nodes.TryGetValue(n2.ToString(CultureInfo.InvariantCulture), out var b)
                || !int.TryParse(e.ElemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) continue;
            double? deg = LiraSchemaConverter.BarRotationDeg(new LiraNodeRecord(0, a.X, a.Y, a.Z, 0),
                new LiraNodeRecord(0, b.X, b.Y, b.Z, 0), txt.ElementAngles.GetValueOrDefault(number));
            if (deg is not { } d) continue;
            rotations[e.ElemTag] = d;
            if (e.BeamRotationDeg is not { } old || Math.Abs(Math.IEEERemainder(old - d, 360)) > 1e-6) changed.Add((e, d));
        }
        if (changed.Count == 0) return;
        _db.ReplaceFemElementBeamRotations(schema.Id, rotations);
        foreach (var (e, d) in changed) e.BeamRotationDeg = d;
        _log.Info(string.Format(Loc.S("LiraBarAxesUpdated"), changed.Count));
    }

    /// <summary>Нагрузки текстового файла (с заменой перенесённых раньше): перенос и ΣF — через <paramref name="work"/>.</summary>
    public async Task<FemLoadTransferOutcome> TransferLiraLoads(FemSchema schema, LiraTextSchema txt,
        IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> elements, IImportWork work)
    {
        var result = await work.Run(PrepareLiraLoads(schema, txt, meshNodes, elements));
        var totals = await work.Run(SaveLoadTransfer(schema, result, LiraLoadTransfer.Origin, meshNodes, elements));
        return FinishLoadTransfer(schema, "LiraLoadsTotal", totals);
    }

    /// <summary>
    /// ГУ и нагрузки текстового файла в только что импортированную схему; путь файла — в <see cref="FemSchema.SourcePath"/>.
    /// </summary>
    /// <param name="raw">Данные импорта (C1 пластин из API).</param>
    public async Task<(FemBoundarySummary Boundary, FemLoadTransferOutcome Loads)> ApplyLiraTextToNewSchema(FemSchema schema,
        LiraSchemaData raw, LiraTextSchema txt, string path, IImportWork work)
    {
        var meshNodes = _db.GetFemMeshNodes(schema.Id);
        var elements = _db.GetFemMeshElements(schema.Id);
        var c1 = LiraSchemaConverter.ToFemElementBoundaryProps(raw)
            .ToDictionary(kv => kv.Key, kv => kv.Value.FoundationC1!.Value, StringComparer.Ordinal);
        var boundary = TransferLiraBoundary(schema, txt, meshNodes, elements, c1.Count > 0 ? c1 : null);
        var loads = await TransferLiraLoads(schema, txt, meshNodes, elements, work);
        _db.UpdateFemSchemaSourcePath(schema, Path.GetFullPath(path));
        return (boundary, loads);
    }

    /// <summary>
    /// Схема целиком из текстового файла ЛИРЫ (без COM): геометрия, жёсткости, блоки, ГУ и нагрузки. Ошибка — схема
    /// удаляется. Файл без геометрии — <see cref="InvalidDataException"/>.
    /// </summary>
    public async Task<(FemSchema Schema, string Done, FemBoundarySummary Boundary, FemLoadTransferOutcome Loads)> ImportLiraText(
        LiraTextSchema txt, string path, string? tag, IImportWork work)
    {
        if (txt.Nodes.Count == 0 || txt.Elements.Count == 0)
            throw new InvalidDataException(string.Format(Loc.S("LiraTextNoGeometry"), Path.GetFileName(path)));
        var raw = await LiraTextToSchemaData(txt, path, work);
        string title = tag ?? (txt.Title.Trim() is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(path));
        int groups = 0;
        FemBoundarySummary? boundary = null;
        FemLoadTransferOutcome? loads = null;
        var schema = await CreateAsync(title, "lira", null, async s =>
        {
            groups = SaveLiraMesh(s, raw);
            (boundary, loads) = await ApplyLiraTextToNewSchema(s, raw, txt, path, work);
            WarnFoundationBeyondC1(raw);
            SaveLiraExtras(s, raw);
        });
        return (schema, LiraDone(raw, groups), boundary!, loads!);
    }

    // ── SCAD ────────────────────────────────────────────────────────────────────

    /// <summary>Схема из текстового файла SCAD (топология): узлы, КЭ, группы.</summary>
    public (FemSchema Schema, string Done) ImportScadText(ScadSchemaData data, string tag, string? sourcePath)
    {
        CScore.Fem.FemMeshNode[] meshNodes = [];
        FemElement[] meshElements = [];
        FemMemberGroup[] groups = [];
        var schema = Create(tag, "scad", sourcePath, s =>
        {
            meshNodes = ScadSchemaConverter.ToFemMeshNodes(data, s.Id);
            meshElements = ScadSchemaConverter.ToFemMeshElements(data, s.Id);
            groups = ScadSchemaConverter.ToFemMemberGroups(data, s.Id);
            SaveMesh(s, CScore.Fem.Import.FemImportResult.MeshOnly(meshNodes, meshElements, groups));
        });
        return (schema, string.Format(Loc.S("ImportScadSuccess"), meshNodes.Length,
            meshElements.Count(e => e.ElemType == "beam"), meshElements.Count(e => e.ElemType == "shell"), groups.Length,
            data.Groups.Count));
    }

    /// <summary>Параметры записи схемы SCAD, прочитанной через SCADAPIX.dll.</summary>
    public sealed record ScadApiImportOptions(bool ConcreteGroupsAsMemberGroups, bool SteelGroupsAsMemberGroups, string? PrfDirectory);

    /// <summary>
    /// Схема из данных SCAD API: сетка, группы (блоки, ЖБ- и стальные группы), жёсткости, вложения, ГУ и нагрузки.
    /// Ошибка — схема удаляется.
    /// </summary>
    public (FemSchema Schema, string Done, FemBoundarySummary? Boundary, FemLoadTransferOutcome? Loads) ImportScadApi(
        ScadSchemaData data, string sprPath, ScadApiImportOptions options)
    {
        string done = "";
        FemBoundarySummary? boundary = null;
        FemLoadTransferOutcome? loads = null;
        var schema = Create(Path.GetFileNameWithoutExtension(sprPath), "scad", Path.GetFullPath(sprPath), s =>
        {
            var meshNodes = ScadSchemaConverter.ToFemMeshNodes(data, s.Id);
            var meshElements = ScadSchemaConverter.ToFemMeshElements(data, s.Id);
            var blockGroups = ScadSchemaConverter.ToFemMemberGroupsByBlocks(data, s.Id);
            var concreteGroups = options.ConcreteGroupsAsMemberGroups ? ScadSchemaConverter.ToFemMemberGroupsByConcreteGroups(data, s.Id) : [];
            var steelGroups = options.SteelGroupsAsMemberGroups ? ScadSchemaConverter.ToFemMemberGroupsBySteelGroups(data, s.Id) : [];
            var memberGroups = ScadSchemaConverter.ToFemMemberGroups(data, s.Id)
                .Concat(blockGroups).Concat(concreteGroups).Concat(steelGroups).ToArray();
            var stiffnesses = ScadSchemaConverter.ToSchemaStiffnesses(data);

            SaveMesh(s, CScore.Fem.Import.FemImportResult.MeshOnly(meshNodes, meshElements, memberGroups));
            _db.SaveFemSchemaStiffnesses(s.Id, stiffnesses);
            SaveSourceFile(s.Id, FemSchemaSourceFileKind.ScadConcreteGroups, ScadConcreteGroupIndex.ToJson(data.ConcreteGroups));
            if (data.SteelGroups.Count > 0)
                SaveSourceFile(s.Id, FemSchemaSourceFileKind.ScadSteelGroups, ScadSteelGroupIndex.ToJson(data.SteelGroups));
            SaveScadSteelProfiles(s.Id, stiffnesses, options.PrfDirectory);
            if (data.AssignedRebar != null)
                SaveSourceFile(s.Id, FemSchemaSourceFileKind.ScadAssignedRebar, data.AssignedRebar.ToJson());
            if (data.AnalysisModel != null)
            {
                SaveSourceFile(s.Id, FemSchemaSourceFileKind.ScadAnalysisModel, data.AnalysisModel.ToJson());
                loads = TransferScadLoads(s, data.AnalysisModel);
                boundary = TransferScadBoundary(s, data.AnalysisModel);
            }
            done = string.Format(Loc.S("ScadApiImportSuccess"), s.Tag, meshNodes.Length,
                meshElements.Count(e => e.ElemType == "beam"), meshElements.Count(e => e.ElemType == "shell"),
                memberGroups.Length, blockGroups.Length, concreteGroups.Length, stiffnesses.Length, steelGroups.Length);
        });
        return (schema, done, boundary, loads);
    }

    /// <summary>ЖБ-группы SCAD — вложение схемы.</summary>
    public void SaveScadConcreteGroups(int schemaId, IReadOnlyCollection<ScadConcreteGroup> groups) =>
        SaveSourceFile(schemaId, FemSchemaSourceFileKind.ScadConcreteGroups, ScadConcreteGroupIndex.ToJson(groups));

    /// <summary>Стальные группы SCAD — вложение схемы.</summary>
    public void SaveScadSteelGroups(int schemaId, IReadOnlyCollection<ScadSteelGroup> groups) =>
        SaveSourceFile(schemaId, FemSchemaSourceFileKind.ScadSteelGroups, ScadSteelGroupIndex.ToJson(groups));

    /// <summary>Заданное армирование SCAD — вложение схемы.</summary>
    public void SaveScadAssignedRebar(int schemaId, ScadAssignedRebarFile file) =>
        SaveSourceFile(schemaId, FemSchemaSourceFileKind.ScadAssignedRebar, file.ToJson());

    void SaveSourceFile(int schemaId, string kind, string json) =>
        _db.SaveFemSchemaSourceFile(schemaId, kind, "", Encoding.UTF8.GetBytes(json));

    /// <summary>Стальные профили жёсткостей STZ по сортаментам SCAD (PRF); профили без сортамента — предупреждение.</summary>
    public void SaveScadSteelProfiles(int schemaId, IEnumerable<LiraStiffnessRecord> stiffnesses, string? prfDirectory)
    {
        var entries = Scad.ScadSteelProfileLoader.Resolve(stiffnesses, prfDirectory);
        if (entries.Count == 0) return;
        SaveSourceFile(schemaId, FemSchemaSourceFileKind.ScadSteelProfiles, SteelProfileIndex.ToJson(entries));
        var failed = entries.Where(e => e.Shape == null).ToList();
        _log.Info(string.Format(Loc.S("ScadSteelProfilesLoaded"), entries.Count - failed.Count, entries.Count));
        foreach (var e in failed)
            _log.Warning(string.Format(Loc.S("ScadSteelProfileFailed"), e.Num, e.Source, e.Reason));
    }

    /// <summary>Вложение SCAD схемы (закрепления, жёсткие тела, нагрузки); null — нет или повреждено (в журнал).</summary>
    public ScadAnalysisModel? LoadScadAnalysisModel(int schemaId)
    {
        if (_db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAnalysisModel) is not { } file) return null;
        try { return ScadAnalysisModel.FromJson(Encoding.UTF8.GetString(file.Data)); }
        catch (InvalidDataException ex) { _log.Error(ex.Message); return null; }
    }

    /// <summary>Сохраняет вложение SCAD (после повторного чтения .SPR).</summary>
    public void SaveScadAnalysisModel(int schemaId, ScadAnalysisModel model) =>
        SaveSourceFile(schemaId, FemSchemaSourceFileKind.ScadAnalysisModel, model.ToJson());

    /// <summary>Нагрузки вложения SCAD в сеточный уровень схемы (с заменой перенесённого ранее).</summary>
    public FemLoadTransferOutcome TransferScadLoads(FemSchema schema, ScadAnalysisModel model)
    {
        var meshNodes = _db.GetFemMeshNodes(schema.Id);
        var elements = _db.GetFemMeshElements(schema.Id);
        int next = 0;
        var result = ScadLoadTransfer.Transfer(model, ElementTypes(elements), schema.LoadCases.ToList(),
            _db.GetFemElementLoads(schema.Id), _db.GetFemMeshNodeLoads(schema.Id), () => --next);
        var totals = SaveLoadTransfer(schema, result, ScadLoadTransfer.Origin, meshNodes, elements)();
        return FinishLoadTransfer(schema, "ScadLoadsTotal", totals);
    }

    /// <summary>ГУ вложения SCAD в сеточный уровень схемы (с заменой перенесённых раньше; ручные не трогает).</summary>
    public FemBoundarySummary TransferScadBoundary(FemSchema schema, ScadAnalysisModel model)
    {
        var meshNodes = _db.GetFemMeshNodes(schema.Id);
        var elements = _db.GetFemMeshElements(schema.Id);
        var result = ScadBoundaryTransfer.Transfer(model, meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal),
            ElementTypes(elements));
        return SaveBoundaryTransfer(schema, result, ScadBoundaryTransfer.Origin, meshNodes);
    }

    /// <summary>Повороты сечений стержней по осям SCAD (повторное чтение .SPR).</summary>
    public void ApplyScadRodAxes(FemSchema schema, ScadSchemaData data)
    {
        var rotations = ScadSchemaConverter.BeamRotations(data)
            .ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => kv.Value, StringComparer.Ordinal);
        int updated = _db.ReplaceFemElementBeamRotations(schema.Id, rotations);
        _log.Info(string.Format(Loc.S("ScadRodAxesUpdated"), updated, data.RodAxes.Count));
    }

    // ── Общий перенос ГУ и нагрузок ─────────────────────────────────────────────

    /// <summary>
    /// Записывает итог переноса ГУ (происхождение <paramref name="origin"/>; ручные ГУ не трогаются). Журнал: сводка,
    /// непереносимое, проверки резолвера. Результаты постановок сбрасываются.
    /// </summary>
    public FemBoundarySummary SaveBoundaryTransfer(FemSchema schema, FemBoundaryTransferResult result, string origin,
        IReadOnlyList<FemMeshNode> meshNodes)
    {
        var manual = _db.ManualMeshNodeTags(schema.Id);
        int keptManual = result.Supports.Select(x => x.NodeTag)
            .Concat(result.Springs.Where(x => x.TargetKind == FemSpringTargetKinds.MeshNode).Select(x => x.NodeTag))
            .Where(manual.Contains).Distinct().Count();
        _db.SaveFemBoundary(schema.Id, origin, result.Supports, result.Springs, result.RigidBodies, result.ElementProps);

        _log.Info(result.Report[0]);
        foreach (var line in result.Report.Skip(1)) _log.Warning(line);
        if (keptManual > 0) _log.Info(string.Format(Loc.S("FemBoundaryManualKept"), keptManual));

        // ГУ КЭ записаны в БД — резолверу нужна сетка после записи; C1 и пружины — с учётом полей и ручных правок.
        var effective = FemFieldService.Effective(_db, schema.Id);
        foreach (var d in effective.Diagnostics)
        {
            string text = d.Tags.Count > 0 ? $"{d.Message}: {d.Tags.Count}" : d.Message;
            if (d.Code == "field_recomputed") _log.Info(text); else _log.Warning(text);
        }
        var resolved = FemBoundaryResolver.Resolve(_db.GetFemNodes(schema.Id), meshNodes, effective.Elements,
            _db.GetFemMeshNodeSupports(schema.Id), effective.Springs.ToList(), _db.GetFemRigidBodies(schema.Id));
        foreach (var d in resolved.Diagnostics.Take(10))
            if (d.IsError) _log.Error(d.Message); else _log.Warning(d.Message);
        if (resolved.Diagnostics.Count > 10)
            _log.Warning(string.Format(Loc.S("ScadBoundaryMoreDiagnostics"), resolved.Diagnostics.Count - 10));

        int invalidated = InvalidateAnalyses(schema);
        if (invalidated > 0) _log.Info(string.Format(Loc.S("ScadBoundaryAnalysesInvalidated"), invalidated));

        var messages = result.Report.Skip(1).Concat(resolved.Diagnostics.Select(d => d.Message)).ToList();
        if (keptManual > 0) messages.Insert(0, string.Format(Loc.S("FemBoundaryManualKept"), keptManual));
        string header = string.Format(Loc.S(origin == LiraBoundaryTransfer.Origin ? "FemBoundarySummaryHeaderLira"
            : "FemBoundarySummaryHeader"), schema.Tag);
        return new FemBoundarySummary(header, result.Summary, messages);
    }

    /// <summary>
    /// Фаза записи переноса нагрузок: загружения и нагрузки сеточного уровня, сводка — в журнал. Возвращает расчёт ΣF
    /// загружений происхождения <paramref name="origin"/> без обращений к базе (можно в фоне).
    /// </summary>
    public Func<List<FemLoadCaseTotal>> SaveLoadTransfer(FemSchema schema, FemLoadTransferResult result, string origin,
        IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> elements)
    {
        _db.SaveFemLoadCasesAndMeshLoads(schema.Id, result.LoadCases, result.ElementLoads, result.MeshNodeLoads);
        _log.Info(result.Report[0]);
        foreach (var line in result.Report.Skip(1)) _log.Warning(line);

        var mesh = new CScore.Fem.Loads.FemLoadMeshContext(meshNodes.ToList(), elements.ToList(), schema.MemberGroups.ToList(),
            FemSelfWeightSourceFactory.Create(_db, schema.Id));
        var cases = result.LoadCases.Where(c => c.Origin == origin).ToList();
        return () => cases.Select(lc =>
        {
            var forces = CScore.Fem.Loads.FemLoadCaseNodalForces.Resolve(lc, result.ElementLoads, result.MeshNodeLoads, mesh);
            var (fx, fy, fz) = forces.Total;
            return new FemLoadCaseTotal(lc.Tag, lc.SourceLoadNum, fx / 1e3, fy / 1e3, fz / 1e3,
                forces.Diagnostics.Select(d => d.Message).Distinct().Take(10).ToList());
        }).ToList();
    }

    /// <summary>ΣF загружений — в журнал (строка <paramref name="totalKey"/>); результаты постановок сбрасываются.</summary>
    public FemLoadTransferOutcome FinishLoadTransfer(FemSchema schema, string totalKey, List<FemLoadCaseTotal> totals)
    {
        foreach (var t in totals)
        {
            _log.Info(string.Format(Loc.S(totalKey), t.Case, t.SourceNum, t.FxKn, t.FyKn, t.FzKn));
            foreach (var d in t.Diagnostics) _log.Warning($"«{t.Case}»: {d}");
        }
        int invalidated = InvalidateAnalyses(schema);
        if (invalidated > 0) _log.Info(string.Format(Loc.S("ScadLoadsAnalysesInvalidated"), invalidated));
        bool ask = calc.ToSp20LoadValueKind() != CScore.Combinations.Sp20LoadValueKind.Design;
        return new FemLoadTransferOutcome(totals, ask);
    }

    static Dictionary<string, string> ElementTypes(IEnumerable<FemElement> elements) =>
        elements.GroupBy(e => e.ElemTag, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ElemType, StringComparer.Ordinal);
}
