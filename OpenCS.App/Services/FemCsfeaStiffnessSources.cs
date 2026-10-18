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
