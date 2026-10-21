using System.Globalization;
using System.IO;
using CScore;
using CScore.Fem;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Пригодный источник жёсткостей КЭ линейного расчёта CSfea (спека 2026-10-17 «инженерная нелинейность», п. 2.2):
/// запись конечного состояния секущей постановки схемы, дошедшего варианта пакета или длительной части варианта.
/// <see cref="Calc"/> — вид расчёта, на диаграммах которого получены жёсткости (набор жёсткостей); <see cref="State"/> —
/// итог прогона для подписи («дошёл», «остановлен на стадии …»).
/// </summary>
public sealed record FemCsfeaStiffnessCandidate(FemAnalysis Analysis, CalcType Calc, int? BatchIndex, string? LongKind,
    string Label, CalcResult Result, int RecordN, bool Completed, string State)
{
    /// <summary>Конечное состояние постановки (не вариант пакета).</summary>
    public bool IsFinalState => BatchIndex == null;

    /// <summary>Тот же источник (постановка, вариант, длительная часть) без учёта отпечатка.</summary>
    public bool Matches(FemCsfeaStiffnessSource? s) => s != null && s.AnalysisId == Analysis.Id && s.BatchIndex == BatchIndex
        && s.LongKind == LongKind;

    /// <summary>«Стадии NL», конечное состояние, вид NL, 18.10.2026 14:02 — дошёл».</summary>
    public string Describe() => $"«{Analysis.Tag}», {Label}, вид {Calc}, {FemCsfeaStiffnessSources.FormatCreated(Result.Created)} — {State}";
}

/// <summary>
/// Источник жёсткостей, проверенный для расчёта: запись конечного состояния (<see cref="Fields"/>, с полными матрицами),
/// определяющая нагрузка (загружение → коэффициент; null — не строится) и геометрическая нелинейность источника:
/// включена, большие перемещения (CR), P-Δ оболочек в своей плоскости.
/// </summary>
public sealed record FemCsfeaStiffnessSourceData(FemCsfeaStiffnessCandidate Candidate,
    CSfea.CScoreBridge.Structural.RcSecantStepFields Fields, IReadOnlyDictionary<int, double>? DeterminingLoad, bool GeomNonlinear,
    bool LargeDisplacements = false, bool ShellInPlanePDelta = true);

/// <summary>Состояние выбранного источника жёсткостей относительно текущих записей БД.</summary>
public enum FemCsfeaStiffnessSourceStatus
{
    /// <summary>Запись та же, что при выборе.</summary>
    Current,
    /// <summary>Источник пересчитан после выбора (другое время расчёта или запись).</summary>
    Changed,
    /// <summary>Постановки, результата или записи конечного состояния больше нет.</summary>
    Missing,
}

/// <summary>
/// Источники жёсткостей КЭ линейного расчёта CSfea: список пригодных записей схемы, отпечаток выбранной записи и
/// проверка её актуальности. Пригодна запись конечного состояния секущего прогона — в ней полные секущие матрицы КЭ
/// (<c>RcSecantStepFields.HasMatrices</c>, записи с 17.10.2026); наличие матриц проверяется при снятии отпечатка (для
/// этого запись распаковывается).
/// </summary>
public static class FemCsfeaStiffnessSources
{
    /// <summary>Пригодные источники секущих постановок схемы по порядку постановок.</summary>
    public static List<FemCsfeaStiffnessCandidate> List(DatabaseService db, int schemaId)
    {
        var list = new List<FemCsfeaStiffnessCandidate>();
        foreach (var a in db.GetFemAnalyses(schemaId))
        {
            if (a.Kind != FemCsfeaRunner.AnalysisKind || a.ResultId is not int id) continue;
            list.AddRange(Candidates(a, db.GetCalcResultById(id)));
        }
        return list;
    }

