using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore.History;

/// <summary>Состояние части в шаге: деформации относительно включения и использование.</summary>
public sealed class PartStateDto
{
    public int Part { get; set; }
    public double EbMin { get; set; }
    public double EbMax { get; set; }
    public double EsMin { get; set; }
    public double EsMax { get; set; }
    public double EtaB { get; set; }
    public double EtaS { get; set; }
    /// <summary>Наибольшая остаточная деформация бетона |εp| (0 без гистерезиса).</summary>
    public double EbRes { get; set; }
    /// <summary>Наибольшая остаточная деформация арматуры (0 без гистерезиса).</summary>
    public double EsRes { get; set; }
}

/// <summary>Шаг истории в результате задачи.</summary>
public sealed class HistoryStepDto
{
    public int Stage { get; set; }
    /// <summary>"load" | "failure".</summary>
    public string Kind { get; set; } = "load";
    public int Step { get; set; }
    public int Parts { get; set; }
    public double NTarget { get; set; }
    public double MxTarget { get; set; }
    public double MyTarget { get; set; }
    public double N { get; set; }
    public double Mx { get; set; }
    public double My { get; set; }
    public double E0 { get; set; }
    public double Ky { get; set; }
    public double Kz { get; set; }
    public double C { get; set; }
    public double Mu { get; set; }
    public bool Converged { get; set; }
    public int Iterations { get; set; }
    public double Residual { get; set; }
    public bool LimitPoint { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<PartStateDto> PartStates { get; set; } = [];

    /// <summary>Плоскость деформаций шага.</summary>
    public Kurvature K => new() { e0 = E0, ky = Ky, kz = Kz };
}

/// <summary>Сводка одного пути.</summary>
public sealed class RunSummaryDto
{
    public string Status { get; set; } = "ok";
    public string StopReason { get; set; } = "completed";
    public double? MLim { get; set; }
    public double? MPeak { get; set; }
    public int? GoverningPart { get; set; }
    public string? GoverningMaterial { get; set; }
    public double? GoverningX { get; set; }
    public double? GoverningY { get; set; }
}

/// <summary>Результат задачи load_history (CalcResult.DataJson). Поля волокон не пишутся —
/// восстанавливаются из K шага и плоскостей включения.</summary>
public sealed class LoadHistoryResultData
{
    public double Phi { get; set; }
    public string StrainMode { get; set; } = "plane";
    public string Fallback { get; set; } = "";
    public List<string> PartTags { get; set; } = [];
    public List<string> StageTags { get; set; } = [];
    public List<double[]> ActivationPlanes { get; set; } = [];
    public List<HistoryStepDto> Steps { get; set; } = [];
    public List<HistoryStepDto>? StepsNoHistory { get; set; }
    public RunSummaryDto With { get; set; } = new();
    public RunSummaryDto? Without { get; set; }
    public List<PartRolesParams> NonStandardRoles { get; set; } = [];
    /// <summary>Параметры гистерезиса задачи; null — нелинейно-упругий материал.</summary>
    public Hysteresis.HysteresisOptions? Hysteresis { get; set; }
    /// <summary>Учёт растяжения бетона задачи (для воспроизведения пути).</summary>
    public bool ConcreteTension { get; set; }
    /// <summary>Длина спада ролей «Разрушается» (для воспроизведения пути).</summary>
    public double SofteningStrain { get; set; }
    public string? Error { get; set; }

    /// <summary>"ok", если оба пути завершены; иначе "partial".</summary>
    [JsonIgnore]
    public string Status => With.Status == "ok" && (Without is null || Without.Status == "ok") ? "ok" : "partial";

    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static LoadHistoryResultData Build(LoadHistoryResult r, StagedSection s,
        IReadOnlyList<HistoryStage> stages, LoadHistoryOptions o)
    {
        var roles = s.Parts.Select(p => o.RoleOf(p.Index))
            .Where(x => x.Concrete != PartLimitRole.Limits || x.Rebar != PartLimitRole.Limits)
            .Select(x => new PartRolesParams { PartIndex = x.PartIndex, Concrete = x.Concrete, Rebar = x.Rebar })
            .ToList();
        return new LoadHistoryResultData
        {
            Phi = r.Phi,
            StrainMode = s.StrainMode == StageStrainMode.Fibers ? "fibers" : "plane",
            Fallback = o.Fallback.ToString(),
            PartTags = s.Parts.Select(p => p.Tag).ToList(),
            StageTags = stages.Select(st => st.Tag).ToList(),
            ActivationPlanes = r.WithHistory.ActivationPlanes.Select(k => new[] { k.e0, k.ky, k.kz }).ToList(),
            Steps = r.WithHistory.Steps.Select(Step).ToList(),
            StepsNoHistory = r.WithoutHistory?.Steps.Select(Step).ToList(),
            With = Summary(r.WithHistory),
            Without = r.WithoutHistory is null ? null : Summary(r.WithoutHistory),
            NonStandardRoles = roles,
            Hysteresis = o.Hysteresis,
            ConcreteTension = o.Ten,
            SofteningStrain = o.Failure.SofteningStrain,
        };
    }

    static HistoryStepDto Step(HistoryStep st) => new()
    {
        Stage = st.StageIndex, Kind = st.Kind == StageKind.Failure ? "failure" : "load", Step = st.Step,
        Parts = st.ActivePartCount,
        NTarget = st.Target.N, MxTarget = st.Target.Mx, MyTarget = st.Target.My,
        N = st.Result.N, Mx = st.Result.Mx, My = st.Result.My,
        E0 = st.K.e0, Ky = st.K.ky, Kz = st.K.kz, C = st.C, Mu = st.Mu,
        Converged = st.Converged, Iterations = st.Iterations,
        Residual = double.IsFinite(st.Residual) ? st.Residual : 0,
        LimitPoint = st.IsLimitPoint, Warnings = [.. st.Warnings],
        PartStates = st.Parts.Select(p => new PartStateDto
        {
            Part = p.PartIndex, EbMin = p.ConcreteEpsMin, EbMax = p.ConcreteEpsMax,
            EsMin = p.RebarEpsMin, EsMax = p.RebarEpsMax, EtaB = p.EtaConcrete, EtaS = p.EtaRebar,
            EbRes = p.ConcreteResidual, EsRes = p.RebarResidual,
        }).ToList(),
    };

    static RunSummaryDto Summary(LoadHistoryRun run) => new()
    {
        Status = run.Status, StopReason = run.StopReason, MLim = run.MLim, MPeak = run.MPeak,
        GoverningPart = run.GoverningPart, GoverningMaterial = run.GoverningMaterial,
        GoverningX = run.GoverningPoint?.X, GoverningY = run.GoverningPoint?.Y,
    };

    public static LoadHistoryResultData Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new() { Error = "empty" };
        try { return JsonSerializer.Deserialize<LoadHistoryResultData>(json, Opts) ?? new() { Error = "empty" }; }
        catch (JsonException ex) { return new() { Error = ex.Message }; }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Opts);
}
