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
    /// <summary>Формы потери устойчивости постановки <c>csfea_buckling</c> (п. 5.3.2(11)), параметры <see cref="FemModesParams"/>.</summary>
    public const string Modes = "modes";
    /// <summary>Погибь стен по ярусам между перекрытиями (EN 1992-1-1 п. 5.2(7)a, (9); п. 8.1.7 СП 63), параметры <see cref="FemWallBowParams"/>.</summary>
    public const string WallBow = "wall_bow";
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
    /// <summary>Параметры вида: <see cref="FemSwayParams"/>, <see cref="FemBowParams"/> или <see cref="FemModesParams"/>.</summary>
    public string ParamsJson { get; set; } = "{}";
    public string? Comment { get; set; }

    /// <summary>Подпись для отчёта.</summary>
    [JsonIgnore]
    public string Label => !string.IsNullOrWhiteSpace(Tag) ? Tag : Kind switch
    {
        FemImperfectionKinds.Sway => $"Крен #{Id}",
        FemImperfectionKinds.Bow => $"Погибь #{Id}",
        FemImperfectionKinds.Modes => $"Формы #{Id}",
        FemImperfectionKinds.WallBow => $"Погибь стены #{Id}",
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
    /// <summary>θᵢ = θ₀·α_h·α_m по EN 1992-1-1 (5.1): l — <see cref="HeightM"/> (null — размах z охвата), m — <see cref="M"/>.</summary>
    public const string PhiEn1992 = "en1992";
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
    /// <summary><see cref="PhiEn1993"/> | <see cref="PhiEn1992"/> | <see cref="PhiManual"/>.</summary>
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
    /// <summary>eᵢ = θᵢ·l₀/2 по EN 1992-1-1 (5.2): l = L цепочки, m = 1, l₀ = <see cref="Mu"/>·L.</summary>
    public const string E0En1992 = "en1992";
    /// <summary>eᵢ = l₀/400 по EN 1992-1-1 п. 5.2(7)a, (9), l₀ = <see cref="Mu"/>·L.</summary>
    public const string E0En1992Simplified = "en1992_l0_400";
    /// <summary>e_a по п. 8.1.7 СП 63: max(L/600, h/30, 10 мм), h — высота сечения в плоскости погиби.</summary>
    public const string E0Sp63 = "sp63_ea";
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
    /// <summary><see cref="E0Table"/> | <see cref="E0Relative"/> | <see cref="E0Absolute"/> | <see cref="E0En1992"/> |
    /// <see cref="E0En1992Simplified"/> | <see cref="E0Sp63"/>.</summary>
    public string E0Mode { get; init; } = E0Relative;
    /// <summary>μ расчётной длины l₀ = μ·L для режимов EN 1992; null — 1.</summary>
    public double? Mu { get; init; }
    /// <summary>Высота сечения h в плоскости погиби, мм, для <see cref="E0Sp63"/>; null — по сечению КЭ.</summary>
    public double? SectionHeightMm { get; init; }
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

    /// <summary>Режим e₀ — ЖБ (EN 1992-1-1 или СП 63): активность всегда (EN 1992-1-1 п. 5.2(1)P), кривой нет.</summary>
    public static bool IsConcreteMode(string? e0Mode) => e0Mode is E0En1992 or E0En1992Simplified or E0Sp63;
}

/// <summary>
/// Параметры погиби стен: стены (КонЭ или КЭ пластин), стрелка e₀ по ярусам между уровнями раскрепления (перекрытиями,
/// <see cref="FemBracedLength.WallTier"/>), знак. Форма в каждом ярусе — парабола вдоль нормали стенки
/// w₀(ζ) = 4e₀·ζ(1 − ζ), ζ = (z − z_низ)/h_яр.
/// </summary>
public sealed record FemWallBowParams
{
    /// <summary>Теги КонЭ-стен (их КЭ сетки).</summary>
    public List<string> MemberTags { get; init; } = [];
    /// <summary>Теги КЭ пластин — для схем без КонЭ.</summary>
    public List<string> ElementTags { get; init; } = [];
    /// <summary><see cref="FemBowParams.E0En1992Simplified"/> (по умолчанию) | <see cref="FemBowParams.E0En1992"/> |
    /// <see cref="FemBowParams.E0Sp63"/> | <see cref="FemBowParams.E0Relative"/> (h_яр/x) | <see cref="FemBowParams.E0Absolute"/>.</summary>
    public string E0Mode { get; init; } = FemBowParams.E0En1992Simplified;
    /// <summary>μ: l₀ = μ·h_яр; null — 1.</summary>
    public double? Mu { get; init; }
    /// <summary>h_яр/e₀ при <see cref="FemBowParams.E0Relative"/>.</summary>
    public double? LOverE0 { get; init; }
    /// <summary>e₀, мм, при <see cref="FemBowParams.E0Absolute"/>.</summary>
    public double? E0Mm { get; init; }
    /// <summary>Толщина стены, мм, для п. 8.1.7 (h/30); null — толщина КЭ.</summary>
    public double? ThicknessMm { get; init; }
    /// <summary><see cref="FemBowParams.SignAuto"/> (по прогибу первого порядка — для каждого яруса) | plus | minus (по нормали стенки).</summary>
    public string Sign { get; init; } = FemBowParams.SignAuto;

    public string ToJson() => JsonSerializer.Serialize(this, FemImperfectionJson.Options);
    public static FemWallBowParams Parse(string? json) => FemImperfectionJson.Parse<FemWallBowParams>(json);
}
