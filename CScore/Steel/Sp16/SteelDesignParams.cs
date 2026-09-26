using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore.Sp16;

/// <summary>Позиция табл. 32 СП 16 (предельная гибкость сжатых элементов).</summary>
public enum CompressionMemberCategory
{
    /// <summary>1а — пояса, опорные раскосы и стойки плоских ферм, структурных и пространственных конструкций из труб, парных уголков (лямбда-профилей) высотой до 50 м: 180 − 60α.</summary>
    TrussChordPlanar = 0,
    /// <summary>1б — то же пространственных конструкций из одиночных уголков, а также из труб и парных уголков высотой св. 50 м: 120.</summary>
    TrussChordSpatial = 1,
    /// <summary>2а — прочие элементы плоских ферм, сварных пространственных и структурных конструкций: 210 − 60α.</summary>
    TrussWebPlanar = 2,
    /// <summary>2б — прочие элементы пространственных и структурных конструкций из одиночных уголков с болтовыми соединениями: 220 − 40α.</summary>
    TrussWebSpatialBolted = 3,
    /// <summary>3 — верхние пояса ферм, не закреплённые в процессе монтажа: 220.</summary>
    TrussTopChordErection = 4,
    /// <summary>4 — основные колонны: 180 − 60α.</summary>
    MainColumn = 5,
    /// <summary>5 — второстепенные колонны, элементы решётки колонн, вертикальные связи ниже балок крановых путей, балки и прогоны с учётом сжатия: 210 − 60α.</summary>
    SecondaryColumn = 6,
    /// <summary>6 — элементы связей (кроме поз. 5), стержни для уменьшения расчётной длины, ненагруженные элементы: 200.</summary>
    Bracing = 7,
    /// <summary>7 — сжатые и ненагруженные элементы пространственных конструкций таврового и крестового сечений под ветром (вертикальная плоскость): 150.</summary>
    WindCruciform = 8,
}

/// <summary>Позиция табл. 33 СП 16 (предельная гибкость растянутых элементов).</summary>
public enum TensionMemberCategory
{
    /// <summary>1 — пояса и опорные раскосы плоских ферм (включая тормозные) и структурных конструкций.</summary>
    TrussChord = 0,
    /// <summary>2 — прочие элементы ферм и структурных конструкций.</summary>
    TrussWeb = 1,
    /// <summary>3 — нижние пояса балок и ферм крановых путей.</summary>
    CraneTrussBottomChord = 2,
    /// <summary>4 — элементы вертикальных связей между колоннами (ниже балок крановых путей).</summary>
    ColumnBracing = 3,
    /// <summary>5 — прочие элементы связей.</summary>
    OtherBracing = 4,
    /// <summary>6 — пояса и опорные раскосы стоек и траверс, тяги траверс опор ЛЭП, ОРУ, КС.</summary>
    PowerLineChord = 5,
    /// <summary>7 — прочие элементы опор ЛЭП, ОРУ и КС.</summary>
    PowerLineOther = 6,
    /// <summary>8 — элементы пространственных конструкций таврового и крестового сечений под ветром (вертикальная плоскость).</summary>
    WindCruciform = 7,
}

/// <summary>Вид нагрузки для табл. 33.</summary>
public enum TensionLoadKind
{
    /// <summary>Динамические, приложенные непосредственно к конструкции.</summary>
    Dynamic = 0,
    /// <summary>Статические.</summary>
    Static = 1,
    /// <summary>От кранов и железнодорожных составов.</summary>
    Crane = 2,
}

/// <summary>Вид нагрузки в пролёте для ψ по табл. Ж.1.</summary>
public enum LtbLoadKind
{
    /// <summary>Сосредоточенная сила в середине пролёта.</summary>
    ConcentratedMid = 0,
    /// <summary>Сосредоточенная сила в четверти пролёта.</summary>
    ConcentratedQuarter = 1,
    /// <summary>Две сосредоточенные силы в третях пролёта.</summary>
    TwoConcentratedThirds = 2,
    /// <summary>Равномерно распределённая нагрузка.</summary>
    Uniform = 3,
    /// <summary>Чистый изгиб (равные концевые моменты одного знака).</summary>
    PureBending = 4,
    /// <summary>Момент на одном конце (треугольная эпюра M–0).</summary>
    EndMomentOneSide = 5,
    /// <summary>Равные концевые моменты разных знаков (M – −M).</summary>
    EndMomentsOpposite = 6,
    /// <summary>Сосредоточенная сила на конце консоли (табл. Ж.2).</summary>
    ConcentratedEnd = 7,
}

