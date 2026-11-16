using System.Text.Json;
using System.Text.Json.Serialization;
using CScore.Fem.Fragment.Masks;
using CScore.Sp63Punching.Fem;

namespace OpenCS.Tasks;

/// <summary>Переопределения узла проверки продавливания по МКЭ.</summary>
public sealed record PunchingFemJointOverride
{
    /// <summary>Тег узла сетки.</summary>
    public string Node { get; init; } = "";
    /// <summary>Узел исключён из проверки.</summary>
    public bool Excluded { get; init; }
    /// <summary>Привязки, м; null — из сечения плиты.</summary>
    public double? Ax { get; init; }
    public double? Ay { get; init; }
    /// <summary>Поперечная арматура узла; null — общая арматура проверки.</summary>
    public PunchingReinforcementParams? Reinforcement { get; init; }
}

/// <summary>
/// Параметры проверки продавливания по МКЭ-схеме (<see cref="FemCheck.NormCode"/> = <c>punching_check</c>, спека
/// 2026-11-15, срез 3). Единицы — м, кН, кПа.
/// </summary>
public sealed record PunchingFemCheckParams
{
    /// <summary>Вид проверки по МКЭ.</summary>
    public const string NormCode = "punching_check";

    /// <summary>Источник усилий: <see cref="FragmentMaskSet.SourceCsfeaLinear"/>, <see cref="FragmentMaskSet.SourceCsfeaSecant"/>,
    /// <see cref="FragmentMaskSet.SourceLira"/>, <see cref="FragmentMaskSet.SourceScad"/>.</summary>
    public string SourceKind { get; init; } = FragmentMaskSet.SourceCsfeaLinear;
    /// <summary>Ссылка на источник: Id постановки CSfea.</summary>
    public string SourceRef { get; init; } = "";
    /// <summary>Главный способ: "auto" (C у CSfea, S у импорта) | "column" | "contour".</summary>
    public string Method { get; init; } = "auto";
    /// <summary>Моменты колонн — полные Mloc: в продавливание идёт половина (п. 8.1.46).</summary>
    public bool MomentsAreMloc { get; init; } = true;
    /// <summary>Бетон по сечению плиты схемы, если он там задан.</summary>
    public bool ConcreteFromSchema { get; init; } = true;
    /// <summary>Бетон проверки (у пластин без бетона и при <see cref="ConcreteFromSchema"/> = false).</summary>
    public int ConcreteMaterialId { get; init; }
    /// <summary>Ручное Rbt, кПа; null — из материала.</summary>
    public double? RbtOverride { get; init; }
    /// <summary>Привязки по умолчанию, м (у сечений без арматуры у растянутой грани).</summary>
    public double DefaultAx { get; init; } = 0.035;
    public double DefaultAy { get; init; } = 0.045;
    /// <summary>Поперечная арматура (общая для узлов).</summary>
    public PunchingReinforcementParams Reinforcement { get; init; } = new();
    /// <summary>Режимы ядра (эксцентриситет, W, пилон, отверстия).</summary>
    public PunchingOptionParams Options { get; init; } = new();
    /// <summary>Наименьший угол стержня к плите, град.</summary>
    public double AngleDeg { get; init; } = 10;
    /// <summary>Края ближе этого числа h0 к площади учитываются.</summary>
    public double EdgeInfluence { get; init; } = 6;
    /// <summary>Вытянутость сечения, начиная с которой — стена, а не пилон.</summary>
    public double WallRatio { get; init; } = 4;
    /// <summary>Переопределения узлов (исключение, привязки, арматура).</summary>
    public List<PunchingFemJointOverride> Overrides { get; init; } = [];

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static PunchingFemCheckParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}") return new PunchingFemCheckParams();
        PunchingFemCheckParams p;
        try { p = JsonSerializer.Deserialize<PunchingFemCheckParams>(json, JsonOpts) ?? new(); }
        catch (JsonException) { p = new(); }
        return p with
        {
            Reinforcement = p.Reinforcement ?? new(), Options = p.Options ?? new(), Overrides = p.Overrides ?? [],
            SourceKind = p.SourceKind ?? FragmentMaskSet.SourceCsfeaLinear, SourceRef = p.SourceRef ?? "", Method = p.Method ?? "auto",
        };
    }

    /// <summary>Настройки поиска узлов.</summary>
    [JsonIgnore]
    public PunchingFemOptions FinderOptions => new(AngleDeg: AngleDeg, EdgeInfluence: EdgeInfluence,
        HoleInfluence: Options.HoleInfluence, WallRatio: WallRatio, DefaultCover: new PunchingFemCover(DefaultAx, DefaultAy),
        WallFigure: PunchingOptionParams.ParseWallFigure(Options.WallFigure));

    /// <summary>Главный способ ядра; null — по источнику.</summary>
    [JsonIgnore]
    public PunchingFemForceMethod? MainMethod => Method switch
    {
        "column" => PunchingFemForceMethod.Column,
        "contour" => PunchingFemForceMethod.Contour,
        _ => null,
    };

    /// <summary>Источник — расчёт CSfea схемы.</summary>
    [JsonIgnore]
    public bool IsCsfea => SourceKind is FragmentMaskSet.SourceCsfeaLinear or FragmentMaskSet.SourceCsfeaSecant;
}
