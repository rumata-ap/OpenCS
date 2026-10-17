using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Fem.Combinations;
using CScore.Import;
using CSfea.Core;
using CSfea.CScoreBridge.Structural;
using OpenCS.Gmsh;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Шаг в сводке секущего расчёта: сквозной номер (по всем шагам, с дроблениями), стадия, доля стадии, сходимость,
/// перемещение контрольного узла; <see cref="Planned"/> — номер шага по плану постановки (сквозной по стадиям, без
/// дроблений; null — промежуточный шаг дробления), <see cref="Fields"/> — записаны полные поля шага;
/// <see cref="ReactionSums"/> — ΣRx, ΣRy, ΣRz (Н) по типам опор (связи, пружины, C1, автозакрепления — подряд по 3,
/// <see cref="RcReactionSet.Sums"/>), <see cref="LoadSum"/> — ΣFx, ΣFy, ΣFz (Н) нагрузки шага; null — старая сводка.
/// </summary>
public sealed record FemCsfeaStepSummary(int N, int Stage, int Step, double LoadFactor, bool Refinement, bool Converged,
    int Iterations, double Residual, double? Control, int? Planned = null, bool Fields = false,
    double[]? ReactionSums = null, double[]? LoadSum = null)
{
    /// <summary>ΣR по всем типам опор (3 компоненты); null — нет данных.</summary>
    public double[]? ReactionTotal => ReactionSums is { Length: RcReactionSet.KindCount * 3 } s
        ? Enumerable.Range(0, 3).Select(c => Enumerable.Range(0, RcReactionSet.KindCount).Sum(k => s[3 * k + c])).ToArray()
        : null;
}

/// <summary>
/// Отбор шагов для записи полей по <see cref="FemCsfeaParams.ResultRecording"/>: «все» — каждый принятый шаг;
/// «выбранные» — шаги плана из <see cref="FemCsfeaParams.RecordSteps"/> и, по флажку, концы стадий; конечный
/// (последний принятый) шаг записывается всегда. Поля снимаются и сжимаются в потоке расчёта.
/// </summary>
public sealed class FemCsfeaFieldRecorder
{
    readonly string _mode;
    readonly HashSet<int> _selected;
    readonly bool _stageEnds;
    readonly int[] _stageSteps;
    int _accepted;

    public FemCsfeaFieldRecorder(FemCsfeaParams p, IReadOnlyList<int> stageSteps)
    {
        _mode = p.ResultRecording;
        _selected = (FemCsfeaParams.ParseRecordSteps(p.RecordSteps, out _) ?? []).ToHashSet();
        _stageEnds = p.RecordStageEnds;
        _stageSteps = stageSteps.Select(n => Math.Max(1, n)).ToArray();
    }

    /// <summary>Сжатые поля по сквозному номеру шага сводки.</summary>
    public SortedDictionary<int, byte[]> Records { get; } = new();

    /// <summary>Номер шага по плану (сквозной по стадиям); null — промежуточный шаг дробления.</summary>
    public int? Planned(int stage, double loadFactor, bool refinement)
    {
        if (refinement || stage < 0 || stage >= _stageSteps.Length) return null;
        return _stageSteps.Take(stage).Sum() + (int)Math.Round(loadFactor * _stageSteps[stage]);
    }

    bool Wanted(CSfea.Core.SecantStepResult s)
    {
        if (_mode == FemCsfeaRecording.All) return true;
        if (_mode != FemCsfeaRecording.Selected) return false;
        if (s.IsRefinement) return false;
        if (_stageEnds && Math.Abs(s.LoadFactor - 1) < 1e-12) return true;
        return Planned(s.Stage, s.LoadFactor, false) is int n && _selected.Contains(n);
    }

    /// <summary>Принятый шаг (<see cref="RcSecantOptions.OnStep"/>).</summary>
    public void OnStep(CSfea.Core.SecantStepResult s, Func<RcSecantStepFields> fields)
    {
        _accepted++;
        if (Wanted(s)) Records[_accepted] = FemCsfeaStepFieldsCodec.Pack(fields());
    }

    /// <summary>Конечный шаг после расчёта — поля по состояниям КЭ последнего принятого шага.</summary>
    public void RecordFinal(RcSecantRun run)
    {
        int last = run.Result.Steps.FindLastIndex(s => s.Converged);
        if (last < 0 || Records.ContainsKey(last + 1)) return;
        if (run.LastConvergedFields() is { } f) Records[last + 1] = FemCsfeaStepFieldsCodec.Pack(f);
    }
}

/// <summary>
/// Вариант пакетного расчёта в сводке (<see cref="FemCsfeaResultSummary.Batch"/>): элемент пакета и итог его расчёта.
/// <see cref="Done"/> — расчёт варианта завершён (дошёл до предела λ или остановился на несходимости); не посчитанный
/// (отмена, остановка пакета) — false. <see cref="RecordN"/> — номер записи полей конечного состояния в
/// <c>fem_result_steps</c> (null — не записаны).
/// </summary>
public sealed class FemCsfeaBatchSummary
{
    public int Index { get; set; }
    public string Tag { get; set; } = "";
    public string Kind { get; set; } = Tasks.FemCsfeaBatchItemKind.Combination;
    public string Type { get; set; } = "";
    public string ExpressionJson { get; set; } = "{}";
    public bool Done { get; set; }
    /// <summary>Дошёл до предела λ.</summary>
    public bool Completed { get; set; }
    /// <summary>Достигнутый коэффициент нагрузки λ (последний сошедшийся шаг).</summary>
    public double LoadFactor { get; set; }
    public int Steps { get; set; }
    public int Iterations { get; set; }
    public double ElapsedSeconds { get; set; }
    /// <summary>Перемещение контрольного узла конечного шага (м или рад).</summary>
    public double? Control { get; set; }
    public double[]? ReactionSums { get; set; }
    public double[]? LoadSum { get; set; }
    /// <summary>Суммарная нагрузка варианта при λ = предел, Н.</summary>
    public double Fx { get; set; }
    public double Fy { get; set; }
    public double Fz { get; set; }
    /// <summary>Почему не дошёл до предела λ (сообщение решателя).</summary>
    public string? Reason { get; set; }
    public int? RecordN { get; set; }

    /// <summary>Запись длительной части (<see cref="FemCsfeaResultSummary.BatchLong"/>): Index варианта; у варианта — null.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? PairOf { get; set; }
    /// <summary>Вид длительной части (<see cref="CScore.Fem.Combinations.FemLongTermPairKind"/>); у варианта — null.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PairKind { get; set; }
    /// <summary>Стадия адаптера прогона длительной части; null — без прогона (совпадает с полной, нулевая).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? Stage { get; set; }

    /// <summary>Длительная часть равна полной нагрузке — поля и итог — у варианта.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSameAsTotal => PairKind == nameof(CScore.Fem.Combinations.FemLongTermPairKind.SameAsTotal);
    /// <summary>Длительная часть нулевая.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsZero => PairKind == nameof(CScore.Fem.Combinations.FemLongTermPairKind.Zero);
    /// <summary>Длительная часть со своим прогоном.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDerived => PairKind == nameof(CScore.Fem.Combinations.FemLongTermPairKind.Derived);