/// <summary>Закрепление сжатого пояса балки в пролёте (табл. Ж.1).</summary>
public enum LtbRestraints
{
    /// <summary>Без закреплений.</summary>
    None = 0,
    /// <summary>Одно в середине.</summary>
    OneAtMid = 1,
    /// <summary>Два и более, делящие пролёт на равные части.</summary>
    TwoOrMore = 2,
}

/// <summary>Вид эпюры моментов по длине стержня для 9.2.3 / табл. Д.5.</summary>
public enum MomentShape
{
    /// <summary>Расчётный момент задан (M принимается как есть).</summary>
    AsGiven = 0,
    /// <summary>
    /// Шарнирно опёртый стержень с линейной эпюрой от концевых моментов, δ = M2/M1: двоякосимметричное
    /// сечение — mef по табл. Д.5, сечение с одной осью симметрии в плоскости изгиба — M по табл. 20.
    /// </summary>
    LinearEndMoments = 1,
    /// <summary>
    /// Шарнирно опёртый стержень с поперечной нагрузкой: для сечения с одной осью симметрии в плоскости
    /// изгиба — M по табл. 20, момент в средней трети M1 = <see cref="SteelDesignParams.MiddleThirdMomentRatio"/>·M.
    /// </summary>
    PinnedTransverse = 2,
}

/// <summary>
/// Параметры проверки стального элемента по СП 16.13330.2017 (изм. № 1–6). Общие для
/// расчётных задач и конструктивных элементов расчётных схем (ParamsJson / DesignParamsJson).
/// Длины — м, силы — кН. Оси — оси контура сечения (x — горизонтальная).
/// </summary>
public sealed record SteelDesignParams
{
    /// <summary>Профиль; null — распознаётся по контуру.</summary>
    public SteelProfile? Profile { get; init; }

    /// <summary>Коэффициент условий работы γc (табл. 1).</summary>
    public double GammaC { get; init; } = 1.0;

    /// <summary>Расчётная длина lef,x = μx·lx для потери устойчивости в плоскости, перпендикулярной оси x (изгиб относительно x), м.</summary>
    public double LefX { get; init; } = 3.0;

    /// <summary>Расчётная длина lef,y = μy·ly, м.</summary>
    public double LefY { get; init; } = 3.0;

    /// <summary>Отношение площади нетто к брутто An/A (ослабление отверстиями), ≤ 1.</summary>
    public double NetAreaRatio { get; init; } = 1.0;

    /// <summary>Растянутый элемент, эксплуатация которого возможна после достижения предела текучести (7.1.1: Ru/γu вместо Ry).</summary>
    public bool TensionYieldAllowed { get; init; }

    /// <summary>Применять формулу (7а) с γres для прокатных двутавров (7.1.3, изм. № 6).</summary>
    public bool UseGammaRes { get; init; }

    /// <summary>Проверка по предельной гибкости определяющая: пределы λ̄uw, λ̄uf увеличиваются по 7.3.11 / 9.4.9.</summary>
    public bool SlendernessGovernsSection { get; init; }

    /// <summary>Учёт пластических деформаций (2-й/3-й класс, 8.2.3, (105), табл. Е.1). Только для статических нагрузок.</summary>
    public bool AllowPlastic { get; init; }

    /// <summary>γf — отношение расчётной эквивалентной нагрузки к нормативной для прим. 2 табл. Е.1 (c ≤ 1,15γf).</summary>
    public double GammaFEq { get; init; } = 1.0;

    /// <summary>Элемент подвергается непосредственному воздействию динамических нагрузок (запрещает (105)).</summary>
    public bool DynamicLoad { get; init; }

    // ── Устойчивость плоской формы изгиба (8.4, прил. Ж) ──