    /// <summary>
    /// Источники одной постановки по её результату: у стадийной — конечное состояние (последний сошедшийся шаг с
    /// записанными полями), у пакета с хранением полей — посчитанные варианты с записью и длительные части своим
    /// прогоном. Не секущий результат, ошибка, пустой — пусто.
    /// </summary>
    public static List<FemCsfeaStiffnessCandidate> Candidates(FemAnalysis analysis, CalcResult? result)
    {
        var list = new List<FemCsfeaStiffnessCandidate>();
        if (analysis.Kind != FemCsfeaRunner.AnalysisKind || result is not { TaskKind: FemCsfeaRunner.TaskKind }
            || result.Status == "error" || FemCsfeaResultSummary.Parse(result.DataJson) is not { } summary
            || summary.Kind != FemCsfeaRunner.AnalysisKind)
            return list;
        var calc = FemAnalysisParams.Parse(analysis.ParamsJson).CalcType ?? CalcType.N;
        var culture = CultureInfo.CurrentCulture;

        if (summary.Batch is not { } batch)
        {
            var end = summary.Steps.LastOrDefault(s => s.Converged);
            if (end is not { Fields: true }) return list;
            string state = summary.Completed ? "дошёл"
                : string.Create(culture, $"остановлен на стадии «{StageTag(summary, end.Stage)}», λ = {end.LoadFactor:0.###}");
            list.Add(new(analysis, calc, null, null, "конечное состояние", result, end.N, summary.Completed, state));
            return list;
        }

        string max = summary.BatchMaxLoadFactor.ToString("0.###", culture);
        string BatchState(FemCsfeaBatchSummary b) => b.Completed ? "дошёл"
            : string.Create(culture, $"λ = {b.LoadFactor:0.###} из {max} — не дошёл");
        var variants = new Dictionary<int, FemCsfeaBatchSummary>();
        foreach (var b in batch)
        {
            variants[b.Index] = b;
            if (b.Done && b.RecordN is int n && b.LoadFactor > 0)
                list.Add(new(analysis, calc, b.Index, null, b.Label, result, n, b.Completed, BatchState(b)));
        }
        // Длительные части — только своим прогоном: совпадающая с полной — та же запись варианта, нулевая — без записи.
        foreach (var p in summary.BatchLong ?? [])
        {
            if (!p.IsDerived || !p.Done || p.RecordN is not int n || p.LoadFactor <= 0 || p.PairOf is not int of
                || !variants.TryGetValue(of, out var v))
                continue;
            bool deflection = p.Kind == FemCsfeaBatchLongKind.Deflection;
            string label = deflection ? $"{v.Label} — длительная часть {p.Type} на диаграммах NL" : $"{v.Label} — длительная часть {p.Type}";
            list.Add(new(analysis, deflection ? CalcType.NL : calc, of, p.Kind, label, result, n, p.Completed, BatchState(p)));
        }
        return list;
    }

    static string StageTag(FemCsfeaResultSummary s, int stage) => stage >= 0 && stage < s.Stages.Count ? s.Stages[stage].Tag : $"{stage + 1}";

    /// <summary>Источник из списка, отвечающий сохранённому (без учёта отпечатка); null — нет.</summary>
    public static FemCsfeaStiffnessCandidate? Find(IEnumerable<FemCsfeaStiffnessCandidate> candidates, FemCsfeaStiffnessSource? source) =>
        source == null ? null : candidates.FirstOrDefault(c => c.Matches(source));

    /// <summary>Отпечаток источника по записи БД; null и текст ошибки — записи нет или в ней нет полных матриц.</summary>
    public static FemCsfeaStiffnessSource? Fingerprint(DatabaseService db, FemCsfeaStiffnessCandidate c, out string? error) =>
        Fingerprint(c, c.Result.Id > 0 ? db.GetFemResultStep(c.Result.Id, c.RecordN) : null, out error);

