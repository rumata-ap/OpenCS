using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore.Fem.Imperfections;

/// <summary>Виды элементов несовершенства (<see cref="FemImperfection.Kind"/>).</summary>
public static class FemImperfectionKinds
{
    /// <summary>Крен (EN 1993-1-1 п. 5.3.2(3)a, рис. 5.2) — поле по охвату, параметры <see cref="FemSwayParams"/>.</summary>
    public const string Sway = "sway";
    /// <summary>Местная погибь стержня (п. 5.3.2(3)b, табл. 5.1) — парабола, параметры <see cref="FemBowParams"/>.</summary>
    public const string Bow = "bow";
    /// <summary>Формы потери устойчивости (п. 5.3.2(11)) — далее, не поддержано.</summary>
    public const string Modes = "modes";
}

/// <summary>
/// Элемент несовершенства загружения «Несовершенство» (<see cref="FemSp20Types.Imperfection"/>, таблица
/// <c>fem_imperfections</c>): вид, параметры (JSON по виду) и комментарий. Несколько элементов одного загружения
/// складываются (крен + погиби — штатно).
/// </summary>
public sealed class FemImperfection
{
    public int Id { get; set; }
    public int SchemaId { get; set; }
    /// <summary>Загружение-несовершенство, которому принадлежит элемент.</summary>
    public int LoadCaseId { get; set; }
    /// <summary><see cref="FemImperfectionKinds"/>.</summary>
    public string Kind { get; set; } = FemImperfectionKinds.Sway;
    /// <summary>Имя элемента (для отчёта); пусто — «вид #Id».</summary>
    public string Tag { get; set; } = "";
    /// <summary>Параметры вида: <see cref="FemSwayParams"/> или <see cref="FemBowParams"/>.</summary>
    public string ParamsJson { get; set; } = "{}";
    public string? Comment { get; set; }

    /// <summary>Подпись для отчёта.</summary>
    [JsonIgnore]
    public string Label => !string.IsNullOrWhiteSpace(Tag) ? Tag : Kind switch
    {
        FemImperfectionKinds.Sway => $"Крен #{Id}",
        FemImperfectionKinds.Bow => $"Погибь #{Id}",
        _ => $"Несовершенство #{Id}",
    };

    public FemImperfection Clone() => (FemImperfection)MemberwiseClone();
}

/// <summary>Сериализация параметров несовершенств (System.Text.Json, null не пишутся).</summary>
public static class FemImperfectionJson
{
    internal static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    internal static T Parse<T>(string? json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
    }
}

/// <summary>
/// Параметры крена: охват, направление в плане, наклон φ. u₀ = φ·(z − z₀)·d, d = (cos α, sin α, 0).
/// </summary>
public sealed record FemSwayParams
{
    public const string ScopeAll = "all";
    public const string ScopeList = "list";
    /// <summary>φ = φ₀·α_h·α_m по EN 1993-1-1 (5.5).</summary>
    public const string PhiEn1993 = "en1993";
    /// <summary>φ = 1/<see cref="PhiInv"/>.</summary>
    public const string PhiManual = "manual";

    /// <summary><see cref="ScopeAll"/> — вся схема; <see cref="ScopeList"/> — <see cref="MemberTags"/> и <see cref="ElementTags"/>.</summary>
    public string Scope { get; init; } = ScopeAll;
    /// <summary>Теги конструктивных элементов охвата (все их КЭ сетки — стержни и пластины).</summary>
    public List<string> MemberTags { get; init; } = [];
    /// <summary>Теги КЭ сетки охвата.</summary>
    public List<string> ElementTags { get; init; } = [];
    /// <summary>Направление крена в плане — угол от +X против часовой стрелки, град.</summary>
    public double AngleDeg { get; init; }
    /// <summary><see cref="PhiEn1993"/> | <see cref="PhiManual"/>.</summary>
    public string PhiMode { get; init; } = PhiManual;
    /// <summary>h по (5.5), м; null — размах z охвата.</summary>
    public double? HeightM { get; init; }
    /// <summary>m по (5.5) — число колонн в ряду; по умолчанию 1 (в запас).</summary>
    public int M { get; init; } = 1;
    /// <summary>1/φ при <see cref="PhiManual"/>.</summary>
    public double? PhiInv { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, FemImperfectionJson.Options);
    public static FemSwayParams Parse(string? json) => FemImperfectionJson.Parse<FemSwayParams>(json);
}

/// <summary>
/// Параметры местной погиби: элементы (КонЭ или цепочки КЭ), плоскость (местная y или z), стрелка e₀, знак и
/// критерий активности. Форма — парабола w₀(ξ) = 4e₀·ξ(1 − ξ).
/// </summary>
public sealed record FemBowParams
{
    public const string PlaneY = "y";
    public const string PlaneZ = "z";
    /// <summary>L/e₀ по табл. 5.1 (кривая <see cref="Curve"/>, столбец — <see cref="Plastic"/>).</summary>
    public const string E0Table = "table";
    /// <summary>e₀ = L/<see cref="LOverE0"/>.</summary>
    public const string E0Relative = "relative";
    /// <summary>e₀ = <see cref="E0Mm"/>, мм.</summary>
    public const string E0Absolute = "absolute";
    public const string CurveAuto = "auto";
    public const string SignAuto = "auto";
    public const string SignPlus = "plus";
    public const string SignMinus = "minus";
    public const string ActivityAlways = "always";
    /// <summary>По п. 5.3.2(6): N_Ed > 0,25·N_cr и хотя бы один конец без шарнира в плоскости погиби.</summary>
    public const string ActivityEn1993 = "en1993";

    /// <summary>Теги конструктивных элементов: каждый — своя погибь по длине КонЭ.</summary>
    public List<string> MemberTags { get; init; } = [];
    /// <summary>Цепочки КЭ сетки (теги) — для схем без КонЭ: каждая — своя погибь.</summary>
    public List<List<string>> ElementChains { get; init; } = [];
    /// <summary><see cref="PlaneY"/> — погибь вдоль местной y первого КЭ; <see cref="PlaneZ"/> — вдоль местной z.</summary>
    public string Plane { get; init; } = PlaneY;
    /// <summary><see cref="E0Table"/> | <see cref="E0Relative"/> | <see cref="E0Absolute"/>.</summary>
    public string E0Mode { get; init; } = E0Relative;
    /// <summary>Кривая потери устойчивости: <see cref="CurveAuto"/> | a0 | a | b | c | d.</summary>
    public string Curve { get; init; } = CurveAuto;
    /// <summary>Столбец табл. 5.1: false — упругий расчёт, true — пластический.</summary>
    public bool Plastic { get; init; }
    /// <summary>L/e₀ при <see cref="E0Relative"/>.</summary>
    public double? LOverE0 { get; init; }
    /// <summary>e₀, мм, при <see cref="E0Absolute"/>.</summary>
    public double? E0Mm { get; init; }
    /// <summary><see cref="SignAuto"/> (по прогибу первого порядка) | <see cref="SignPlus"/> | <see cref="SignMinus"/>.</summary>
    public string Sign { get; init; } = SignAuto;
    /// <summary><see cref="ActivityAlways"/> | <see cref="ActivityEn1993"/>.</summary>
    public string Activity { get; init; } = ActivityEn1993;

    public string ToJson() => JsonSerializer.Serialize(this, FemImperfectionJson.Options);
    public static FemBowParams Parse(string? json) => FemImperfectionJson.Parse<FemBowParams>(json);
}