    /// <summary>Расчётная длина балки lef для φb (8.4.2), м; 0 — принимается LefY.</summary>
    public double LefB { get; init; }

    /// <summary>Вид нагрузки для ψ (табл. Ж.1).</summary>
    public LtbLoadKind LtbLoad { get; init; } = LtbLoadKind.Uniform;

    /// <summary>Закрепления сжатого пояса в пролёте (табл. Ж.1).</summary>
    public LtbRestraints LtbRestraints { get; init; } = LtbRestraints.None;

    /// <summary>Нагрузка приложена к растянутому поясу (иначе — к сжатому).</summary>
    public bool LtbLoadOnTensionFlange { get; init; }

    /// <summary>Концы балки защемлены (эпюры табл. Ж.1 с Mоп = Mпр).</summary>
    public bool LtbFixedEnds { get; init; }

    /// <summary>Консоль (табл. Ж.2).</summary>
    public bool Cantilever { get; init; }

    /// <summary>Нагрузка передаётся через сплошной жёсткий настил, связанный со сжатым поясом (8.4.4 а).</summary>
    public bool ContinuousRigidDeck { get; init; }

    // ── Местная нагрузка и стенка (8.2.2, 8.5) ──

    /// <summary>Сосредоточенная сила на пояс F для σloc (8.2.2), кН; 0 — нет.</summary>
    public double LocalForce { get; init; }

    /// <summary>Ширина опирания верхнего элемента b для lef = b + 2h (48), м.</summary>
    public double BearingLength { get; init; }

    /// <summary>Катет поясного шва kf сварной балки для h в (48), м (h = tf + kf).</summary>
    public double FlangeWeldLeg { get; init; }

    /// <summary>Проверяемое сечение в зоне чистого изгиба (8.2.3: β = 1, cxm = 0,5(1 + cx)).</summary>
    public bool PureBendingZone { get; init; }

    /// <summary>Пояса прикреплены односторонними швами (8.5.1: λ̄uw = 3,2).</summary>
    public bool OneSidedFlangeWelds { get; init; }

    /// <summary>Фрикционные поясные соединения (ψ = 4,5, табл. 12).</summary>
    public bool FrictionFlangeJoints { get; init; }

    /// <summary>Шаг поперечных рёбер жёсткости a, м; 0 — рёбер нет.</summary>
    public double RibSpacing { get; init; }

    // ── Сжатие с изгибом (9.2) ──

    /// <summary>Вид эпюры моментов (9.2.3, табл. Д.5).</summary>
    public MomentShape MomentShape { get; init; } = MomentShape.AsGiven;

    /// <summary>δ = M2/M1 для табл. Д.5 (от −1 до 1).</summary>
    public double EndMomentRatio { get; init; } = 1.0;

    /// <summary>
    /// Отношение наибольшего момента в средней трети длины к расчётному (M1 табл. 20, mx по 9.2.6;
    /// принимается не менее 0,5); 1 — принимается расчётный момент.
    /// </summary>
    public double MiddleThirdMomentRatio { get; init; } = 1.0;

    /// <summary>Элемент с одним защемлённым и другим свободным концом (9.2.3, 9.2.6).</summary>
    public bool CantileverColumn { get; init; }

    /// <summary>9.2.10: для двухсимметричного коробчатого сечения проверять по одному условию (121а) вместо (120), (121).</summary>
    public bool UseFormula121a { get; init; }

    // ── Предельная гибкость (10.4) ──

    /// <summary>Позиция табл. 32.</summary>
    public CompressionMemberCategory CompressionCategory { get; init; } = CompressionMemberCategory.MainColumn;

    /// <summary>Позиция табл. 33.</summary>
    public TensionMemberCategory TensionCategory { get; init; } = TensionMemberCategory.TrussWeb;

    /// <summary>Вид нагрузки табл. 33.</summary>
    public TensionLoadKind TensionLoad { get; init; } = TensionLoadKind.Static;

    /// <summary>Группа 4 по приложению В (10.4.2: предельная гибкость +10 %).</summary>
    public bool Group4 { get; init; }

    // ── Переопределения ──

    /// <summary>Тип сечения по табл. 7 относительно оси x (null — по профилю).</summary>
    public SectionCurve? CurveX { get; init; }