    /// <summary>Отпечаток источника по сжатой записи <paramref name="record"/> конечного состояния.</summary>
    public static FemCsfeaStiffnessSource? Fingerprint(FemCsfeaStiffnessCandidate c, byte[]? record, out string? error)
    {
        error = null;
        if (record == null)
        {
            error = $"У «{c.Analysis.Tag}» ({c.Label}) нет записи конечного состояния — пересчитайте источник.";
            return null;
        }
        try
        {
            if (!FemCsfeaStepFieldsCodec.Unpack(record).HasMatrices)
            {
                error = $"Запись «{c.Analysis.Tag}» ({c.Label}) без полных секущих матриц КЭ (расчёт до 17.10.2026) — пересчитайте источник.";
                return null;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            error = $"Запись «{c.Analysis.Tag}» ({c.Label}) не читается: {ex.Message}";
            return null;
        }
        return new FemCsfeaStiffnessSource
        {
            AnalysisId = c.Analysis.Id, BatchIndex = c.BatchIndex, LongKind = c.LongKind,
            ResultCreated = c.Result.Created, RecordCrc = Crc32(record),
        };
    }

    /// <summary>Совпадает ли отпечаток <paramref name="source"/> с текущим источником и его записью.</summary>
    public static FemCsfeaStiffnessSourceStatus Status(FemCsfeaStiffnessSource source, FemCsfeaStiffnessCandidate? current, byte[]? record)
    {
        if (current == null || !current.Matches(source) || record == null) return FemCsfeaStiffnessSourceStatus.Missing;
        return current.Result.Created == source.ResultCreated && Crc32(record) == source.RecordCrc
            ? FemCsfeaStiffnessSourceStatus.Current : FemCsfeaStiffnessSourceStatus.Changed;
    }

    /// <summary>Состояние источника по БД схемы.</summary>
    public static FemCsfeaStiffnessSourceStatus Status(DatabaseService db, int schemaId, FemCsfeaStiffnessSource source)
    {
        var current = Find(List(db, schemaId), source);
        var record = current is { Result.Id: > 0 } ? db.GetFemResultStep(current.Result.Id, current.RecordN) : null;
        return Status(source, current, record);
    }

    /// <summary>
    /// Источник для расчёта: запись есть, отпечаток совпадает, в записи полные матрицы; определяющая нагрузка —
    /// загружения схемы с коэффициентами в записанном состоянии (стадии до записанной целиком, записанная — долей λ;
    /// у варианта пакета и длительной части — их стадия долей λ). null и понятная ошибка — иначе.
    /// </summary>
    public static FemCsfeaStiffnessSourceData? Load(DatabaseService db, int schemaId, FemCsfeaStiffnessSource source, out string? error)
    {
        error = null;
        var current = Find(List(db, schemaId), source);
        string name = db.GetFemAnalysis(source.AnalysisId)?.Tag is { } tag ? $"«{tag}»" : $"№ {source.AnalysisId}";
        var record = current is { Result.Id: > 0 } ? db.GetFemResultStep(current.Result.Id, current.RecordN) : null;
        switch (Status(source, current, record))
        {
            case FemCsfeaStiffnessSourceStatus.Missing:
                error = $"Источник жёсткостей {name} не найден: секущая постановка удалена, не посчитана или её результат сброшен "
                    + "(например, после перестройки сетки). Посчитайте её и выберите источник в постановке заново.";
                return null;
            case FemCsfeaStiffnessSourceStatus.Changed:
                error = $"Источник жёсткостей {name} пересчитан после выбора. Откройте постановку и подтвердите источник (OK в диалоге) — "
                    + "так видно, что расчёт пойдёт на новых жёсткостях.";
                return null;
        }
        if (Fingerprint(current!, record, out error) == null) return null;
        var fields = FemCsfeaStepFieldsCodec.Unpack(record!);
        var pars = FemAnalysisParams.Parse(current!.Analysis.ParamsJson);
        return new FemCsfeaStiffnessSourceData(current, fields, DeterminingLoad(db, schemaId, current, pars, fields),
            pars.Csfea?.GeomNonlinear == true, pars.Csfea?.GeomLargeDisplacements == true, pars.Csfea?.ShellInPlanePDelta ?? true);
    }

    /// <summary>
    /// Источник среди <paramref name="candidates"/> результата по номеру показанной записи; null — показана не запись
    /// конечного состояния (промежуточный шаг, огибающая, комбинация полей).
    /// </summary>
    public static FemCsfeaStiffnessCandidate? ByRecord(IEnumerable<FemCsfeaStiffnessCandidate> candidates, int? recordN) =>
        recordN is int n ? candidates.FirstOrDefault(c => c.RecordN == n) : null;

    /// <summary>
    /// Постановка «Линейный — CSfea» на жёсткостях источника (команда «Линейный расчёт на этих жёсткостях…», спека
    /// п. 2.3): вид расчёта — как у источника, загружения — все загружения схемы, параметры сечений — как у секущей
    /// постановки (без геометрической нелинейности и шагов записи), имя «ИН — <источник>».
    /// </summary>
    public static FemAnalysis LinearTemplate(FemCsfeaStiffnessCandidate c, FemCsfeaStiffnessSource fingerprint,
        IEnumerable<FemLoadCase> loadCases)
    {
        var source = FemAnalysisParams.Parse(c.Analysis.ParamsJson).Csfea ?? new FemCsfeaParams();
        var csfea = System.Text.Json.JsonSerializer.Deserialize<FemCsfeaParams>(System.Text.Json.JsonSerializer.Serialize(source))!;
        csfea.StiffnessSource = fingerprint;
        csfea.SourcePDelta = source.GeomNonlinear;
        csfea.GeomNonlinear = false;
        csfea.GeomLargeDisplacements = false;
        csfea.PhysicalNonlinearity = true;
        csfea.ResultRecording = FemCsfeaRecording.Final;
        csfea.RecordSteps = "";
        var stages = loadCases.Select(lc => new FemAnalysisStage
        {
            Tag = lc.Tag,
            LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [lc.Id] }.ToJson(),
            LoadFactorStep = 1.0, MaxLoadFactor = 1.0,
        }).ToList();
        var pars = new FemAnalysisParams { CalcType = c.Calc, Stages = stages, Csfea = csfea };
        return new FemAnalysis
        {
            SchemaId = c.Analysis.SchemaId, Kind = FemCsfeaRunner.LinearAnalysisKind,
            Tag = c.IsFinalState ? $"ИН — {c.Analysis.Tag}" : $"ИН — {c.Analysis.Tag}, {c.Label}",
            LoadExpressionJson = stages.FirstOrDefault()?.LoadExpressionJson ?? "{}", ParamsJson = pars.ToJson(),
        };
    }

