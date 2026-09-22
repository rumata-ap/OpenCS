using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.Sp63.CrackWidth;

namespace OpenCS.Reporting;

/// <summary>Поставщик отчёта по упрощённой формульной проверке ширины раскрытия трещин СП 63.</summary>
public sealed class Sp63CrackWidthReportProvider : IReportProvider
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <inheritdoc/>
    public string TaskKind => "sp63_crack_width";

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
        var parameters = Sp63CrackWidthTaskParams.Parse(context.Task.ParamsJson);
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "без метки" : context.Task.Tag;
        var document = new ReportDocument($"Упрощённая проверка ширины раскрытия трещин СП 63 — {tag}");
        SectionReportSections.Identification(document, context,
            "N — кН; M — кН·м; линейные размеры — м или мм по контексту показателя; acrc, acrc,lim — мм");

        document
            .Add(new ReportHeading(1, "Исходные данные"))
            .Add(new ReportKeyValueTable(
            [
                ("Форма сечения", parameters.ShapeKind == "rectangular" ? "прямоугольник" : parameters.ShapeKind),
                ("Ось изгиба", parameters.Axis),
                ("φ1 (длительность действия нагрузки), п. 8.2.10", F(parameters.Phi1)),
                ("φ2 (профиль арматуры), п. 8.2.10", F(parameters.Phi2)),
                ("acrc,lim, мм", F(parameters.AcrcLimMm)),
                ("Ручные усилия", parameters.UseManualForces
                    ? $"да: N = {F(parameters.N)} кН, Mx = {F(parameters.Mx)} кН·м, My = {F(parameters.My)} кН·м"
                    : "нет, используется набор усилий задачи")
            ], "Параметр", "Значение"))
            .Add(new ReportHeading(1, "Вердикт"))
            .Add(new ReportKeyValueTable(
            [
                ("Статус", LocalizeStatus(domain.Status)),
                ("Нормативная ветвь", LocalizeBranch(domain.Branch)),
                ("Трещины образуются", domain.Cracked switch
                {
                    true => "да", false => "нет", _ => "—"
                }),
                ("Вердикт", VerdictText(domain))
            ], "Параметр", "Значение"));

        AddCheckTable(document, "Числовое условие acrc ≤ acrc,lim",
            "Входит в итоговый вердикт LimitPassed.", domain.Details);
        AddMessageTable(document, "Причины неприменимости формульного режима", domain.ApplicabilityMessages);
        AddMessageTable(document, "Справочные сообщения", domain.InformationalMessages);

        if (domain.Variables.Count > 0)
            document
                .Add(new ReportHeading(1, "Переменные расчёта"))
                .Add(new ReportKeyValueTable(
                    domain.Variables.OrderBy(v => v.Key)
                        .Select(v => (v.Key, F(v.Value))).ToList(),
                    "Переменная", "Значение"));

        if (domain.Status == Sp63CrackWidthStatus.NotApplicable)
            document.Add(new ReportWarning("Формульная проверка неприменима для данных исходных данных — см. причины неприменимости."));
        else if (domain.Status == Sp63CrackWidthStatus.Calculated && domain.LimitPassed == false)
            document.Add(new ReportWarning("Ширина раскрытия трещин превышает предельно допустимую."));
        else if (domain.Status == Sp63CrackWidthStatus.InvalidInput)
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
                ["Формула", "Описание", "Пункт СП", "acrc, мм", "acrc,lim, мм", "Ratio", "Результат", "Переменные"],
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

    static void AddMessageTable(ReportDocument document, string heading, List<Sp63CrackWidthMessage> messages)
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

    static Sp63CrackWidthResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("Пустой результат sp63_crack_width.");
        var model = JsonSerializer.Deserialize<Sp63CrackWidthResult>(json, JsonOptions)
            ?? throw new JsonException("Пустой результат sp63_crack_width.");
        model.Details ??= [];
        model.ApplicabilityMessages ??= [];
        model.InformationalMessages ??= [];
        model.Variables ??= [];
        return model;
    }

    static string VerdictText(Sp63CrackWidthResult domain) => domain.Status switch
    {
        Sp63CrackWidthStatus.Calculated when domain.LimitPassed == true => "ширина раскрытия трещин в пределах допустимой",
        Sp63CrackWidthStatus.Calculated => "ширина раскрытия трещин превышает допустимую",
        Sp63CrackWidthStatus.NotApplicable => "формульная проверка недоступна",
        _ => "исходные данные не прошли валидацию"
    };

    static string LocalizeStatus(Sp63CrackWidthStatus status) => status switch
    {
        Sp63CrackWidthStatus.Calculated => "выполнен",
        Sp63CrackWidthStatus.NotApplicable => "неприменимо",
        _ => "ошибка исходных данных"
    };

    static string LocalizeBranch(string branch) => branch switch
    {
        "cracked" => "трещины образуются",
        "not_cracked" => "трещины не образуются",
        "not_applicable" => "формульная проверка неприменима",
        "invalid_input" => "исходные данные не прошли валидацию",
        _ when !string.IsNullOrWhiteSpace(branch) => branch,
        _ => "не определено"
    };

    /// <summary>Переводит ключ локализации Sp63CrackWidth_*/Sp63Normal_* в русский текст отчёта
    /// (общие причины неприменимости геометрии/раскладки арматуры дословно совпадают с
    /// Sp63Normal — см. Sp63CrackWidthMessage); произвольный текст возвращается как есть.</summary>
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
    /// OpenCS.Reporting не ссылается на WPF-проект с ресурсами локализации. Часть ключей
    /// ("Sp63Normal_*") общая с Sp63NormalReportProvider — причины неприменимости геометрии и
    /// раскладки арматуры распознаются тем же Sp63RebarLayoutAnalyzer.</summary>
    static readonly Dictionary<string, string> Texts = new()
    {
        ["Sp63CrackWidth_InvalidInput"] = "Параметры задачи не соответствуют поддержанному контракту.",
        ["Sp63CrackWidth_HandlerError"] = "Не удалось выполнить упрощённую проверку ширины раскрытия трещин.",
        ["Sp63CrackWidth_InvalidResultJson"] = "Данные результата повреждены или имеют неизвестный формат.",
        ["Sp63CrackWidth_ShapeNotSupported"] = "Выбранная форма сечения пока не поддерживается.",
        ["Sp63CrackWidth_InvalidAxis"] = "Выбрана неизвестная плоскость изгиба.",
        ["Sp63CrackWidth_NonFiniteLoad"] = "Усилия должны быть конечными числами.",
        ["Sp63CrackWidth_InvalidPhi"] = "Коэффициенты φ1 и φ2 должны быть положительными конечными числами.",
        ["Sp63CrackWidth_InvalidAcrcLimit"] = "Предельная ширина раскрытия трещин должна быть положительной.",
        ["Sp63CrackWidth_BiaxialLoad"] = "Упрощённая проверка одноосная; ненулевой момент в другой плоскости требует деформационной модели.",
        ["Sp63CrackWidth_ZeroMoment"] = "Упрощённый путь построен для изгиба/внецентренного нагружения с ненулевым моментом; центральное растяжение/сжатие им не покрывается.",
        ["Sp63CrackWidth_MissingConcreteChars"] = "Для бетона не заданы характеристики Eb, Rb,ser, Rbt,ser.",
        ["Sp63CrackWidth_MissingRebarChars"] = "Для арматуры не заданы модуль упругости Es и Rs,ser.",
        ["Sp63CrackWidth_AcrcCheck"] = "Ширина раскрытия трещин: acrc ≤ acrc,lim",
        ["Sp63CrackWidth_NeutralAxisNote"] = "Высота сжатой зоны сечения с трещиной — по п. 8.2.28 с поправкой (8.154) на продольную силу; при xm ≤ 0 сечение растянуто насквозь и растяжение воспринимают оба ряда арматуры.",
        ["Sp63CrackWidth_ThroughTensionOppositeRow"] = "Сечение растянуто насквозь (xm ≤ 0); решает ряд арматуры у грани, которую момент не растягивает.",
        ["Sp63CrackWidth_NotCracked"] = "M ≤ Mcrc — трещины не образуются, acrc = 0.",
        ["Sp63CrackWidth_SuggestFullModel"] = "Для двуосного изгиба, центрального растяжения/сжатия и других случаев вне упрощённого пути используйте расчёт по деформационной модели (задача «Ширина раскрытия трещин»).",
        // Общие с Sp63NormalReportProvider причины неприменимости геометрии и раскладки арматуры
        // (Sp63RebarLayoutAnalyzer/Sp63RectangularGeometryPolicy).
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
        ["Sp63Normal_InvalidRebarGeometry"] = "Геометрия слоя арматуры не позволяет построить профиль."
    };
}
