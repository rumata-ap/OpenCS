using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
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

        document
            .Add(new ReportHeading(1, "Исходные данные"))
            .Add(new ReportKeyValueTable(
            [
                ("Форма сечения", parameters.ShapeKind == "rectangular" ? "прямоугольник" : parameters.ShapeKind),
                ("Ось изгиба", parameters.Axis),
                ("Схема статической определимости", LocalizeScheme(parameters.StructuralScheme)),
                ("Режим устойчивости", LocalizeStabilityMode(parameters.StabilityMode)),
                ("Длина элемента / расстояние между закреплениями L, м", F(parameters.ElementLengthOrRestraintDistance)),
                ("Расчётная длина l0, м", F(parameters.EffectiveLengthL0)),
                ("ψ (доля длительного момента)", F(parameters.Psi)),
                ("Порог гибкости l0/h", F(parameters.SlendernessThreshold)),
                ("Ручные усилия", parameters.UseManualForces
                    ? $"да: N = {F(parameters.N)} кН, Mx = {F(parameters.Mx)} кН·м, My = {F(parameters.My)} кН·м"
                    : "нет, используется набор усилий задачи")
            ], "Параметр", "Значение"))
            .Add(new ReportHeading(1, "Вердикт"))
            .Add(new ReportKeyValueTable(
            [
                ("Статус", LocalizeStatus(domain.Status)),
                ("Нормативная ветвь", LocalizeBranch(domain.Branch)),
                ("Вердикт прочности", VerdictText(domain))
            ], "Параметр", "Значение"));

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

    static void AddCheckTable(ReportDocument document, string heading, string note, List<CheckDetail> details)
    {
        if (details.Count == 0) return;
        document
            .Add(new ReportHeading(1, heading))
            .Add(new ReportParagraph(note))
            .Add(new ReportTable(
                ["Формула", "Описание", "Пункт СП", "Applied", "Allowable", "Ratio", "Результат", "Переменные"],
                details.Select(detail => (IReadOnlyList<string>)
                [
                    detail.Formula,
                    LocalizeKey(detail.Description),
                    detail.NormReference,
                    F(detail.Applied),
                    F(detail.Allowable),
                    F(detail.Ratio),
                    detail.Passed ? "выполнено" : "не выполнено",
                    FormatVariables(detail.Variables)
                ]).ToList()));
    }

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
                ("η", F(value.Eta)),
                ("Ncr, кН", F(value.Ncr)),
                ("D, кН·м²", F(value.D)),
                ("Гибкость l0/h превышает порог", value.Slender ? "да" : "нет"),
                ("Устойчивость обеспечена", value.Stable ? "да" : "нет"),
                ("M после усиления M0·η, кН·м", F(value.MEff)),
                ("Итераций решателя", value.Iterations.ToString()),
                ("Экстраполяция Эйткена не применена", value.ExtrapolationFailed ? "да" : "нет"),
                ("История η по проходам", value.EtaHistory.Length == 0
                    ? "—" : string.Join("; ", value.EtaHistory.Select(F)))
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

    static string FormatVariables(IReadOnlyDictionary<string, double> variables) =>
        variables.Count == 0
            ? ""
            : string.Join("; ", variables.OrderBy(v => v.Key).Select(v => $"{v.Key} = {F(v.Value)}"));

    static string F(double value) => SectionReportSections.F(value);
    static string F(double? value) => SectionReportSections.F(value);

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
        ["Sp63Normal_ShapeNotSupported"] = "Выбранная форма сечения пока не поддерживается.",
        ["Sp63Normal_InvalidAxis"] = "Выбрана неизвестная плоскость изгиба.",
        ["Sp63Normal_NonFiniteLoad"] = "Усилия должны быть конечными числами.",
        ["Sp63Normal_BiaxialLoad"] = "Упрощённая проверка одноосная; ненулевой момент в другой плоскости требует НДМ.",
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
        ["Sp63Normal_SuggestNdm"] = "Для отверстий, нескольких бетонных областей, двуосного изгиба и сложной арматуры используйте расчёт по деформационной модели (НДМ)."
    };
}