    /// <summary>
    /// Пометка устаревания линейного результата на жёсткостях секущего: источник пересчитан или пропал после расчёта;
    /// null — источника нет (начальные жёсткости) или запись та же.
    /// </summary>
    public static string? StaleNote(DatabaseService db, int schemaId, FemCsfeaResultSummary summary)
    {
        if (summary.StiffnessSource is not { } source) return null;
        return Status(db, schemaId, source) switch
        {
            FemCsfeaStiffnessSourceStatus.Changed => "Источник жёсткостей пересчитан после этого расчёта — пересчитайте линейную постановку.",
            FemCsfeaStiffnessSourceStatus.Missing => "Источника жёсткостей больше нет (постановка удалена или её результат сброшен) — "
                + "результат показан на прежних жёсткостях; посчитайте источник и выберите его в постановке заново.",
            _ => null,
        };
    }

    /// <summary>Определяющая нагрузка записи источника: номер загружения → коэффициент; null — не строится.</summary>
    static Dictionary<int, double>? DeterminingLoad(DatabaseService db, int schemaId, FemCsfeaStiffnessCandidate c,
        FemAnalysisParams pars, CSfea.CScoreBridge.Structural.RcSecantStepFields fields)
    {
        var loadCases = db.GetFemLoadCases(schemaId);
        var diag = new List<FemValidationDiagnostic>();
        IReadOnlyList<FemAnalysisStage> stages;
        int last;
        if (pars.Batch is not { } batch)
        {
            stages = pars.ResolveStages(c.Analysis);
            last = fields.Stage;
        }
        else if (c.LongKind == null)
        {
            var all = batch.ToStages();
            if (c.BatchIndex is not int i || i >= all.Count) return null;
            stages = [all[i]];
            last = 0;
        }
        else
        {
            var summary = FemCsfeaResultSummary.Parse(c.Result.DataJson);
            var all = FemCsfeaBatchLongTerm.Stages(batch, loadCases);
            if (summary?.LongOf(c.BatchIndex ?? -1, c.LongKind)?.Stage is not int s || s >= all.Count) return null;
            stages = [all[s]];
            last = 0;
        }
        if (last < 0 || last >= stages.Count) return null;
        var adapted = FemCsfeaInputBuilder.Stages(stages.Take(last + 1).ToList(), loadCases, diag);
        if (diag.Any(d => d.IsError)) return null;
        var load = new Dictionary<int, double>();
        for (int s = 0; s <= last; s++)
        {
            double k = s == last ? fields.LoadFactor : 1.0;
            foreach (var (lc, f) in adapted[s].Terms) load[lc] = load.GetValueOrDefault(lc) + k * f;
        }
        return load;
    }

    /// <summary>«18.10.2026 14:02» из времени результата «2026-10-18 14:02:11»; не разобралось — как есть.</summary>
    public static string FormatCreated(string created) =>
        DateTime.TryParseExact(created, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : created;

    static readonly uint[] CrcTable = BuildCrcTable();

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    /// <summary>CRC-32 (IEEE 802.3, как у zip и gzip).</summary>
    public static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}