    /// <summary>Группа огибающей: тип комбинации или <see cref="FemCsfeaBatchGroup.LoadCases"/>; у длительной части — группа пар.</summary>
    public string GroupKey => PairOf != null ? FemCsfeaBatchGroup.PairsOf(Type)
        : Kind == Tasks.FemCsfeaBatchItemKind.LoadCase ? FemCsfeaBatchGroup.LoadCases
        : string.IsNullOrEmpty(Type) ? "C" : Type;

    /// <summary>«[C] Огибающая C 017».</summary>
    public string Label => string.IsNullOrEmpty(Type) ? Tag : $"[{Type}] {Tag}";
}

/// <summary>
/// Огибающая группы пакета, записанная при хранении «только огибающие»: ключ (тип комбинации C…A или
/// <see cref="LoadCases"/>), номер записи (<see cref="FemCsfeaEnvelopeCodec"/>), сколько вариантов вошло.
/// </summary>
public sealed record FemCsfeaBatchGroup(string Key, int RecordN, int Count)
{
    /// <summary>Ключ группы загружений.</summary>
    public const string LoadCases = "LC";
    /// <summary>Ключ группы длительных частей типа NL или CL: «NL-пары» (решение 3 спеки длительных пар).</summary>
    public static string PairsOf(string longType) => longType + "-pairs";
    /// <summary>Номера записей огибающих — после номеров полей вариантов.</summary>
    public const int RecordBase = 100000;
}

/// <summary>Вид записи длительной части в <see cref="FemCsfeaResultSummary.BatchLong"/>.</summary>
public static class FemCsfeaBatchLongKind
{
    /// <summary>NL (CL)-нагрузка на диаграммах варианта: трещины, φ1 и ψ, наборы (пп. 6.1.23, 6.1.26).</summary>
    public const string Part = "long_part";
    /// <summary>NL-нагрузка на диаграммах NL: полный прогиб (пп. 6.1.25, 8.2.24).</summary>
    public const string Deflection = "long_deflection";
}

/// <summary>Прогресс пакетного расчёта: номер варианта (с 1) из числа считаемых, его тег, прогресс решателя.</summary>
public sealed record FemCsfeaBatchProgress(int Number, int Count, string Tag, SecantProgress Step);

/// <summary>Стадия в сводке: тег, заданное число шагов, суммарная нагрузка (Н) в глобальных осях.</summary>
public sealed record FemCsfeaStageSummary(string Tag, int Steps, double Fx, double Fy, double Fz);

/// <summary>
/// Сводка секущего расчёта CSfea — DataJson <see cref="CalcResult"/> вида <see cref="FemCsfeaRunner.TaskKind"/>. Пишется
/// всегда для всех шагов; полные поля шагов — отдельными записями (срез 4е). При ошибке до расчёта заполнены только
/// <see cref="Errors"/> и <see cref="Report"/>.
/// </summary>
public sealed class FemCsfeaResultSummary
{
    public string Kind { get; set; } = FemCsfeaRunner.AnalysisKind;
    public bool Completed { get; set; }
    /// <summary>Стадия (с 0), на которой шаг не сошёлся и после дроблений; null — нет.</summary>
    public int? LimitStage { get; set; }
    public string? Message { get; set; }
    public double ElapsedSeconds { get; set; }
    public int Nodes { get; set; }
    public int Shells { get; set; }
    public int Beams { get; set; }
    public List<FemCsfeaStageSummary> Stages { get; set; } = [];
    public List<FemCsfeaStepSummary> Steps { get; set; } = [];
    /// <summary>Тег узла сетки контрольного узла (пусто — не задан) и его DOF.</summary>
    public string ControlNodeTag { get; set; } = "";
    /// <summary>Тег узла схемы контрольного узла (пусто — задан узлом сетки без узла схемы).</summary>
    public string ControlSchemaNodeTag { get; set; } = "";
    public int ControlDof { get; set; }
    /// <summary>Отчёт подготовки и адаптера: ошибки, предупреждения, сведения.</summary>
    public List<string> Report { get; set; } = [];
    /// <summary>Ошибки, из-за которых расчёт не запускался (формат ошибок прочих FEM-результатов).</summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>Варианты пакетного расчёта; null — не пакет.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<FemCsfeaBatchSummary>? Batch { get; set; }
    /// <summary>
    /// Длительные части вариантов (спека 2026-10-17 «длительные пары»): <see cref="FemCsfeaBatchSummary.PairOf"/> —
    /// Index варианта, <see cref="FemCsfeaBatchSummary.Kind"/> — <see cref="FemCsfeaBatchLongKind"/>, Type — NL/CL;
    /// null — не считались.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<FemCsfeaBatchSummary>? BatchLong { get; set; }
    /// <summary>Хранение пакета (<see cref="Tasks.FemCsfeaBatchRecording"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? BatchRecording { get; set; }
    /// <summary>Предел λ вариантов пакета.</summary>
    public double BatchMaxLoadFactor { get; set; } = 1.0;
    /// <summary>Записанные огибающие групп пакета (хранение «только огибающие»).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<FemCsfeaBatchGroup>? BatchGroups { get; set; }
    /// <summary>Доля длительной части, при которой в записанные огибающие вошла ширина раскрытия; null — не входила.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? BatchCrackLongShare { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsBatch => Batch != null;

    /// <summary>Длительная часть варианта <paramref name="variantIndex"/> вида <paramref name="kind"/>; null — нет.</summary>
    public FemCsfeaBatchSummary? LongOf(int variantIndex, string kind = FemCsfeaBatchLongKind.Part) =>
        BatchLong?.FirstOrDefault(b => b.PairOf == variantIndex && b.Kind == kind);

    /// <summary>
    /// Номер записи полей длительной части: своя у Derived, варианта — у совпадающей с полной; null — нет полей
    /// (нулевая, не посчитана, вариант не дошёл).
    /// </summary>
    public int? LongRecordN(FemCsfeaBatchSummary pair)
    {
        if (pair.IsDerived) return pair.Done && pair.Completed ? pair.RecordN : null;
        if (!pair.IsSameAsTotal || Batch?.FirstOrDefault(b => b.Index == pair.PairOf) is not { Completed: true } variant) return null;
        return variant.RecordN;
    }

    /// <summary>Длительная часть ждёт прогона: своя нагрузка, не посчитана, а вариант дошёл до предела λ.</summary>
    public bool LongPending(FemCsfeaBatchSummary pair) => pair.Stage != null && !pair.Done
        && Batch?.FirstOrDefault(b => b.Index == pair.PairOf) is { Completed: true };

    public string ToJson() => JsonSerializer.Serialize(this);