    /// <summary>Тип сечения по табл. 7 относительно оси y (null — по профилю).</summary>
    public SectionCurve? CurveY { get; init; }

    /// <summary>Коэффициент η по табл. Д.2 (null — по профилю).</summary>
    public double? EtaOverride { get; init; }

    // ── Устаревшие поля (миграция старого ParamsJson) ──

    /// <summary>Устар.: l0x до умножения на μ. Используется только при чтении старых задач.</summary>
    [JsonPropertyName("DesignLengthX")] public double? LegacyDesignLengthX { get; init; }
    /// <summary>Устар.</summary>
    [JsonPropertyName("DesignLengthY")] public double? LegacyDesignLengthY { get; init; }
    /// <summary>Устар.</summary>
    [JsonPropertyName("MuX")] public double? LegacyMuX { get; init; }
    /// <summary>Устар.</summary>
    [JsonPropertyName("MuY")] public double? LegacyMuY { get; init; }
    /// <summary>Устар.: lbit.</summary>
    [JsonPropertyName("DesignLengthBit")] public double? LegacyDesignLengthBit { get; init; }
    /// <summary>Устар.: «γM» (фактически γm, уже учтённый в Ry) — игнорируется.</summary>
    [JsonPropertyName("GammaM")] public double? LegacyGammaM { get; init; }
    /// <summary>Устар.: «βm» (в СП 16 отсутствует) — игнорируется.</summary>
    [JsonPropertyName("BetaM")] public double? LegacyBetaM { get; init; }

    /// <summary>Параметры получены из старого формата (для предупреждения в результате).</summary>
    [JsonIgnore] public bool MigratedFromLegacy { get; private init; }

    /// <summary>Расчётная длина для φb.</summary>
    [JsonIgnore] public double LefBOrY => LefB > 0 ? LefB : LefY;

    static readonly JsonSerializerOptions Opts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>JSON без устаревших полей.</summary>
    public string ToJson() => JsonSerializer.Serialize(this with
    {
        LegacyDesignLengthX = null, LegacyDesignLengthY = null, LegacyMuX = null, LegacyMuY = null,
        LegacyDesignLengthBit = null, LegacyGammaM = null, LegacyBetaM = null,
    }, Opts);

    /// <summary>
    /// Чтение параметров, включая старый формат (DesignLengthX·MuX → LefX, DesignLengthBit → LefB;
    /// GammaM и BetaM игнорируются).
    /// </summary>
    public static SteelDesignParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}") return new SteelDesignParams();
        var p = JsonSerializer.Deserialize<SteelDesignParams>(json, Opts) ?? new SteelDesignParams();
        bool legacy = p.LegacyDesignLengthX.HasValue || p.LegacyDesignLengthY.HasValue || p.LegacyGammaM.HasValue
                      || p.LegacyBetaM.HasValue || p.LegacyMuX.HasValue || p.LegacyDesignLengthBit.HasValue;
        if (!legacy) return p;
        using var doc = JsonDocument.Parse(json);
        bool hasNewLefX = doc.RootElement.TryGetProperty(nameof(LefX), out _);
        bool hasNewLefY = doc.RootElement.TryGetProperty(nameof(LefY), out _);
        return p with
        {
            LefX = hasNewLefX ? p.LefX : (p.LegacyDesignLengthX ?? 3.0) * (p.LegacyMuX ?? 1.0),
            LefY = hasNewLefY ? p.LefY : (p.LegacyDesignLengthY ?? 3.0) * (p.LegacyMuY ?? 1.0),
            LefB = p.LefB > 0 ? p.LefB : p.LegacyDesignLengthBit ?? 0,
            MigratedFromLegacy = true,
        };
    }
}

/// <summary>Усилия в сечении, кН и кН·м. N &gt; 0 — растяжение; Mx = ∫σ·y dA, My = ∫σ·x dA; Qy сопутствует Mx, Qx — My.</summary>
public sealed record SteelForces(double N, double Mx, double My, double Qx, double Qy)
{
    /// <summary>Перестановка осей (контур повёрнут на 90° относительно канонического положения профиля).</summary>
    public SteelForces SwapAxes() => new(N, My, Mx, Qy, Qx);
}
