using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.CalculationTrace;
using CScore.Sp63.Normal;

namespace OpenCS.Reporting;

/// <summary>Поставщик отчёта по упрощённой формульной проверке нормального сечения СП 63.</summary>
public sealed class Sp63NormalReportProvider : IReportProvider
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <inheritdoc/>
    public string TaskKind => "sp63_normal";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedKinds => [TaskKind];

    /// <inheritdoc/>
    public bool CanHandle(CalcTask task) => SupportedKinds.Contains(task.Kind, StringComparer.Ordinal);

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanHandle(context.Task))
            throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

        var domain = Parse(context.Result.DataJson);
        var parameters = Sp63NormalTaskParams.Parse(context.Task.ParamsJson);
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "без метки" : context.Task.Tag;
        var document = new ReportDocument($"Упрощённая проверка нормального сечения СП 63 — {tag}");
        SectionReportSections.Identification(document, context,
            "N — кН; M — кН·м; линейные размеры — м или мм по контексту показателя; безразмерные величины — как есть");

        bool roundShape = parameters.ShapeKind is "circular" or "annular";
        string shapeText = parameters.ShapeKind switch
        {
            "rectangular" => "прямоугольник",
            "tee" => "тавр/двутавр",
            "circular" => "круглое сплошное",
            "annular" => "кольцевое",
            _ => parameters.ShapeKind
        };
        var inputRows = new List<(string, string)>
        {
            ("Форма сечения", shapeText),
            ("Ось изгиба", roundShape ? "не используется (результирующий момент)" : parameters.Axis),
            ("Схема статической определимости", LocalizeScheme(parameters.StructuralScheme)),
            ("Режим устойчивости", LocalizeStabilityMode(parameters.StabilityMode)),
            ("Длина элемента / расстояние между закреплениями L, м", F(parameters.ElementLengthOrRestraintDistance, ReportUnit.Meter)),
            ("Расчётная длина l0, м", F(parameters.EffectiveLengthL0, ReportUnit.Meter)),
            ("ψ (доля длительного момента)", F(parameters.Psi, ReportUnit.Unitless)),
            ("Порог гибкости l0/i", F(parameters.SlendernessThreshold, ReportUnit.Unitless)),
            ("Ручные усилия", parameters.UseManualForces
                ? $"да: N = {F(parameters.N, ReportUnit.Kilonewton)} кН, Mx = {F(parameters.Mx, ReportUnit.KilonewtonMeter)} кН·м, My = {F(parameters.My, ReportUnit.KilonewtonMeter)} кН·м"
                : "нет, используется набор усилий задачи")
        };
        if (string.Equals(parameters.ShapeKind, "tee", StringComparison.OrdinalIgnoreCase))
            inputRows.Insert(4, ("Пролёт элемента l, м", F(parameters.SpanLength, ReportUnit.Meter)));

        document
            .Add(new ReportHeading(1, "Исходные данные"))
            .Add(new ReportKeyValueTable(inputRows, "Параметр", "Значение"));

        AddSectionDiagram(document, context, parameters, domain);

        document
            .Add(new ReportHeading(1, "Вердикт"))
            .Add(new ReportKeyValueTable(
            [
                ("Статус", LocalizeStatus(domain.Status)),
                ("Нормативная ветвь", LocalizeBranch(domain.Branch)),
                ("Вердикт прочности", VerdictText(domain))
            ], "Параметр", "Значение"));

        AddIdealizedRebarMarker(document, context.Section);

        AddTrace(document, domain);

        AddCheckTable(document, "Числовые условия прочности",
            "Входят в итоговый вердикт StrengthPassed.", domain.StrengthDetails);
        AddCheckTable(document, "Конструктивные требования раздела 10 (справочно)",
            "Справочные проверки минимального армирования, защитного слоя и расстановки стержней; в вердикт прочности не входят.",
            domain.ConstructiveChecks);
        AddMessageTable(document, "Причины неприменимости формульного режима", domain.ApplicabilityMessages);
        AddMessageTable(document, "Справочные сообщения", domain.InformationalMessages);

        if (domain.Variables.Count > 0)
            document
                .Add(new ReportHeading(1, "Переменные расчёта"))
                .Add(new ReportKeyValueTable(
                    domain.Variables.OrderBy(v => v.Key)
                        .Select(v => (v.Key, F(v.Value))).ToList(),
                    "Переменная", "Значение"));

        AddEta(document, domain.Eta);

        if (domain.Status == Sp63NormalStatus.NotApplicable)
            document.Add(new ReportWarning("Формульная проверка неприменима для данных исходных данных — см. причины неприменимости."));
        else if (domain.Status == Sp63NormalStatus.Calculated && domain.StrengthPassed == false)
            document.Add(new ReportWarning("Условие прочности не выполнено."));
        else if (domain.Status == Sp63NormalStatus.InvalidInput)
            document.Add(new ReportWarning("Исходные данные не прошли валидацию упрощённого режима СП 63."));

        return document;
    }

    static void AddIdealizedRebarMarker(ReportDocument document, CrossSection? section)
    {
        if (section is null) return;
        var layers = section.Areas
            .Where(a => a.RebarRepresentation == RebarRepresentation.IdealizedLayer)
            .SelectMany(a => a.Fibers.Where(f => f.TypeFiber == FiberType.point)
                .Select(f => new
                {
                    Area = f.Area,
                    Diameter = f.Diameter,
                    Axis = a.IdealizedAxis?.ToString() ?? "—",
                    Coordinate = a.IdealizedAxis == IdealizedRebarAxis.My ? f.X : f.Y
                }))
            .ToList();
        if (layers.Count == 0) return;

        document
            .Add(new ReportHeading(1, "Расчётные слои арматуры"))
            .Add(new ReportParagraph(
                "Расчётный слой — эквивалентная площадь As. Проверки 10.3.2 и 10.3.9 имеют информационный характер; процент армирования не отключён."))
            .Add(new ReportTable(
                ["As, м²", "φ, мм", "Координата, м", "Разрешённая ось"],
                layers.Select(layer => (IReadOnlyList<string>)[
                    F(layer.Area, ReportUnit.SquareMeter),
                    F(layer.Diameter * 1000.0, ReportUnit.Millimeter),
                    F(layer.Coordinate, ReportUnit.Meter), layer.Axis
                ]).ToList()));
    }

    static void AddCheckTable(ReportDocument document, string heading, string note, List<CheckDetail> details)
    {
        if (details.Count == 0) return;
        document
            .Add(new ReportHeading(1, heading))
            .Add(new ReportParagraph(note))
            .Add(new ReportTable(
                ["Формула", "Описание", "Пункт СП", "Факт", "Допуск", "Кисп.", "Результат"],
                details.Select(detail => (IReadOnlyList<string>)
                [
                    detail.Formula,
                    LocalizeKey(detail.Description),
                    detail.NormReference,
                    F(detail.Applied, DetailUnit(detail)),
                    F(detail.Allowable, DetailUnit(detail)),
                    F(detail.Ratio, ReportUnit.Unitless),
                    detail.Passed ? "выполнено" : "не выполнено"
                ]).ToList()));
    }

    static void AddSectionDiagram(ReportDocument document, ReportContext context,
        Sp63NormalTaskParams parameters, Sp63NormalResult domain)
    {
        document.Add(new ReportHeading(1, "Схема поперечного сечения"));
        if (context.Section is null)
        {
            document.Add(new ReportWarning("Модель сечения не передана: схема поперечного сечения недоступна."));
            return;
        }

        bool circular = parameters.ShapeKind is "circular" or "annular";
        ReportTensionSide side = circular
            ? ReportTensionSide.NotApplicable
            : domain.Variables.TryGetValue("tensionDirection", out var direction)
                ? direction >= 0 ? ReportTensionSide.Positive : ReportTensionSide.Negative
                : ReportTensionSide.Unknown;
        var options = new ReportSectionDiagramOptions
        {
            Axis = circular ? null : parameters.Axis,
            TensionSide = side,
            A = Value(domain, "a") ?? DerivedTensionCover(domain),
            APrime = Value(domain, "aPrime"),
            H0 = Value(domain, "h0")
        };
        if (context.ParametricSection is { } definition)
            document.Add(new ReportImage("Параметрическая схема сечения",
                new ParametricRcSectionSvgRenderer().Render(definition, options)));
        else
        {
            document.Add(new ReportImage("Универсальная схема сечения",
                new CrossSectionReportSvgRenderer().Render(context.Section)));
            if (!string.IsNullOrWhiteSpace(context.ParametricSectionWarning))
                document.Add(new ReportWarning(context.ParametricSectionWarning));
        }
    }

    static double? Value(Sp63NormalResult result, string key)
        => result.Variables.TryGetValue(key, out var value) && double.IsFinite(value)
            ? value : null;

    static double? DerivedTensionCover(Sp63NormalResult result)
    {
        double? h = Value(result, "h");
        double? h0 = Value(result, "h0");
        return h is double height && h0 is double effectiveHeight && height >= effectiveHeight
            ? height - effectiveHeight
            : null;
    }

    static void AddTrace(ReportDocument document, Sp63NormalResult domain)
    {
        if (domain.TraceSteps.Count == 0) return;
        document.Add(new ReportHeading(1, "Ход расчёта"));
        foreach (var step in domain.TraceSteps)
        {
            document.Add(new ReportCalculationStep
            {
                StepId = step.StepId,
                Reference = step.CodeReference ?? "",
                Title = LocalizeKey(step.TitleKey ?? "") is { Length: > 0 } title
                    ? title : "Расчётный шаг",
                Formula = ReportMathExpression.Latex(step.FormulaLatex ?? ""),
                Substitution = ReportMathExpression.Latex(
                    Substitute(step.SubstitutionLatexTemplate, step.Values)),
                Result = ReportMathExpression.Latex(
                    NormalizeUtilizationLabel(Substitute(step.ResultLatexTemplate, step.Values))),
                Unit = ResultUnit(step),
                Status = MapStatus(step.Status),
                StatusText = StatusText(step.Status)
            });
        }
    }

    static string Substitute(string? template,
        IReadOnlyDictionary<string, TraceValue> values)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";
        return System.Text.RegularExpressions.Regex.Replace(template,
            @"\{(?<name>[A-Za-z0-9_.-]+)\}", match =>
            {
                if (!values.TryGetValue(match.Groups["name"].Value, out var value) ||
                    !double.IsFinite(value.Value))
                    return ReportNumberFormatter.UndefinedPlaceholder;
                return ReportNumberFormatter.Format(value.Value, MapUnit(value.Unit));
        });
    }

    /// <summary>Заменяет старое обозначение коэффициента использования η в сохранённых результатах.
    /// η оставляется только для поправки прогиба по п. 8.1.15.</summary>
    static string NormalizeUtilizationLabel(string value)
        => value.Replace(@"\eta", "Кисп", StringComparison.Ordinal)
            .Replace("η", "Кисп", StringComparison.Ordinal);

    static string ResultUnit(CalculationTraceStep step)
    {
        string template = step.ResultLatexTemplate ?? "";
        string key = template.Contains("{ratio}", StringComparison.Ordinal)
            ? "ratio"
            : template.Contains("{xi}", StringComparison.Ordinal)
                ? "xi"
                : template.Contains("{x}", StringComparison.Ordinal)
                    ? "x"
                    : "allowable";
        if (step.Values.TryGetValue(key, out var value))
            return UnitText(value.Unit);
        return "";
    }

    static ReportUnit MapUnit(CalculationUnit unit) => unit switch
    {
        CalculationUnit.Centimeter => ReportUnit.Centimeter,
        CalculationUnit.Millimeter => ReportUnit.Millimeter,
        CalculationUnit.Meter => ReportUnit.Meter,
        CalculationUnit.Kilonewton => ReportUnit.Kilonewton,
        CalculationUnit.KilonewtonMeter => ReportUnit.KilonewtonMeter,
        CalculationUnit.Megapascal => ReportUnit.Megapascal,
        CalculationUnit.Kilopascal => ReportUnit.Kilopascal,
        CalculationUnit.SquareCentimeter => ReportUnit.SquareCentimeter,
        CalculationUnit.SquareMeter => ReportUnit.SquareMeter,
        CalculationUnit.Strain => ReportUnit.Strain,
        CalculationUnit.Count => ReportUnit.Count,
        _ => ReportUnit.Unitless
    };

    static string UnitText(CalculationUnit unit) => unit switch
    {
        CalculationUnit.Meter => "м",
        CalculationUnit.Centimeter => "см",
        CalculationUnit.Millimeter => "мм",
        CalculationUnit.Kilonewton => "кН",
        CalculationUnit.KilonewtonMeter => "кН·м",
        CalculationUnit.Kilopascal => "кПа",
        CalculationUnit.Megapascal => "МПа",
        CalculationUnit.SquareMeter => "м²",
        CalculationUnit.SquareCentimeter => "см²",
        _ => ""
    };

    static ReportCalculationStatus MapStatus(CalculationTraceStatus status) => status switch
    {
        CalculationTraceStatus.Passed => ReportCalculationStatus.Passed,
        CalculationTraceStatus.Failed => ReportCalculationStatus.Failed,
        CalculationTraceStatus.NotApplicable => ReportCalculationStatus.NotApplicable,
        CalculationTraceStatus.NoCapacity => ReportCalculationStatus.NoCapacity,
        CalculationTraceStatus.Error => ReportCalculationStatus.Error,
        _ => ReportCalculationStatus.Informational
    };

    static string StatusText(CalculationTraceStatus status) => status switch
    {
        CalculationTraceStatus.Passed => "выполнено",
        CalculationTraceStatus.Failed => "не выполнено",
        CalculationTraceStatus.NotApplicable => "не применяется",
        CalculationTraceStatus.NoCapacity => "несущая способность не определена",
        CalculationTraceStatus.Error => "ошибка",
        _ => "справочно"
    };

    static void AddMessageTable(ReportDocument document, string heading, List<Sp63NormalMessage> messages)
    {
        if (messages.Count == 0) return;
        document
            .Add(new ReportHeading(1, heading))
            .Add(new ReportTable(
                ["Код", "Пункт СП", "Текст"],
                messages.Select(message => (IReadOnlyList<string>)
                [
                    message.Code,
                    message.NormReference,
                    LocalizeKey(message.Text)
                ]).ToList()));
    }

    static void AddEta(ReportDocument document, CScore.Sp63.EccentricityAmplifier.EtaResult? eta)
    {
        if (eta is not { } value) return;
        document
            .Add(new ReportHeading(1, "Влияние прогиба η (п. 8.1.15)"))
            .Add(new ReportKeyValueTable(
            [
                ("η", F(value.Eta, ReportUnit.Unitless)),
                ("Ncr, кН", F(value.Ncr, ReportUnit.Kilonewton)),
                ("D, кН·м²", F(value.D, ReportUnit.KilonewtonMeter)),
                ("Гибкость l0/i превышает порог", value.Slender ? "да" : "нет"),
                ("Устойчивость обеспечена", value.Stable ? "да" : "нет"),
                ("M после усиления M0·η, кН·м", F(value.MEff, ReportUnit.KilonewtonMeter)),
                ("Итераций решателя", value.Iterations.ToString()),
                ("Экстраполяция Эйткена не применена", value.ExtrapolationFailed ? "да" : "нет"),
                ("История η по проходам", value.EtaHistory.Length == 0
                    ? "—" : string.Join("; ", value.EtaHistory.Select(item => F(item, ReportUnit.Unitless))))
            ], "Параметр", "Значение"));
        if (!value.Stable)
            document.Add(new ReportWarning("Расчётная устойчивость элемента не обеспечена (|N| ≥ Ncr)."));
    }

    static Sp63NormalResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("Пустой результат sp63_normal.");
        var model = JsonSerializer.Deserialize<Sp63NormalResult>(json, JsonOptions)
            ?? throw new JsonException("Пустой результат sp63_normal.");
        model.StrengthDetails ??= [];
        model.ConstructiveChecks ??= [];
        model.ApplicabilityMessages ??= [];
        model.InformationalMessages ??= [];
        model.Variables ??= [];
        model.TraceSteps ??= [];
        return model;
    }

    static string VerdictText(Sp63NormalResult domain) => domain.Status switch
    {
        Sp63NormalStatus.Calculated when domain.StrengthPassed == true => "прочность обеспечена",
        Sp63NormalStatus.Calculated => "прочность не обеспечена",
        Sp63NormalStatus.NotApplicable => "формульная проверка недоступна",
        _ => "исходные данные не прошли валидацию"
    };

    static string LocalizeStatus(Sp63NormalStatus status) => status switch
    {
        Sp63NormalStatus.Calculated => "выполнен",
        Sp63NormalStatus.NotApplicable => "неприменимо",
        _ => "ошибка исходных данных"
    };

    static string LocalizeScheme(string scheme) => scheme switch
    {
        "statically_determinate" => "статически определимая",
        "statically_indeterminate" => "статически неопределимая",
        _ => scheme
    };

    static string LocalizeStabilityMode(string mode) => mode switch
    {
        "member" => "с учётом гибкости элемента (η)",
        "section_only_explicit" => "только сечение, влияние прогиба исключено явно",
        _ => mode
    };

    static string LocalizeBranch(string branch) => branch switch
    {
        "compression" => "внецентренное сжатие",
        "bending" => "изгиб",
        "central_tension" => "центральное растяжение",
        "eccentric_tension_between" => "внецентренное растяжение, сила между арматурой",
        "eccentric_tension_outside" => "внецентренное растяжение, сила за пределами арматуры",
        "circular_bending" => "круглое сечение, изгиб",
        "circular_compression" => "круглое сечение, внецентренное сжатие",
        "annular_bending" => "кольцевое сечение, изгиб",
        "annular_compression" => "кольцевое сечение, внецентренное сжатие",
        "not_applicable" => "формульная проверка неприменима",
        "invalid_input" => "исходные данные не прошли валидацию",
        _ when !string.IsNullOrWhiteSpace(branch) => branch,
        _ => "не определено"
    };

    /// <summary>Переводит ключ локализации Sp63Normal_* в русский текст отчёта;
    /// произвольный текст возвращается как есть.</summary>
    static string LocalizeKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        return Texts.TryGetValue(key, out var text) ? text : key;
    }

    static ReportUnit DetailUnit(CheckDetail detail)
        => detail.Formula.Contains("M", StringComparison.OrdinalIgnoreCase)
            ? ReportUnit.KilonewtonMeter : ReportUnit.Kilonewton;

    static string F(double value, ReportUnit unit = ReportUnit.Unitless)
        => ReportNumberFormatter.Format(value, unit);

    static string F(double? value, ReportUnit unit = ReportUnit.Unitless)
        => value is double number ? F(number, unit) : ReportNumberFormatter.UndefinedPlaceholder;

    /// <summary>Русские тексты сообщений и описаний проверок; дублируют Resources/Strings.ru-RU.xaml,
    /// поскольку отчёт всегда печатается по-русски независимо от текущей локали UI и
    /// OpenCS.Reporting не ссылается на WPF-проект с ресурсами локализации.</summary>
    static readonly Dictionary<string, string> Texts = new()
    {
        ["Sp63Normal_InvalidInput"] = "Параметры задачи не соответствуют поддержанному контракту.",
        ["Sp63Normal_HandlerError"] = "Не удалось выполнить упрощённую проверку нормального сечения.",
        ["Sp63Normal_InvalidResultJson"] = "Данные результата повреждены или имеют неизвестный формат.",
        ["Sp63Normal_GeometryNotSupported"] = "Формульная проверка доступна только для сплошного прямоугольного сечения без отверстий.",
        ["Sp63Normal_InvalidTensionDirection"] = "Не удалось определить сторону растяжения по направлению момента.",
        ["Sp63Normal_MissingConcreteResistance"] = "Для бетона не задано расчётное сопротивление сжатию.",
        ["Sp63Normal_PrestressedRebar"] = "Предварительно напряжённая арматура не входит в упрощённый формульный режим.",
        ["Sp63Normal_MissingRebarMaterial"] = "Для арматуры не задан материал.",
        ["Sp63Normal_NonPointRebar"] = "Упрощённый режим учитывает только точечные стержни.",
        ["Sp63Normal_MissingRebarResistance"] = "Для арматуры не заданы расчётные сопротивления.",
        ["Sp63Normal_InvalidRebarArea"] = "Площадь стержня должна быть положительной.",
        ["Sp63Normal_MissingRebar"] = "В сечении не найдена продольная арматура.",
        ["Sp63Normal_MixedRebarResistance"] = "Сопротивления крайних слоёв арматуры должны совпадать.",
        ["Sp63Normal_InsufficientRebarLayers"] = "Для проверки нужны минимум два уровня продольной арматуры.",
        ["Sp63Normal_InvalidRebarGeometry"] = "Геометрия слоя арматуры не позволяет построить профиль.",
        ["Sp63Normal_NotATeeShape"] = "Контур является прямоугольником: выберите форму «Прямоугольное сплошное».",
        ["Sp63Normal_TeeGeometryNotSupported"] = "Формульная проверка тавра поддерживает только осевой контур из двух или трёх полос без отверстий.",
        ["Sp63Normal_UnsupportedLoadCaseForTee"] = "Упрощённая проверка тавра реализована только для чистого изгиба; внецентренное сжатие и растяжение требуют НДМ.",
        ["Sp63Normal_MissingSpanLength"] = "Для учёта свесов полки по п. 8.1.11 укажите пролёт элемента l.",
        ["Sp63Normal_TeeBendingCheck"] = "Изгиб тавра/двутавра: M ≤ Mult",
        ["Sp63Normal_TeeSymmetricBendingCheck"] = "Изгиб тавра/двутавра (симметричное армирование): M ≤ Mult",
        ["Sp63Normal_ShapeNotSupported"] = "Выбранная форма сечения пока не поддерживается.",
        ["Sp63Normal_InvalidAxis"] = "Выбрана неизвестная плоскость изгиба.",
        ["Sp63Normal_NonFiniteLoad"] = "Усилия должны быть конечными числами.",
        ["Sp63Normal_BiaxialLoad"] = "Упрощённая проверка одноосная; ненулевой момент в другой плоскости требует НДМ.",
        ["Sp63Normal_IdealizedRebarLayer"] = "Для расчётного слоя проверки 10.3.2 и 10.3.9 имеют информационный характер; процент армирования сохраняется.",
        ["Sp63Normal_IdealizedRebarAxisMismatch"] = "Ось расчётного слоя арматуры не совпадает с осью проверки.",
        ["Sp63Normal_IdealizedRebarBiaxialLoad"] = "Расчётный слой допускает только одноосную нагрузку по своей оси.",
        ["Sp63Normal_IdealizedRebarAxisInconsistent"] = "Расчётные слои имеют несовместимые оси изгиба.",
        ["Sp63Normal_IdealizedRebarTaskNotSupported"] = "Выбранный вид задачи не поддерживает расчётный слой арматуры.",
        ["Sp63Normal_ZeroLoad"] = "Нулевая комбинация N, Mx и My не образует проверяемого загружения.",
        ["Sp63Normal_InvalidCompressionZone"] = "Не удалось определить допустимую сжатую зону.",
        ["Sp63Normal_NonpositiveCapacity"] = "Несущая способность получилась неположительной.",
        ["Sp63Normal_CentralTensionCheck"] = "Центральное растяжение: N ≤ Rs·As",
        ["Sp63Normal_BendingCheck"] = "Изгиб: M ≤ Mult",
        ["Sp63Normal_SymmetricBendingCheck"] = "Изгиб симметричного армирования: M ≤ Mult",
        ["Sp63Normal_EccentricTensionCheck"] = "Внецентренное растяжение: N·e ≤ Mult",
        ["Sp63Normal_EccentricTensionPrimeCheck"] = "Внецентренное растяжение: N·e′ ≤ M′ult",
        ["Sp63Normal_CompressionCheck"] = "Внецентренное сжатие: N·e ≤ Mult",
        ["Sp63Normal_StabilityCheck"] = "Проверка устойчивости: N ≤ Ncr",
        ["Sp63Normal_CompressionZoneRatio"] = "Относительная высота сжатой зоны ξ = x/h0",
        ["Sp63Normal_Trace_CompressionZone"] = "Высота сжатой зоны",
        ["Sp63Normal_Trace_RelativeCompressionZone"] = "Относительная высота сжатой зоны",
        ["Sp63Normal_Trace_LimitComparison"] = "Сравнение с предельной высотой сжатой зоны",
        ["Sp63Normal_Trace_Capacity"] = "Предельный изгибающий момент",
        ["Sp63Normal_Trace_Strength"] = "Условие прочности",
        ["Sp63Normal_Trace_CentralTensionCapacity"] = "Предельная сила при центральном растяжении",
        ["Sp63Normal_Trace_ResultantMoment"] = "Результирующий момент круглого/кольцевого сечения",
        ["Sp63Normal_AccidentalEccentricity"] = "Учтён случайный эксцентриситет ea = max(L/600, h/30, 10 мм)",
        ["Sp63Normal_SymmetricBranch"] = "Применена ветвь симметричного армирования по п. 8.1.9.",
        ["Sp63Normal_StabilityExcludedExplicitly"] = "Влияние прогиба η явно исключено режимом «Только сечение».",
        ["Sp63Normal_UnstableElement"] = "Расчётная устойчивость элемента не обеспечена или коэффициент η не определён.",
        ["Sp63Normal_MissingAccidentalEccentricityLength"] = "Для сжатия в режиме элемента укажите длину L.",
        ["Sp63Normal_MissingEffectiveLength"] = "Для расчёта η укажите расчётную длину l0.",
        ["Sp63Normal_MissingConcreteStrain"] = "Для бетона не задана предельная деформация сжатия.",
        ["Sp63Normal_MissingRebarModulus"] = "Для арматуры не задан модуль упругости.",
        ["Sp63Normal_MinReinforcementTension"] = "Минимальный процент армирования, растянутая арматура: μs,min ≤ μs",
        ["Sp63Normal_MinReinforcementCompression"] = "Минимальный процент армирования, сжатая арматура: μs,min ≤ μs",
        ["Sp63Normal_MinReinforcementCentralTension"] = "Минимальный процент армирования при центральном растяжении (вся арматура к полному сечению бетона): μs,min ≤ μs",
        ["Sp63Normal_MinReinforcementSlendernessUnknown"] = "Минимальный процент армирования по п. 10.3.6 не проверен: не задана расчётная длина l0.",
        ["Sp63Normal_MinCoverTension"] = "Защитный слой растянутой арматуры, частично п. 10.3.2 (не менее диаметра стержня и не менее 10 мм; таблица 10.1 по условиям эксплуатации не проверяется)",
        ["Sp63Normal_MinCoverCompression"] = "Защитный слой сжатой арматуры, частично п. 10.3.2 (не менее диаметра стержня и не менее 10 мм; таблица 10.1 по условиям эксплуатации не проверяется)",
        ["Sp63Normal_CoverBarDiameterUnknown"] = "Диаметр стержней слоя не задан — проверка защитного слоя по п. 10.3.2 не выполнена.",
        ["Sp63Normal_MinTensionBarCount"] = "Число продольных растянутых стержней при ширине сечения более 150 мм, п. 10.3.9",
        ["Sp63Normal_SuggestNdm"] = "Для отверстий, нескольких бетонных областей, двуосного изгиба и сложной арматуры используйте расчёт по деформационной модели (НДМ).",
        ["Sp63Normal_CircularCheck"] = "Круглое сечение: M ≤ Mult",
        ["Sp63Normal_AnnularCheck"] = "Кольцевое сечение: M ≤ Mult",
        ["Sp63Normal_CircularSingleConcreteRegion"] = "Для круглого и кольцевого сечений по приложению Д нужна ровно одна бетонная область.",
        ["Sp63Normal_CircularHasHole"] = "Для формы «Круглое сплошное» бетонная область не должна иметь отверстий; для полого сечения выберите «Кольцевое».",
        ["Sp63Normal_AnnularHoleCount"] = "Для формы «Кольцевое» бетонная область должна иметь ровно одно отверстие.",
        ["Sp63Normal_NotCircularContour"] = "Контур не распознан как окружность: отклонение вершин по радиусу или площади многоугольника от круга превышает 1 %. Увеличьте число сегментов аппроксимации.",
        ["Sp63Normal_AnnularNotConcentric"] = "Отверстие кольца смещено относительно центра наружного контура более чем на 1 % наружного радиуса.",
        ["Sp63Normal_AnnularRadiusRatio"] = "Отношение r₁/r₂ меньше 0,5: по примечанию 3 приложения Д расчёт выполняется по общим правилам 8.1 (НДМ).",
        ["Sp63Normal_CircularTensionNotSupported"] = "Приложение Д относится к сжатым элементам; при растягивающей продольной силе используйте НДМ.",
        ["Sp63Normal_CircularInsufficientBars"] = "Формулы приложения Д применимы при числе продольных стержней не менее 7.",
        ["Sp63Normal_CircularRebarNotCentered"] = "Центр окружности стержней не совпадает с центром бетонного сечения (допуск 2 % rs).",
        ["Sp63Normal_CircularRebarUnequalAreas"] = "Площади продольных стержней различаются более чем на 1 %: раскладка неравномерная.",
        ["Sp63Normal_CircularRebarNotOnCircle"] = "Стержни не лежат на одной окружности: отклонение радиуса более 2 %.",
        ["Sp63Normal_CircularRebarNonUniform"] = "Угловой шаг стержней отличается от равномерного более чем на 5 %.",
        ["Sp63Normal_CircularRebarClassAboveA400"] = "По п. Д.2 класс арматуры круглого сечения должен быть не выше А400: расчётное сопротивление Rs (характеристики C) превышает 350 МПа.",
        ["Sp63Normal_CircularRebarClassByRs"] = "Условие «класс арматуры не выше А400» проверено косвенно: Rs по характеристикам C не превышает 350 МПа.",
        ["Sp63Normal_AppendixDRecommended"] = "Расчёт выполнен по рекомендуемому приложению Д СП 63.13330.2018.",
        ["Sp63Normal_AppendixDPureBendingExtension"] = "Приложение Д предназначено для внецентренно сжатых колонн; расчёт при N = 0 — расширение OpenCS.",
        ["Sp63Normal_ResultantMomentUsed"] = "Mx и My сведены к результирующему моменту M₀ = √(Mx² + My²): сечение и армирование осесимметричны.",
        ["Sp63Normal_CompressionExceedsSectionCapacity"] = "Продольная сжимающая сила превышает несущую способность сечения: условие прочности не выполняется."
    };
}