    public static FemCsfeaResultSummary? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<FemCsfeaResultSummary>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Строки итога для журнала.</summary>
    public IEnumerable<string> Describe()
    {
        foreach (var e in Errors) yield return "Ошибка: " + e;
        if (Batch != null)
        {
            foreach (var line in DescribeBatch()) yield return line;
            yield break;
        }
        if (Steps.Count == 0) yield break;
        bool linear = Kind == FemCsfeaRunner.LinearAnalysisKind;
        yield return linear
            ? string.Create(CultureInfo.CurrentCulture,
                $"Узлов {Nodes}, оболочек {Shells}, стержней {Beams}; загружений {Steps.Count}, {ElapsedSeconds:0.#} с.")
            : string.Create(CultureInfo.CurrentCulture,
                $"Узлов {Nodes}, оболочек {Shells}, стержней {Beams}; шагов {Steps.Count(s => s.Converged)}, итераций {Steps.Sum(s => s.Iterations)}, {ElapsedSeconds:0.#} с.");
        foreach (var (stage, i) in Stages.Select((s, i) => (s, i)))
        {
            var end = Steps.LastOrDefault(s => s.Stage == i && s.Converged);
            string control = end?.Control is { } c ? string.Create(CultureInfo.CurrentCulture, $", контрольный {FemCsfeaRunner.DescribeControlNode(ControlSchemaNodeTag, ControlNodeTag)}: {c * (ControlDof < 3 ? 1e3 : 1):0.###} {(ControlDof < 3 ? "мм" : "рад")}") : "";
            if (linear)
            {
                yield return string.Create(CultureInfo.CurrentCulture,
                    $"Загружение «{stage.Tag}»: ΣFx = {stage.Fx / 1e3:0.###}, ΣFy = {stage.Fy / 1e3:0.###}, ΣFz = {stage.Fz / 1e3:0.###} кН{control}.");
                continue;
            }
            string state = end == null ? "не начата" : Math.Abs(end.LoadFactor - 1) < 1e-12 ? "пройдена" : string.Create(CultureInfo.CurrentCulture, $"остановлена на λ = {end.LoadFactor:0.###}");
            yield return string.Create(CultureInfo.CurrentCulture,
                $"Стадия «{stage.Tag}» (ΣFz = {stage.Fz / 1e3:0.###} кН): {state}{control}.");
        }
        if (Message != null) yield return Message;
    }

    /// <summary>
    /// Пояснение статуса постановки пакета для дерева: «7 из 128 не дошли до λ = 1; не посчитаны 3»; null — не пакет или
    /// все дошли.
    /// </summary>
    /// <summary>Пояснение статуса постановки по результату CSfea (<see cref="BatchStatusNote"/>); null — нет.</summary>
    public static string? StatusNote(CalcResult result) =>
        result.TaskKind == FemCsfeaRunner.TaskKind ? Parse(result.DataJson)?.BatchStatusNote() : null;

    public string? BatchStatusNote()
    {
        if (Batch is not { Count: > 0 } batch) return null;
        int stopped = batch.Count(b => b.Done && !b.Completed), notDone = batch.Count(b => !b.Done);
        int longPending = BatchLong?.Count(LongPending) ?? 0;
        if (stopped == 0 && notDone == 0 && longPending == 0) return null;
        var parts = new List<string>();
        if (stopped > 0)
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{stopped} из {batch.Count} не дошли до λ = {BatchMaxLoadFactor:0.###}"));
        if (notDone > 0) parts.Add($"не посчитаны {notDone} из {batch.Count}");
        if (longPending > 0) parts.Add($"длительных частей не посчитано {longPending}");
        return string.Join("; ", parts);
    }

    /// <summary>
    /// Итог пакета: сколько дошли до предела λ, затем по строке на каждый недошедший и непосчитанный (спека 2026-10-17,
    /// п. 4.1 — какие варианты не вошли в огибающие).
    /// </summary>
    /// <summary>Итог длительных частей: сколько своим прогоном, совпадают с полной, нулевые; недошедшие — по строке.</summary>
    IEnumerable<string> DescribeLong(string max)
    {
        if (BatchLong?.Where(b => b.Kind == FemCsfeaBatchLongKind.Part).ToList() is not { Count: > 0 } parts) yield break;
        var variants = (Batch ?? []).ToDictionary(b => b.Index);
        bool Reached(FemCsfeaBatchSummary p) => variants.TryGetValue(p.PairOf ?? -1, out var v) && v.Completed;
        int derived = parts.Count(p => p.IsDerived && p.Done && p.Completed);
        int same = parts.Count(p => p.IsSameAsTotal && Reached(p)), zero = parts.Count(p => p.IsZero && Reached(p));
        var stopped = parts.Where(p => p.IsDerived && p.Done && !p.Completed).ToList();
        var invalid = parts.Where(p => p.PairKind == nameof(FemLongTermPairKind.Invalid)).ToList();
        int pending = parts.Count(LongPending);
        var text = new List<string> { $"своим прогоном {derived}", $"совпадают с полной {same}", $"нулевые {zero}" };
        if (stopped.Count > 0) text.Add($"не дошли {stopped.Count}");
        if (invalid.Count > 0) text.Add($"не строятся {invalid.Count}");
        if (pending > 0) text.Add($"не посчитаны {pending}");
        yield return $"Длительные части: {string.Join(", ", text)}.";
        foreach (var p in stopped)
            yield return string.Create(CultureInfo.CurrentCulture,
                $"  {(variants.TryGetValue(p.PairOf ?? -1, out var v) ? v.Label : p.Tag)} — длительная часть {p.Type} до λ = {p.LoadFactor:0.###} из {max}");
    }

    IEnumerable<string> DescribeBatch()
    {
        var batch = Batch!;
        if (batch.Count == 0) yield break;
        string max = BatchMaxLoadFactor.ToString("0.###", CultureInfo.CurrentCulture);
        int completed = batch.Count(b => b.Completed);
        yield return string.Create(CultureInfo.CurrentCulture,
            $"Узлов {Nodes}, оболочек {Shells}, стержней {Beams}; вариантов {batch.Count}, до λ = {max} — {completed}, {ElapsedSeconds:0.#} с.");
        var stopped = batch.Where(b => b.Done && !b.Completed).ToList();
        var notDone = batch.Where(b => !b.Done).ToList();
        if (stopped.Count > 0)
        {
            yield return $"Не дошли до λ = {max}: {stopped.Count}:";
            foreach (var b in stopped)
                yield return string.Create(CultureInfo.CurrentCulture,
                    $"  {b.Label} — λ = {b.LoadFactor:0.###}{(string.IsNullOrEmpty(b.Reason) ? "" : $" ({b.Reason})")}");
        }
        if (notDone.Count > 0)
        {
            yield return $"Не посчитаны: {notDone.Count}:";
            foreach (var b in notDone) yield return $"  {b.Label}";
        }
        foreach (var line in DescribeLong(max)) yield return line;
        if (Message != null) yield return Message;
    }
}

/// <summary>Окружение исполнителя: БД, Gmsh, журнал, параллельность пересчёта сечений.</summary>
public sealed record FemCsfeaRunContext(DatabaseService Db, GmshSettings Gmsh, ILogService Log, int MaxDegreeOfParallelism = -1);

/// <summary>Подготовленный вход: модель адаптера (null — подготовка прервана) и отчёт.</summary>
public sealed record FemCsfeaPrepared(FemRcModelInput? Input, FemRcModelResult? Adapted, List<FemValidationDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.IsError) || Adapted is not { HasErrors: false };

    /// <summary>Контрольный узел: тег узла схемы (пусто — задан узлом сетки) и тег узла сетки; null — не задан или не найден.</summary>
    public (string SchemaTag, string MeshTag)? ControlNode { get; init; }

