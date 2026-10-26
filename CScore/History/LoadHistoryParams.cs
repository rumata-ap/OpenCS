using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore.History;

/// <summary>Этап истории в параметрах задачи: усилие задаётся вручную или строкой набора.</summary>
public sealed class LoadHistoryStageParams
{
    /// <summary>Наименование этапа.</summary>
    public string Tag { get; set; } = "";
    /// <summary>Вид этапа.</summary>
    public StageKind Kind { get; set; } = StageKind.Load;
    /// <summary>Часть, включаемая в начале этапа; null — без включения.</summary>
    public int? ActivatePart { get; set; }
    /// <summary>Усилие конца этапа (у этапа разрушения — только направление момента).</summary>
    public StageForce Force { get; set; } = new() { Mode = "manual" };
    /// <summary>Число шагов этапа по нагрузке.</summary>
    public int Steps { get; set; } = 5;
}

/// <summary>Роли бетона и арматуры части в параметрах задачи.</summary>
public sealed class PartRolesParams
{
    /// <summary>Индекс части.</summary>
    public int PartIndex { get; set; }
    /// <summary>Роль бетона части.</summary>
    public PartLimitRole Concrete { get; set; }
    /// <summary>Роль арматуры части.</summary>
    public PartLimitRole Rebar { get; set; }
}

/// <summary>Коды ошибок параметров истории (ключи строк LoadHistoryErr_&lt;код&gt;).</summary>
public static class LoadHistoryParamErrors
{
    public const string NoStages = "no_stages", FailureNotLast = "failure_not_last", BadActivation = "bad_activation",
        Steps = "steps", ForceMode = "force_mode", DropRatio = "drop_ratio", Softening = "softening",
        FailureSteps = "failure_steps", CMax = "c_max";
}

/// <summary>Параметры задачи load_history (CalcTask.ParamsJson).</summary>
public sealed class LoadHistoryParams
{
    /// <summary>Этапы в порядке выполнения; этап разрушения — только последним.</summary>
    public List<LoadHistoryStageParams> Stages { get; set; } = [];
    /// <summary>Настройки этапа разрушения (управление по кривизне).</summary>
    public FailureOptions Failure { get; set; } = new();
    /// <summary>Роли частей в критерии предела; отсутствующая роль — «Ограничивает».</summary>
    public List<PartRolesParams> PartRoles { get; set; } = [];
    /// <summary>Считать второй путь «без истории» (все части с начала).</summary>
    public bool CompareWithoutHistory { get; set; } = true;
    /// <summary>Продолжение несошедшегося шага по нагрузке.</summary>
    public LoadStepFallback Fallback { get; set; } = LoadStepFallback.CurvatureControl;

    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Разобрать JSON-параметры; пусто или ошибка — значения по умолчанию.</summary>
    public static LoadHistoryParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new();
        try { return JsonSerializer.Deserialize<LoadHistoryParams>(json, Opts) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Сериализовать в JSON (перечисления — строками).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    /// <summary>Коды ошибок; пусто — параметры верны.</summary>
    /// <param name="partCount">Число частей стадийного сечения.</param>
    public IReadOnlyList<string> Validate(int partCount)
    {
        var errors = new List<string>();
        void Add(string code) { if (!errors.Contains(code)) errors.Add(code); }
        if (Stages.Count == 0) Add(LoadHistoryParamErrors.NoStages);
        int next = 1;
        for (int i = 0; i < Stages.Count; i++)
        {
            var st = Stages[i];
            if (st.Kind == StageKind.Failure && i != Stages.Count - 1) Add(LoadHistoryParamErrors.FailureNotLast);
            if (st.Kind == StageKind.Load && st.Steps < 1) Add(LoadHistoryParamErrors.Steps);
            if (st.Force.Mode is not ("manual" or "item")) Add(LoadHistoryParamErrors.ForceMode);
            if (st.ActivatePart is int p)
            {
                if (p != next || p >= partCount) Add(LoadHistoryParamErrors.BadActivation);
                next++;
            }
        }
        if (!(Failure.DropRatio > 0 && Failure.DropRatio < 1)) Add(LoadHistoryParamErrors.DropRatio);
        if (!(Failure.SofteningStrain > 0)) Add(LoadHistoryParamErrors.Softening);
        if (Failure.Steps < 1) Add(LoadHistoryParamErrors.FailureSteps);
        if (Failure.CMax is double c && !(c > 0)) Add(LoadHistoryParamErrors.CMax);
        return errors;
    }

    /// <summary>Роли частей для решателя истории.</summary>
    public IReadOnlyList<PartRoles> ToRoles() =>
        PartRoles.Select(r => new PartRoles(r.PartIndex, r.Concrete, r.Rebar)).ToList();
}
