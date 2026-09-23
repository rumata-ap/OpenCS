using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.Sp63.Deflection;

namespace OpenCS.Reporting;

/// <summary>Формирует русский отчёт по полной кривизне и прогибу по СП 63.</summary>
public sealed class Sp63DeflectionReportProvider : IReportProvider
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public string TaskKind => "sp63_deflection";
    public IReadOnlyCollection<string> SupportedKinds => [TaskKind];
    public bool CanHandle(CalcTask task) => SupportedKinds.Contains(task.Kind, StringComparer.Ordinal);

    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanHandle(context.Task))
            throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

        var result = Parse(context.Result.DataJson);
        var parameters = Sp63DeflectionTaskParams.Parse(context.Task.ParamsJson);
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "без метки" : context.Task.Tag;
        var document = new ReportDocument($"Прогиб по СП 63 — {tag}");
        SectionReportSections.Identification(document, context,
            "N/Nl — кН; M/Ml/Mcrc — кН·м; l — м; f/fult — мм; кривизна — 1/м");

        document
            .Add(new ReportHeading(1, "Исходные данные"))
            .Add(new ReportKeyValueTable(
            [
                ("Форма сечения", parameters.ShapeKind == "rectangular" ? "прямоугольник" : parameters.ShapeKind),
                ("Ось изгиба", parameters.Axis),
                ("Расчётная схема", SchemeText(result.Scheme)),
                ("Коэффициент схемы S", F(result.CoefficientS)),
                ("Пролёт l, м", F(result.SpanM)),
                ("Предельный прогиб fult, мм", F(result.DeflectionLimitMm)),
                ("Влажность среды", HumidityText(parameters.Humidity)),
                ("Режим длительных усилий", ForcesModeText(parameters.ForcesMode)),
                ("N — кН; Mx/My — кН·м", ForceText(result, "N", "M")),
                ("Nl — кН; Mxl/Myl — кН·м", ForceText(result, "Nl", "Ml"))
            ], "Параметр", "Значение"))
            .Add(new ReportHeading(1, "Вердикт"))
            .Add(new ReportKeyValueTable(
            [
                ("Статус расчёта", StatusText(result.Status)),
                ("Ветвь кривизны", result.Branch switch
                {
                    "cracked" => "с трещинами",
                    "not_cracked" => "без трещин",
                    _ => "не определена"
                }),
                ("Эксплуатационный вердикт", VerdictText(result))
            ], "Параметр", "Значение"));

        AddCurvatureTable(document, result);
        if (result.Status == Sp63DeflectionStatus.Calculated && result.Curvature is { } curvature)
        {
            document
                .Add(new ReportHeading(1, "Прогиб по формуле (8.139)"))
                .Add(new ReportFormula("(8.139)", "f = S·l²·|1/r|",
                    $"{F(result.CoefficientS)} · {F(result.SpanM)}² · |{F(curvature.Total)}|",
                    $"{F(result.DeflectionMm)} мм"))
                .Add(new ReportKeyValueTable(
                [
                    ("Расчётный прогиб f, мм", F(result.DeflectionMm)),
                    ("Предельный прогиб fult, мм", F(result.DeflectionLimitMm)),
                    ("Коэффициент использования f/fult", F(result.Utilization)),
                    ("Условие f ≤ fult", result.DeflectionPassed == true ? "выполнено" : "не выполнено")
                ], "Показатель", "Значение"));
        }
        else
            document.Add(new ReportParagraph("Полная кривизна и прогиб не вычислены."));

        AddMessages(document, "Причины неприменимости и ошибки исходных данных", result.ApplicabilityMessages);
        AddMessages(document, "Справочные сообщения", result.InformationalMessages);
        if (result.Variables.Count > 0)
            document.Add(new ReportHeading(1, "Переменные расчёта"))
                .Add(new ReportKeyValueTable(result.Variables.OrderBy(item => item.Key)
                    .Select(item => (VariableName(item.Key), VariableValue(item.Key, item.Value))).ToArray(),
                    "Переменная", "Значение"));

        if (result.Status == Sp63DeflectionStatus.NotApplicable)
            document.Add(new ReportWarning("Формульный расчёт прогиба неприменим для заданных данных."));
        else if (result.Status == Sp63DeflectionStatus.InvalidInput)
            document.Add(new ReportWarning("Расчёт не выполнен: проверьте исходные данные."));
        else if (result.DeflectionPassed == false)
            document.Add(new ReportWarning("Предельный прогиб превышен."));
        return document;
    }

    static Sp63DeflectionResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new JsonException("Пустой результат sp63_deflection.");
        var model = JsonSerializer.Deserialize<Sp63DeflectionResult>(json, JsonOptions)
            ?? throw new JsonException("Пустой результат sp63_deflection.");
        model.ApplicabilityMessages ??= [];
        model.InformationalMessages ??= [];
        model.Variables ??= [];
        return model;
    }

    static void AddCurvatureTable(ReportDocument document, Sp63DeflectionResult result)
    {
        if (result.Curvature is not { } curvature || curvature.Terms.Count == 0) return;
        document.Add(new ReportHeading(1, "Кривизна по пп. 8.2.23–8.2.30"))
            .Add(new ReportParagraph(curvature.Cracked
                ? $"Участок с трещинами, (8.141): (1/r) = (1/r)1 − (1/r)2 + (1/r)3 = {F(curvature.Total)} 1/м."
                : $"Участок без трещин, (8.140): (1/r) = (1/r)1 + (1/r)2 = {F(curvature.Total)} 1/м."))
            .Add(new ReportTable(
                ["Составляющая", "Действие нагрузки", "M, кН·м", "N, кН", "Eb1, МПа", "xm, мм", "D, кН·м²", "1/r, 1/м"],
                curvature.Terms.Select(term => (IReadOnlyList<string>)
                [
                    $"(1/r){term.Index}", term.LongTerm ? "длительное" : "кратковременное",
                    F(term.M), F(term.N), F(term.Eb1 / 1000),
                    double.IsNaN(term.Xm) ? "—" : F(term.Xm * 1000),
                    F(term.D) + (term.LimitedByUncracked ? " (ограничено п. 8.2.27)" : ""), F(term.Curvature)
                ]).ToArray()));
    }

    static void AddMessages(ReportDocument document, string heading, List<Sp63DeflectionMessage> messages)
    {
        if (messages.Count == 0) return;
        document.Add(new ReportHeading(1, heading)).Add(new ReportTable(["Код", "Пункт СП", "Текст"],
            messages.Select(message => (IReadOnlyList<string>)
                [message.Code, message.NormReference, Localize(message.Text)]).ToArray()));
    }

    static string Localize(string text) => Texts.TryGetValue(text, out var localized) ? localized : text;
    static string SchemeText(Sp63DeflectionStaticScheme scheme) => scheme switch
    {
        Sp63DeflectionStaticScheme.SimplySupportedUniform => "шарнирно опёртая балка, равномерная нагрузка",
        Sp63DeflectionStaticScheme.SimplySupportedMidpoint => "шарнирно опёртая балка, сила в середине пролёта",
        Sp63DeflectionStaticScheme.CantileverTip => "консоль, сила на свободном конце",
        _ => "неизвестная схема"
    };
    static string HumidityText(string humidity) => humidity switch
    { "above_75" => "выше 75 %", "below_40" => "ниже 40 %", _ => "40–75 %" };
    static string ForcesModeText(string mode) => mode switch
    { "share" => "доля полной нагрузки", "manual" => "ручные длительные усилия", _ => "полная нагрузка" };
    static string StatusText(Sp63DeflectionStatus status) => status switch
    { Sp63DeflectionStatus.Calculated => "расчёт выполнен", Sp63DeflectionStatus.NotApplicable => "неприменимо", _ => "ошибка исходных данных" };
    static string VerdictText(Sp63DeflectionResult result) => result.Status switch
    {
        Sp63DeflectionStatus.Calculated when result.DeflectionPassed == true => "прогиб не превышает предельное значение",
        Sp63DeflectionStatus.Calculated when result.DeflectionPassed == false => "предельный прогиб превышен",
        Sp63DeflectionStatus.NotApplicable => "формульный расчёт неприменим",
        _ => "исходные данные не прошли проверку"
    };
    static string ForceText(Sp63DeflectionResult result, string n, string m) =>
        $"N = {F(result.Variables.GetValueOrDefault(n))} кН; M = {F(result.Variables.GetValueOrDefault(m))} кН·м";
    static string VariableName(string key) => key switch
    { "M" => "M исходный подписанный, кН·м", "Ml" => "Ml исходный подписанный, кН·м", "McrcFull" => "Mcrc, полный, кН·м", "McrcLong" => "Mcrc, длительный, кН·м", "S" => "S", "l" => "l, м", "f" => "f, мм", "fult" => "fult, мм", _ => key };
    static string VariableValue(string key, double value) => key switch
    { "axis" => value == 0 ? "Mx" : "My", _ => F(value) };
    static string F(double value) => SectionReportSections.F(value);

    static readonly Dictionary<string, string> Texts = new()
    {
        ["Sp63Deflection_InvalidInput"] = "Исходные параметры не соответствуют поддержанному формульному расчёту.",
        ["Sp63Deflection_InvalidOptions"] = "Параметры расчёта имеют недопустимое значение.",
        ["Sp63Deflection_ShapeNotSupported"] = "Формульный прогиб поддерживает только прямоугольное сечение.",
        ["Sp63Deflection_InvalidSpan"] = "Пролёт должен быть конечным положительным числом.",
        ["Sp63Deflection_InvalidLimit"] = "Предельный прогиб должен быть конечным положительным числом.",
        ["Sp63Deflection_InvalidLongTermShare"] = "Доля длительной нагрузки должна находиться в диапазоне от 0 до 1.",
        ["Sp63Deflection_NonFiniteLoad"] = "Усилия должны быть конечными числами.",
        ["Sp63Deflection_BiaxialLoad"] = "Формульный расчёт прогиба применим только при изгибе относительно одной оси.",
        ["Sp63Deflection_ZeroMoment"] = "Нулевой момент не задаёт характерную кривизну изгиба.",
        ["Sp63Deflection_LongMomentReversed"] = "Длительный момент меняет знак относительно полного и меняет растянутую грань.",
        ["Sp63Deflection_LongMomentExceedsTotal"] = "Модуль длительного момента превышает модуль полного момента.",
        ["Sp63Deflection_MissingConcreteChars"] = "Для бетона не заданы необходимые характеристики материала.",
        ["Sp63Deflection_MissingRebarChars"] = "Для арматуры не заданы необходимые характеристики материала.",
        ["Sp63Deflection_InvalidGeometry"] = "Не удалось определить положительную ширину сечения.",
        ["Sp63Deflection_CurvatureThroughTension"] = "Сечение растянуто насквозь; формульная кривизна не определена.",
        ["Sp63Deflection_MissingConcreteClass"] = "Для бетона не задан класс, необходимый для расчёта длительной кривизны.",
        ["Sp63Deflection_NonFiniteResult"] = "Расчёт прогиба дал неконечное значение.",
        ["Sp63Deflection_IdealizedAxisMismatch"] = "Ось расчётного слоя арматуры не совпадает с выбранной осью изгиба.",
        ["Sp63Deflection_IdealizedAxisInconsistent"] = "Расчётные слои арматуры имеют несовместимые оси.",
        ["Sp63Deflection_IdealizedRebarNotSupported"] = "Расчётный слой арматуры неприменим к выбранной нагрузке.",
        ["Sp63Deflection_ConstantStiffnessNote"] = "Прогиб оценён по кривизне характерного сечения при постоянной изгибной жёсткости.",
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