    /// <summary>Сводка подготовленной модели для отчёта «Проверить вход»: размеры, стадии с ΣF, контрольный узел.</summary>
    public IEnumerable<string> Describe()
    {
        if (Adapted?.Model is not { } model) yield break;
        yield return $"Модель: узлов {model.Nodes.Count}, оболочек {model.Shells.Count}, стержней {model.Beams.Count}, "
            + $"опор {model.Supports.Count}, пружин {model.Springs.Count}, жёстких тел {model.RigidBodies.Count(b => !b.EqualDofs)}"
            + (model.RigidBodies.Any(b => b.EqualDofs) ? $", объединений перемещений {model.RigidBodies.Count(b => b.EqualDofs)}." : ".");
        foreach (var (stage, i) in model.Stages.Select((s, i) => (s, i)))
        {
            var t = i < Adapted.StageTotals.Count ? Adapted.StageTotals[i] : null;
            yield return t == null
                ? $"Стадия «{stage.Name}»: шагов {stage.Steps}."
                : string.Create(CultureInfo.CurrentCulture,
                    $"Стадия «{stage.Name}»: шагов {stage.Steps}, ΣFx = {t.Fx / 1e3:0.###}, ΣFy = {t.Fy / 1e3:0.###}, ΣFz = {t.Fz / 1e3:0.###} кН.");
        }
        if (ControlNode is { } c)
            yield return $"Контрольный {FemCsfeaRunner.DescribeControlNode(c.SchemaTag, c.MeshTag)}.";
    }

    /// <summary>Строки отчёта: ошибки, затем предупреждения и сведения.</summary>
    public IEnumerable<string> Report => Diagnostics.Concat(Adapted?.Diagnostics ?? []).OrderByDescending(d => d.IsError)
        .Select(d => (d.IsError ? "Ошибка: " : "") + d.Message);
}

/// <summary>
/// Секущий расчёт CSfea постановки схемы (Kind = <see cref="AnalysisKind"/>): сетка схемы (устаревшая — строится),
/// проверка переноса вложения SCAD, вход (<see cref="FemCsfeaInputBuilder"/>) — в вызывающем (UI) потоке; адаптер и
/// расчёт — в фоне. Отмена — <see cref="OperationCanceledException"/> (решатель откатывает незавершённый шаг).
/// </summary>
public static class FemCsfeaRunner
{
    public const string AnalysisKind = "csfea_secant";

    /// <summary>
    /// Линейный расчёт CSfea: стадии постановки — независимые загружения (без накопления и шагов), одна факторизация на
    /// все; сечения упругие, бетон брутто. Результат — того же вида <see cref="TaskKind"/> (общее окно просмотра),
    /// различается <see cref="FemCsfeaResultSummary.Kind"/>: каждое загружение — стадия из одного шага с полями.
    /// </summary>
    public const string LinearAnalysisKind = "csfea_linear";
    public const string TaskKind = "fem_csfea_secant";

    /// <summary>Постановка считается решателем CSfea (секущий или линейный расчёт).</summary>
    public static bool IsCsfeaKind(string? kind) => kind is AnalysisKind or LinearAnalysisKind;

    /// <summary>
    /// Подготовка без расчёта. <paramref name="buildMesh"/> = false — «Проверить вход»: сетка не перестраивается и не
    /// пишется (устаревшая — предупреждение, вход по сохранённой).
    /// </summary>
    public static async Task<FemCsfeaPrepared> PrepareAsync(FemCsfeaRunContext ctx, FemSchema schema, FemAnalysis analysis,
        bool buildMesh, CancellationToken ct)
    {
        var diag = new List<FemValidationDiagnostic>();
        var p = FemAnalysisParams.Parse(analysis.ParamsJson);
        var csfea = p.Csfea ?? new FemCsfeaParams();
        string? stepsError = null;
        if (csfea.ResultRecording == FemCsfeaRecording.Selected)
            FemCsfeaParams.ParseRecordSteps(csfea.RecordSteps, out stepsError);
        if (stepsError != null) diag.Add(new("record_steps", stepsError, true));

        await EnsureMeshAsync(ctx, schema, buildMesh, diag, ct);
        if (diag.Any(d => d.IsError)) return new(null, null, diag);

        var input = FemCsfeaInputBuilder.Build(ctx.Db, schema.Id, FemCsfeaSetup.FromAnalysis(analysis, ctx.Db.GetFemLoadCases(schema.Id)));
        CheckScadTransfer(ctx.Db, schema.Id, input, diag);
        CheckLiraTransfer(schema, input, diag);
        var control = string.IsNullOrWhiteSpace(csfea.ControlNodeTag) ? null : ControlNode(input, csfea.ControlNodeTag);
        if (!string.IsNullOrWhiteSpace(csfea.ControlNodeTag) && control == null)
            diag.Add(new("control_node", $"Контрольный узел «{csfea.ControlNodeTag}» не найден ни среди узлов схемы, ни в сетке.", true));
        if (csfea.ControlDof is < 0 or > 5)
            diag.Add(new("control_dof", $"DOF контрольного узла {csfea.ControlDof} — вне 0…5.", true));
        if (diag.Any(d => d.IsError)) return new(input, null, diag) { ControlNode = control };

        var adapted = await Task.Run(() => FemRcModelAdapter.Adapt(input), ct);
        return new(input, adapted, diag) { ControlNode = control };
    }

    /// <summary>Подготовка и расчёт; <paramref name="progress"/> — отчёты решателя (создавать в UI-потоке).</summary>
    public static async Task<CalcResult> RunAsync(FemCsfeaRunContext ctx, FemSchema schema, FemAnalysis analysis,
        IProgress<SecantProgress>? progress, CancellationToken ct)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        // Стадии пакета — независимые варианты; расчёт подряд накапливал бы их нагрузки.
        if (FemAnalysisParams.Parse(analysis.ParamsJson).Batch != null)
            return await RunBatchAsync(ctx, schema, analysis, null, null, null, ct);
        var prepared = await PrepareAsync(ctx, schema, analysis, buildMesh: true, ct);
        var summary = new FemCsfeaResultSummary { Report = prepared.Report.ToList() };
        if (prepared.HasErrors)
        {
            summary.Errors = prepared.Diagnostics.Concat(prepared.Adapted?.Diagnostics ?? [])
                .Where(d => d.IsError).Select(d => d.Message).ToList();
            return Result(analysis, created, "error", summary);
        }

        var csfea = FemAnalysisParams.Parse(analysis.ParamsJson).Csfea ?? new FemCsfeaParams();
        var adapted = prepared.Adapted!;
        string tag = analysis.Tag;
        if (analysis.Kind == LinearAnalysisKind)
            return await RunLinearAsync(ctx, analysis, created, prepared, summary, csfea, ct);
        // В журнал — строки шагов; строки итераций (с отступом) остаются в сводке итераций решателя.
        void Log(string line)
        {
            if (!line.StartsWith(' ')) ctx.Log.Info($"[{tag}] {line}");
        }
        var recorder = new FemCsfeaFieldRecorder(csfea, adapted.Model.Stages.Select(s => s.Steps).ToList());
        var options = FemCsfeaSetup.SecantOptions(csfea, ctx.MaxDegreeOfParallelism, Log, adapted.ShellForceAngles,
            recorder.OnStep);

        var clock = Stopwatch.StartNew();
        var run = await Task.Run(() =>
        {
            var r = RcSecantAnalysis.Run(adapted.Model, options, progress, ct);
            recorder.RecordFinal(r);
            return r;
        }, ct);
        clock.Stop();

        string? controlTag = prepared.ControlNode?.MeshTag;
        int? controlDof = ControlDof(prepared, run.Build, csfea, summary);

        var r = run.Result;
        summary.Completed = r.Completed;
        summary.LimitStage = r.LimitStage;
        summary.Message = r.Message;
        summary.ElapsedSeconds = clock.Elapsed.TotalSeconds;
        summary.Nodes = run.Build.Mesh.NNodes;
        summary.Shells = run.Build.Mesh.Shells.Count;
        summary.Beams = run.Build.Mesh.Beams.Count;
        summary.ControlNodeTag = controlDof != null ? controlTag! : "";
        summary.ControlSchemaNodeTag = controlDof != null ? prepared.ControlNode!.Value.SchemaTag : "";
        summary.ControlDof = csfea.ControlDof;
        summary.Stages = adapted.Model.Stages.Select((s, i) =>
        {
            var total = i < adapted.StageTotals.Count ? adapted.StageTotals[i] : new FemRcStageTotal(s.Name, 0, 0, 0);
            return new FemCsfeaStageSummary(s.Name, s.Steps, total.Fx, total.Fy, total.Fz);
        }).ToList();
        summary.Steps = r.Steps.Select((s, i) =>
        {
            var reactions = s.Converged ? run.StepReactions(s) : null;
            return new FemCsfeaStepSummary(i + 1, s.Stage, s.Step, s.LoadFactor, s.IsRefinement,
                s.Converged, s.Iterations, s.TrueResidual, controlDof is int d ? s.U[d] : null,
                recorder.Planned(s.Stage, s.LoadFactor, s.IsRefinement), recorder.Records.ContainsKey(i + 1),
                reactions?.Reactions.Sums, reactions?.LoadSum);
        }).ToList();
        var result = Result(analysis, created, r.Completed ? "ok" : "not_converged", summary);
        // Поля шагов — отдельными записями к результату, поэтому результат сохраняется здесь.
        ctx.Db.SaveCalcResult(result);
        ctx.Db.SaveFemResultSteps(result.Id, recorder.Records.Select(kv => (kv.Key, kv.Value)));
        return result;
    }

    /// <summary>
    /// Пакетный секущий расчёт (спека 2026-10-17): одна подготовка (адаптер получает все варианты стадиями — нагрузки и
    /// ΣF переводятся один раз), затем по каждому варианту — расчёт одной стадией с нуля (<c>model.Stages</c> = стадия
    /// варианта). Сводка пишется в БД после каждого варианта, поля конечного состояния — сразу (хранение «поля») или
    /// огибающие групп дошедших до предела λ — в конце (хранение «только огибающие»). Отмена не теряет посчитанное:
    /// результат «partial». <paramref name="resume"/> — частичный результат той же постановки с хранением «поля»:
    /// варианты с тем же выражением и записанными полями не пересчитываются, убранные из пакета — удаляются.
    /// <paramref name="attached"/> — результат впервые записан в БД (постановке — его номер).
    /// </summary>
    public static async Task<CalcResult> RunBatchAsync(FemCsfeaRunContext ctx, FemSchema schema, FemAnalysis analysis,
        CalcResult? resume, IProgress<FemCsfeaBatchProgress>? progress, Action<CalcResult>? attached, CancellationToken ct)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var pars = FemAnalysisParams.Parse(analysis.ParamsJson);
        var batch = pars.Batch ?? throw new InvalidOperationException("Постановка — не пакет.");
        var csfea = pars.Csfea ?? new FemCsfeaParams();
        bool envelopes = batch.Recording == FemCsfeaBatchRecording.Envelopes;
        var prepared = await PrepareAsync(ctx, schema, analysis, buildMesh: true, ct);
        var summary = new FemCsfeaResultSummary
        {
            Report = prepared.Report.ToList(), Batch = [], BatchRecording = batch.Recording, BatchMaxLoadFactor = batch.MaxLoadFactor,
        };
        if (prepared.HasErrors)
        {
            summary.Errors = prepared.Diagnostics.Concat(prepared.Adapted?.Diagnostics ?? [])
                .Where(d => d.IsError).Select(d => d.Message).ToList();
            return Result(analysis, created, "error", summary);
        }

        var adapted = prepared.Adapted!;
        var model = adapted.Model;
        var stages = model.Stages.ToList();
        string tag = analysis.Tag;

        // Досчёт: посчитанные варианты с записанными полями переносятся по выражению; сетка другая — всё заново.
        var previous = !envelopes && resume != null ? FemCsfeaResultSummary.Parse(resume.DataJson) : null;
        var kept = new Dictionary<string, FemCsfeaBatchSummary>(StringComparer.Ordinal);
        if (previous?.Batch is { } prevBatch)
            foreach (var b in prevBatch.Where(b => b.Done && b.RecordN != null)) kept.TryAdd(b.ExpressionJson, b);
        var result = previous != null ? resume! : Result(analysis, created, "partial", summary);
        summary.ElapsedSeconds = previous?.ElapsedSeconds ?? 0;
        summary.Nodes = previous?.Nodes ?? 0;
        summary.Shells = previous?.Shells ?? 0;
        summary.Beams = previous?.Beams ?? 0;
        summary.ControlNodeTag = previous?.ControlNodeTag ?? "";
        summary.ControlSchemaNodeTag = previous?.ControlSchemaNodeTag ?? "";
        summary.ControlDof = csfea.ControlDof;
        for (int i = 0; i < batch.Items.Count; i++)
        {
            var item = batch.Items[i];
            var total = i < adapted.StageTotals.Count ? adapted.StageTotals[i] : new FemRcStageTotal(item.Tag, 0, 0, 0);
            var entry = kept.Remove(item.LoadExpressionJson, out var old) ? old : new FemCsfeaBatchSummary
            {
                ExpressionJson = item.LoadExpressionJson,
            };
            entry.Index = i;
            entry.Tag = item.Tag;
            entry.Kind = item.Kind;
            entry.Type = item.Type;
            (entry.Fx, entry.Fy, entry.Fz) = (total.Fx, total.Fy, total.Fz);
            summary.Batch.Add(entry);
        }

        // Длительные части (спека «длительные пары», п. 2.4): по текущим загружениям; посчитанные — по выражениям
        // варианта и пары, лишние (флажок снят, вариант убран, доля изменилась) удаляются.
        var keptLong = new Dictionary<string, FemCsfeaBatchSummary>(StringComparer.Ordinal);
        if (previous?.BatchLong is { } prevLong)
            foreach (var b in prevLong.Where(b => b.Done && b.RecordN != null))
                if (previous.Batch?.FirstOrDefault(v => v.Index == b.PairOf) is { } v)
                    keptLong.TryAdd(LongKey(v.ExpressionJson, b), b);
        var plans = FemCsfeaBatchLongTerm.Plans(batch, ctx.Db.GetFemLoadCases(schema.Id));
        if (batch.LongTermPairs.Count > 0) summary.BatchLong = [];
        var notes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            var variant = summary.Batch[plan.Item];
            foreach (var note in plan.Pair.Notes)
                if (notes.Add(note)) summary.Report.Add(plan.Pair.Kind == FemLongTermPairKind.Invalid ? $"{variant.Label}: {note}" : note);
            var pair = new FemCsfeaBatchSummary
            {
                Kind = FemCsfeaBatchLongKind.Part, Tag = variant.Tag,
                Type = plan.Pair.Long?.CombinationType ?? FemLongTermPair.LongType(variant.Type) ?? "",
                ExpressionJson = plan.Pair.Long?.ToJson() ?? "{}", PairOf = variant.Index, PairKind = plan.Pair.Kind.ToString(),
                Stage = plan.Stage,
            };
            if (plan.Stage != null && keptLong.Remove(LongKey(variant.ExpressionJson, pair), out var oldPair))
                pair = oldPair;
            pair.Index = summary.BatchLong!.Count;
            pair.PairOf = variant.Index;
            pair.Stage = plan.Stage;
            pair.Tag = variant.Tag;
            var total = plan.Stage is int st && st < adapted.StageTotals.Count ? adapted.StageTotals[st]
                : plan.Pair.Kind == FemLongTermPairKind.SameAsTotal ? new FemRcStageTotal("", variant.Fx, variant.Fy, variant.Fz)
                : new FemRcStageTotal("", 0, 0, 0);
            (pair.Fx, pair.Fy, pair.Fz) = (total.Fx, total.Fy, total.Fz);
            if (plan.Stage == null)
            {
                // Без прогона: совпадающая с полной — итог варианта, нулевая — пустая, не строится — с причиной.
                pair.Done = true;
                pair.Completed = plan.Pair.Kind != FemLongTermPairKind.Invalid;
                pair.Reason = plan.Pair.Kind == FemLongTermPairKind.Invalid ? plan.Pair.Notes.FirstOrDefault() : null;
            }
            summary.BatchLong.Add(pair);
        }

        var removed = kept.Values.Concat(keptLong.Values).Select(b => b.RecordN!.Value).ToList();
        if (previous != null && removed.Count > 0)
            ctx.Db.DeleteFemResultSteps(result.Id, removed);
        int nextRecord = summary.Batch.Concat(summary.BatchLong ?? []).Where(b => b.RecordN != null)
            .Select(b => b.RecordN!.Value).DefaultIfEmpty(0).Max() + 1;

        void Save()
        {
            result.DataJson = summary.ToJson();
            bool isNew = result.Id == 0;
            ctx.Db.SaveCalcResult(result);
            if (isNew) attached?.Invoke(result);
        }
        result.Status = "partial";
        Save();

        var quantities = envelopes ? FemCsfeaEnvelopeQuantities.FromMesh(ctx.Db.GetFemMeshNodes(schema.Id), ctx.Db.GetFemMeshElements(schema.Id)) : null;
        var groups = new Dictionary<string, (FemCsfeaEnvelopeAccumulator Acc, RcSecantStepFields Reference)>(StringComparer.Ordinal);
        // Ширина раскрытия в огибающих (N, NL) — при доле длительной части по умолчанию, как в окне результата.
        var calc = pars.CalcType ?? CalcType.N;
        var crackElements = envelopes && FemCsfeaCrackWidths.Applies(calc)
            ? FemCsfeaCrackWidths.Elements(adapted, ctx.Db.Materials) : null;
        double crackShare = FemCsfeaCrackWidths.DefaultLongShare(calc);
        summary.BatchCrackLongShare = crackElements != null ? crackShare : null;
        void Log(string line)
        {
            if (!line.StartsWith(' ')) ctx.Log.Info($"[{tag}] {line}");
        }
        var options = FemCsfeaSetup.SecantOptions(csfea, ctx.MaxDegreeOfParallelism, Log, adapted.ShellForceAngles, null);
        // Работа: непосчитанные варианты и длительные части дошедших (в т. ч. посчитанных раньше — флажок включён позже).
        var longOf = (summary.BatchLong ?? []).Where(b => b.Kind == FemCsfeaBatchLongKind.Part)
            .ToDictionary(b => b.PairOf!.Value);
        bool PairPending(FemCsfeaBatchSummary v) => longOf.TryGetValue(v.Index, out var p) && p.Stage != null && !p.Done;
        var todo = summary.Batch.Where(b => !b.Done || b.Completed && PairPending(b)).ToList();
        int? controlDof = null;
        bool controlResolved = false;

        // Расчёт одной стадии с нуля: стадия адаптера, поля конечного состояния (упакованные — при хранении «поля»).
        async Task<(RcSecantRun Run, RcSecantStepFields? Fields, byte[]? Packed, double Seconds)> Solve(int stage, int number, string progressTag)
        {
            model.Stages.Clear();
            model.Stages.Add(stages[stage]);
            var inner = progress == null ? null
                : new Progress<SecantProgress>(p => progress.Report(new FemCsfeaBatchProgress(number, todo.Count, progressTag, p)));
            var clock = Stopwatch.StartNew();
            var (run, fields, packed) = await Task.Run(() =>
            {
                var solved = RcSecantAnalysis.Run(model, options, inner, ct);
                var f = solved.LastConvergedFields();
                return (solved, f, !envelopes && f != null ? FemCsfeaStepFieldsCodec.Pack(f) : null);
            }, ct);
            clock.Stop();

            if (!controlResolved)
            {
                controlDof = ControlDof(prepared, run.Build, csfea, summary);
                summary.ControlNodeTag = controlDof != null ? prepared.ControlNode!.Value.MeshTag : "";
                summary.ControlSchemaNodeTag = controlDof != null ? prepared.ControlNode!.Value.SchemaTag : "";
                controlResolved = true;
            }
            if (summary.Nodes != 0 && (summary.Nodes != run.Build.Mesh.NNodes || summary.Shells != run.Build.Mesh.Shells.Count
                || summary.Beams != run.Build.Mesh.Beams.Count))
                throw new InvalidOperationException("Сетка схемы изменилась после начала пакета — запустите «Пересчитать всё».");
            summary.Nodes = run.Build.Mesh.NNodes;
            summary.Shells = run.Build.Mesh.Shells.Count;
            summary.Beams = run.Build.Mesh.Beams.Count;
            summary.ElapsedSeconds += clock.Elapsed.TotalSeconds;
            return (run, fields, packed, clock.Elapsed.TotalSeconds);
        }

        // Итог прогона в запись сводки; поля — отдельной записью.
        void Fill(FemCsfeaBatchSummary entry, RcSecantRun run, RcSecantStepFields? fields, byte[]? packed, double seconds)
        {
            var r = run.Result;
            var last = r.Steps.LastOrDefault(s => s.Converged);
            entry.Done = true;
            entry.Completed = r.Completed;
            entry.LoadFactor = (last?.LoadFactor ?? 0) * batch.MaxLoadFactor;
            entry.Steps = r.Steps.Count(s => s.Converged);
            entry.Iterations = r.Steps.Sum(s => s.Iterations);
            entry.ElapsedSeconds = seconds;
            entry.Reason = r.Completed ? null : r.Message;
            entry.Control = last != null && controlDof is int d ? last.U[d] : null;
            entry.ReactionSums = fields?.ReactionSums;
            entry.LoadSum = fields?.LoadSum;
            if (packed != null)
            {
                entry.RecordN = nextRecord++;
                ctx.Db.SaveFemResultStep(result.Id, entry.RecordN.Value, packed);
            }
        }

        // Огибающая группы (хранение «только огибающие») — по полям дошедшего прогона.
        async Task AddToEnvelope(FemCsfeaBatchSummary entry, RcSecantStepFields f)
        {
            var vector = await Task.Run(() => quantities!.Vector(f, FemCsfeaEnvelopeQuantities.NodeIndex(f), single: true,
                crackElements != null ? FemCsfeaCrackWidths.Compute(crackElements, f, crackShare, ct) : null), ct);
            if (!groups.TryGetValue(entry.GroupKey, out var g)) groups[entry.GroupKey] = g = (new FemCsfeaEnvelopeAccumulator(), f);
            g.Acc.Add(vector, entry.PairOf ?? entry.Index);
        }

        try
        {
            for (int k = 0; k < todo.Count; k++)
            {
                ct.ThrowIfCancellationRequested();
                var entry = todo[k];
                int number = k + 1;
                RcSecantStepFields? variantFields = null;
                if (!entry.Done)
                {
                    var (run, fields, packed, seconds) = await Solve(entry.Index, number, entry.Tag);
                    Fill(entry, run, fields, packed, seconds);
                    variantFields = fields;
                    ctx.Log.Info(string.Create(CultureInfo.CurrentCulture,
                        $"[{tag}] Вариант {number} из {todo.Count} «{entry.Tag}»: λ = {entry.LoadFactor:0.###}{(entry.Completed ? "" : " — не дошёл до предела")}, шагов {entry.Steps}, {entry.ElapsedSeconds:0.#} с."));
                }

                // Длительная часть — сразу после дошедшего варианта, на тех же диаграммах (п. 6.1.26).
                if (entry.Completed && longOf.TryGetValue(entry.Index, out var pair))
                {
                    if (pair.Stage is int pairStage && !pair.Done)
                    {
                        ct.ThrowIfCancellationRequested();
                        var (run, fields, packed, seconds) = await Solve(pairStage, number,
                            FemCsfeaBatchLongTerm.PairTag(entry.Tag, pair.Type));
                        Fill(pair, run, fields, packed, seconds);
                        ctx.Log.Info(pair.Completed
                            ? string.Create(CultureInfo.CurrentCulture,
                                $"[{tag}] {entry.Label}: длительная часть {pair.Type} — шагов {pair.Steps}, {pair.ElapsedSeconds:0.#} с.")
                            : string.Create(CultureInfo.CurrentCulture,
                                $"[{tag}] {entry.Label}: длительная часть не дошла до λ = {batch.MaxLoadFactor:0.###} (λ = {pair.LoadFactor:0.###}) — {(pair.Type == "CL" ? "ψ" : "φ1")} по умолчанию."));
                        if (quantities != null && fields != null && pair.Completed) await AddToEnvelope(pair, fields);
                    }
                    else if (quantities != null && variantFields != null && pair.IsSameAsTotal)
                        await AddToEnvelope(pair, variantFields);
                }
                if (quantities != null && variantFields != null && entry.Completed) await AddToEnvelope(entry, variantFields);
                Save();
                if (!entry.Completed && batch.StopAtLimit)
                {
                    summary.Message = $"Пакет остановлен: вариант «{entry.Tag}» не дошёл до предела λ.";
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            summary.Message = "Расчёт пакета прерван — посчитанные варианты сохранены.";
        }
        finally
        {
            model.Stages.Clear();
            model.Stages.AddRange(stages);
        }

        if (envelopes)
        {
            summary.BatchGroups = [];
            int g = 0;
            foreach (var key in FemCombinationTypesOrder(groups.Keys))
            {
                var (acc, reference) = groups[key];
                int n = FemCsfeaBatchGroup.RecordBase + ++g;
                ctx.Db.SaveFemResultStep(result.Id, n, FemCsfeaEnvelopeCodec.Pack(acc.Result!, reference));
                summary.BatchGroups.Add(new FemCsfeaBatchGroup(key, n, acc.Count));
            }
        }
        result.Status = summary.Batch.Any(b => !b.Done) || summary.Batch.Any(b => b.Completed && PairPending(b)) ? "partial"
            : summary.Batch.All(b => b.Completed) ? "ok" : "not_converged";
        summary.Completed = result.Status == "ok";
        Save();
        return result;
    }

    /// <summary>Ключ посчитанной длительной части для досчёта: выражения варианта и пары, вид.</summary>
    public static string LongKey(string variantExpressionJson, FemCsfeaBatchSummary pair) =>
        LongKey(variantExpressionJson, pair.Kind, pair.ExpressionJson);

    public static string LongKey(string variantExpressionJson, string kind, string pairExpressionJson) =>
        variantExpressionJson + "\n" + kind + "\n" + pairExpressionJson;

    /// <summary>Ключи групп огибающих пакета по порядку: C, CL, N, NL, A, затем загружения.</summary>
    static IEnumerable<string> FemCombinationTypesOrder(IEnumerable<string> keys)
    {
        var order = CScore.Fem.Combinations.FemCombinationTypes.All.Append(FemCsfeaBatchGroup.LoadCases).ToList();
        return keys.OrderBy(k => order.IndexOf(k) is int i && i >= 0 ? i : order.Count).ThenBy(k => k, StringComparer.Ordinal);
    }

    /// <summary>
    /// Линейный расчёт подготовленной модели: решения всех загружений и упаковка их полей — в фоне; в сводке каждое
    /// загружение — стадия из одного сошедшегося шага, поля записываются у всех.
    /// </summary>
    static async Task<CalcResult> RunLinearAsync(FemCsfeaRunContext ctx, FemAnalysis analysis, string created,
        FemCsfeaPrepared prepared, FemCsfeaResultSummary summary, FemCsfeaParams csfea, CancellationToken ct)
    {
        var adapted = prepared.Adapted!;
        string tag = analysis.Tag;
        var options = new RcLinearOptions
        {
            MaxDegreeOfParallelism = ctx.MaxDegreeOfParallelism, ShellForceAngles = adapted.ShellForceAngles,
            Log = line => ctx.Log.Info($"[{tag}] {line}"),
        };
        var clock = Stopwatch.StartNew();
        var (run, records) = await Task.Run(() =>
        {
            var r = RcLinearAnalysis.Run(adapted.Model, options, ct);
            var packed = new byte[r.Cases.Count][];
            Parallel.For(0, packed.Length, new ParallelOptions { MaxDegreeOfParallelism = ctx.MaxDegreeOfParallelism },
                i => packed[i] = FemCsfeaStepFieldsCodec.Pack(r.Cases[i].Fields));
            return (r, packed);
        }, ct);
        clock.Stop();

        int? controlDof = ControlDof(prepared, run.Build, csfea, summary);
        summary.Kind = LinearAnalysisKind;
        summary.Completed = true;
        summary.ElapsedSeconds = clock.Elapsed.TotalSeconds;
        summary.Nodes = run.Build.Mesh.NNodes;
        summary.Shells = run.Build.Mesh.Shells.Count;
        summary.Beams = run.Build.Mesh.Beams.Count;
        summary.ControlNodeTag = controlDof != null ? prepared.ControlNode!.Value.MeshTag : "";
        summary.ControlSchemaNodeTag = controlDof != null ? prepared.ControlNode!.Value.SchemaTag : "";
        summary.ControlDof = csfea.ControlDof;
        summary.Report.AddRange(run.Build.Report);
        summary.Stages = adapted.Model.Stages.Select((s, i) =>
        {
            var total = i < adapted.StageTotals.Count ? adapted.StageTotals[i] : new FemRcStageTotal(s.Name, 0, 0, 0);
            return new FemCsfeaStageSummary(s.Name, 1, total.Fx, total.Fy, total.Fz);
        }).ToList();
        summary.Steps = run.Cases.Select((c, i) => new FemCsfeaStepSummary(i + 1, i, 1, 1.0, false, true, 1, 0.0,
            controlDof is int d ? c.U[d] : null, i + 1, true, c.Fields.ReactionSums, c.Fields.LoadSum)).ToList();
        var result = Result(analysis, created, "ok", summary);
        ctx.Db.SaveCalcResult(result);
        ctx.Db.SaveFemResultSteps(result.Id, records.Select((data, i) => (i + 1, data)));
        return result;
    }

    /// <summary>
    /// Глобальный DOF контрольного узла в сетке расчёта; null — узел не задан или не входит в модель (запись в отчёт).
    /// </summary>
    static int? ControlDof(FemCsfeaPrepared prepared, RcStructuralMeshBuild build, FemCsfeaParams csfea,
        FemCsfeaResultSummary summary)
    {
        if (prepared.ControlNode?.MeshTag is not { } controlTag) return null;
        if (FemMeshTopology.CanonicalNodeTag(controlTag) is { } canon
            && int.TryParse(canon, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nodeId)
            && build.NodeIndex.ContainsKey(nodeId))
            return build.Dof(nodeId, csfea.ControlDof);
        summary.Report.Add($"Контрольный узел «{csfea.ControlNodeTag}» не входит в расчётную модель — история не записана.");
        return null;
    }

    static CalcResult Result(FemAnalysis analysis, string created, string status, FemCsfeaResultSummary summary) => new()
    {
        TaskId = 0, TaskKind = TaskKind, TaskTag = analysis.Tag, Created = created, Status = status, DataJson = summary.ToJson(),
    };

    /// <summary>
    /// Сетка схемы актуальна — ничего; устарела — строится и записывается (<paramref name="build"/>), иначе предупреждение.
    /// Изменившаяся сетка сбрасывает результаты постановок схемы, как команда «Построить сетку схемы».
    /// </summary>
    static async Task EnsureMeshAsync(FemCsfeaRunContext ctx, FemSchema schema, bool build, List<FemValidationDiagnostic> diag,
        CancellationToken ct)
    {
        var db = ctx.Db;
        var nodes = db.GetFemNodes(schema.Id);
        var members = db.GetFemMembers(schema.Id);
        double? barStep = db.GetFemSchemaMeshSteps(schema.Id).Bar;
        var service = new FemSchemaMeshService(db, ctx.Gmsh);
        var check = await service.CheckAsync(schema.Id, nodes, members, barStep, ct);
        if (check.HasErrors)
        {
            diag.AddRange(check.Diagnostics);
            return;
        }
        if (check.IsCurrent == true) return;
        if (!build)
        {
            diag.Add(new("mesh_stale", "Сетка схемы устарела — перед расчётом будет перестроена; вход проверен по сохранённой.", false));
            return;
        }

        ctx.Log.Info($"Сетка схемы «{schema.Tag}» устарела — строится заново.");
        var result = await service.BuildAsync(schema.Id, nodes, members, barStep, ctx.Log.Info, ct);
        if (result.Mesh is not { } mesh || result.HasErrors)
        {
            diag.AddRange(result.Diagnostics);
            if (!diag.Any(d => d.IsError)) diag.Add(new("mesh_build", "Сетка схемы не построена.", true));
            return;
        }
        diag.AddRange(result.Diagnostics);
        bool changed = !service.IsSameAsStored(schema.Id, mesh);
        service.Save(schema.Id, mesh);
        if (changed) service.ReportMeshChanged(schema, ctx.Log);
        ctx.Log.Info(string.Format(Loc.S("FemSchemaMeshDone"), mesh.Nodes.Count,
            mesh.Elements.Count(e => e.ElemType == "beam"), mesh.Elements.Count(e => e.ElemType == "shell"),
            result.RegionCount, result.RebuiltRegionCount, mesh.SharedNodeCount, mesh.SplitBeamCount));
    }

    /// <summary>
    /// Схема с вложением SCAD, из которого не перенесены нагрузки или ГУ (нет объектов с происхождением
    /// <see cref="ScadLoadTransfer.Origin"/>), — ошибка с подсказкой команды; расчёт данные схемы не меняет.
    /// </summary>
    static void CheckScadTransfer(DatabaseService db, int schemaId, FemRcModelInput input, List<FemValidationDiagnostic> diag)
    {
        if (!db.HasFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAnalysisModel)) return;
        string origin = ScadLoadTransfer.Origin;
        bool loads = input.LoadCases.Any(c => c.Origin == origin) || input.ElementLoads.Any(l => l.Origin == origin)
            || input.MeshNodeLoads.Any(l => l.Origin == origin);
        bool boundary = input.Supports.Any(s => s.Origin == origin) || input.Springs.Any(s => s.Origin == origin)
            || input.RigidBodies.Any(b => b.Origin == origin);
        if (!loads)
            diag.Add(new("scad_loads_not_transferred",
                $"Нагрузки из вложения SCAD не перенесены в схему — команда «{Loc.S("ScadLoadsTransfer")}».", true));
        if (!boundary)
            diag.Add(new("scad_boundary_not_transferred",
                $"Граничные условия SCAD не перенесены в схему — команда «{Loc.S("ScadBoundaryRefresh")}».", true));
    }

    /// <summary>
    /// Схема ЛИРЫ без нагрузок или без граничных условий вовсе (текстовый файл схемы при импорте не найден или перенос
    /// не выполнялся) — ошибка с подсказкой команды переноса из текстового файла ЛИРЫ. Ручные нагрузки и ГУ считаются:
    /// схемам, где пользователь задал их сам, перенос не навязывается.
    /// </summary>
    internal static void CheckLiraTransfer(FemSchema schema, FemRcModelInput input, List<FemValidationDiagnostic> diag)
    {
        if (schema.SourceType != "lira") return;
        bool loads = input.ElementLoads.Count > 0 || input.MeshNodeLoads.Count > 0 || input.NodeLoads.Count > 0
            || input.MemberLoads.Count > 0 || input.KinematicLoads.Count > 0
            || input.LoadCases.Any(c => c.SelfWeightFactor is { } f && f != 0);
        bool boundary = input.Supports.Count > 0 || input.Springs.Count > 0 || input.RigidBodies.Count > 0
            || input.Nodes.Any(n => n.DofMask != 0) || input.MeshElements.Any(e => e.FoundationC1 is > 0);
        if (!loads)
            diag.Add(new("lira_loads_not_transferred",
                $"У схемы ЛИРЫ нет нагрузок — перенесите их из текстового файла схемы: команда «{Loc.S("LiraLoadsTransfer")}».", true));
        if (!boundary)
            diag.Add(new("lira_boundary_not_transferred",
                $"У схемы ЛИРЫ нет граничных условий — перенесите их из текстового файла схемы: команда «{Loc.S("LiraBoundaryRefresh")}».", true));
    }

    /// <summary>
    /// Контрольный узел: узел схемы (узел сетки — по <see cref="FemMeshNode.SourceNodeTag"/>), иначе узел сетки (тег узла
    /// схемы — его <see cref="FemMeshNode.SourceNodeTag"/>, если есть); null — не найден.
    /// </summary>
    static (string SchemaTag, string MeshTag)? ControlNode(FemRcModelInput input, string tag)
    {
        tag = tag.Trim();
        if (input.Nodes.Any(n => n.NodeTag == tag) && input.MeshNodes.FirstOrDefault(n => n.SourceNodeTag == tag) is { } bySource)
            return (tag, bySource.NodeTag);
        return input.MeshNodes.FirstOrDefault(n => n.NodeTag == tag) is { } mesh ? (mesh.SourceNodeTag ?? "", mesh.NodeTag) : null;
    }

    /// <summary>Подпись контрольного узла в журнале (после слова «контрольный»): «узел схемы 5 (сетка 123)» или «узел сетки 123».</summary>
    public static string DescribeControlNode(string? schemaTag, string meshTag) =>
        string.IsNullOrEmpty(schemaTag) ? $"узел сетки {meshTag}" : $"узел схемы {schemaTag} (сетка {meshTag})";
}
