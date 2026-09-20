using System.Text.Json;

namespace OpenCS.Reporting;

/// <summary>Reporting-модель JSON результата наклонного сечения.</summary>
public sealed class ShearInclinedReportData
{
    /// <summary>Метка сечения.</summary>
    public string SectionTag { get; private init; } = "";

    /// <summary>Метка строки усилий.</summary>
    public string ForceLabel { get; private init; } = "";

    /// <summary>Идентификатор версии trace.</summary>
    public int TraceVersion { get; private init; }

    /// <summary>Входные данные по плоскостям.</summary>
    public Dictionary<string, Dictionary<string, double?>> Inputs { get; private init; } = [];

    /// <summary>Шаги расчётной трассировки.</summary>
    public List<ShearInclinedReportTraceStep> TraceSteps { get; private init; } = [];

    /// <summary>Таблица стоянок.</summary>
    public List<ShearInclinedReportStation> Stations { get; private init; } = [];

    /// <summary>Предупреждения расчёта.</summary>
    public List<string> Warnings { get; private init; } = [];

    /// <summary>Итоговый коэффициент использования по точным проверкам.</summary>
    public double? UtilizationExact { get; private init; }

    /// <summary>Статус итогового коэффициента.</summary>
    public string UtilizationStatus { get; private init; } = "ok";

    /// <summary>Оговорка о применимости расчёта.</summary>
    public string ApplicabilityStatus { get; private init; } = "ok";

    /// <summary>Разбирает новый и старый JSON-контракт результата.</summary>
    public static ShearInclinedReportData Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new() { UtilizationStatus = "error" };

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var data = new ShearInclinedReportData
        {
            SectionTag = String(root, "sectionTag") ?? "",
            ForceLabel = String(root, "forceLabel") ?? "",
            TraceVersion = Integer(root, "traceVersion") ?? 0,
            UtilizationExact = Number(root, "utilizationExact") ?? Number(root, "utilization"),
            UtilizationStatus = String(root, "utilizationStatus") ?? "ok",
            ApplicabilityStatus = String(
                root.TryGetProperty("applicability", out var applicability)
                    ? applicability : default, "status") ?? "ok",
            Inputs = ReadInputs(root),
            TraceSteps = ReadTraceSteps(root),
            Stations = ReadStations(root),
            Warnings = ReadStrings(root, "warnings")
        };

        if (data.TraceSteps.Count == 0)
            data.TraceSteps.AddRange(ReadLegacyDetails(root));
        return data;
    }

    static Dictionary<string, Dictionary<string, double?>> ReadInputs(JsonElement root)
    {
        var result = new Dictionary<string, Dictionary<string, double?>>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var plane in inputs.EnumerateObject())
        {
            var values = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            if (plane.Value.ValueKind == JsonValueKind.Object)
                foreach (var value in plane.Value.EnumerateObject())
                    values[value.Name] = Number(value.Value);
            result[plane.Name] = values;
        }
        return result;
    }

    static List<ShearInclinedReportTraceStep> ReadTraceSteps(JsonElement root)
    {
        var result = new List<ShearInclinedReportTraceStep>();
        if (!root.TryGetProperty("traceSteps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var step in steps.EnumerateArray())
        {
            var values = new Dictionary<string, ShearInclinedReportTraceValue>(StringComparer.Ordinal);
            if (step.TryGetProperty("values", out var valueObject) && valueObject.ValueKind == JsonValueKind.Object)
                foreach (var value in valueObject.EnumerateObject())
                {
                    var valueData = value.Value;
                    values[value.Name] = new ShearInclinedReportTraceValue(
                        Number(valueData, "value"), String(valueData, "unit"));
                }
            result.Add(new ShearInclinedReportTraceStep
            {
                StepId = String(step, "stepId") ?? "",
                CodeReference = String(step, "codeReference") ?? "",
                FormulaLatex = String(step, "formulaLatex") ?? "",
                SubstitutionTemplate = String(step, "substitutionLatexTemplate") ?? "",
                ResultTemplate = String(step, "resultLatexTemplate") ?? "",
                Status = String(step, "status") ?? "informational",
                Plane = String(step, "plane") ?? "",
                FormulaCode = String(step, "formulaCode") ?? "",
                StationIndex = Integer(step, "stationIndex"),
                StationS = Number(step, "stationS"),
                CriticalC = Number(step, "criticalC"),
                Values = values
            });
        }
        return result;
    }

    static List<ShearInclinedReportTraceStep> ReadLegacyDetails(JsonElement root)
    {
        var result = new List<ShearInclinedReportTraceStep>();
        if (!root.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
            return result;
        int index = 0;
        foreach (var detail in details.EnumerateArray())
        {
            var values = new Dictionary<string, ShearInclinedReportTraceValue>(StringComparer.Ordinal)
            {
                ["applied"] = new(Number(detail, "applied"), UnitFor(detail, "formula", "applied")),
                ["allowable"] = new(Number(detail, "allowable"), UnitFor(detail, "formula", "allowable")),
                ["ratio"] = new(Number(detail, "ratio"), "Unitless")
            };
            if (detail.TryGetProperty("variables", out var variables) && variables.ValueKind == JsonValueKind.Object)
                foreach (var value in variables.EnumerateObject())
                    values[value.Name] = new(Number(value.Value), UnitFor(detail, "formula", value.Name));
            string formula = String(detail, "formula") ?? "";
            result.Add(new ShearInclinedReportTraceStep
            {
                StepId = $"legacy.shear.{index++}",
                CodeReference = String(detail, "normRef") ?? "",
                FormulaCode = formula,
                Plane = String(detail, "plane") ?? "",
                FormulaLatex = FormulaFor(formula),
                SubstitutionTemplate = @"{applied} \le {allowable}",
                ResultTemplate = @"Кисп = {ratio}",
                Status = formula is "8.55" or "8.56" or "8.63"
                    ? (Boolean(detail, "passed") == true ? "passed" : "failed")
                    : "informational",
                StationS = Value(values, "s"),
                CriticalC = Value(values, "C"),
                Values = values
            });
        }
        return result;
    }

    static List<ShearInclinedReportStation> ReadStations(JsonElement root)
    {
        var result = new List<ShearInclinedReportStation>();
        if (!root.TryGetProperty("stations", out var stations) || stations.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var station in stations.EnumerateArray())
            result.Add(new ShearInclinedReportStation
            {
                Plane = String(station, "plane") ?? "",
                S = Number(station, "s"),
                N = Number(station, "n"),
                PhiN = Number(station, "phiN"),
                TensionOnPositiveSide = Boolean(station, "tensionOnPositiveSide"),
                Q = Number(station, "q"),
                CriticalC = Number(station, "cCrit"),
                Qb = Number(station, "qb"),
                Qsw = Number(station, "qsw"),
                Eta = Number(station, "eta"),
                MomentApplied = Number(station, "mApplied"),
                CriticalCMoment = Number(station, "cCritMoment"),
                Ms = Number(station, "ms"),
                Msw = Number(station, "msw"),
                EtaM = Number(station, "etaM")
            });
        return result;
    }

    static double? Value(IReadOnlyDictionary<string, ShearInclinedReportTraceValue> values,
        string key)
        => values.TryGetValue(key, out var value) ? value.Value : null;

    static string FormulaFor(string formula) => formula switch
    {
        "8.55" => @"Q \le Q_{b,max}",
        "8.56" => @"Q \le Q_b + Q_{sw}",
        "8.60" => @"Q \le Q_{b,min} + Q_{sw,min}",
        "8.63" or "8.63s" => @"M \le M_s + M_{sw}",
        _ => @"A_{Ed} \le A_{Rd}"
    };

    static string UnitFor(JsonElement detail, string formulaProperty, string key)
    {
        string formula = String(detail, formulaProperty) ?? "";
        if (key is "s" or "C" or "d" or "b" or "h0") return "Meter";
        if (key is "Ms" or "Msw" ||
            ((key is "applied" or "allowable") &&
             (formula is "8.63" or "8.63s")))
            return "KilonewtonMeter";
        if (key is "Q" or "Qb" or "Qsw" or "Qb,min" or "Qsw,min" || key is "applied" or "allowable")
            return "Kilonewton";
        if (key == "Rb") return "Kilopascal";
        return "Unitless";
    }

    static List<string> ReadStrings(JsonElement root, string name)
        => root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(item => item.GetString() ?? "").ToList()
            : [];

    static string? String(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static double? Number(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? Number(value) : null;

    static double? Number(JsonElement element)
        => element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value)
            ? value : null;

    static int? Integer(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.TryGetInt32(out var result) ? result : null;

    static bool? Boolean(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? value.GetBoolean() : null;
}

/// <summary>Один шаг наклонной трассировки в Reporting-модели.</summary>
public sealed class ShearInclinedReportTraceStep
{
    /// <summary>Стабильный идентификатор.</summary>
    public string StepId { get; init; } = "";
    /// <summary>Пункт нормы.</summary>
    public string CodeReference { get; init; } = "";
    /// <summary>LaTeX-формула.</summary>
    public string FormulaLatex { get; init; } = "";
    /// <summary>Шаблон подстановки.</summary>
    public string SubstitutionTemplate { get; init; } = "";
    /// <summary>Шаблон результата.</summary>
    public string ResultTemplate { get; init; } = "";
    /// <summary>Статус.</summary>
    public string Status { get; init; } = "informational";
    /// <summary>Плоскость.</summary>
    public string Plane { get; init; } = "";
    /// <summary>Код формулы.</summary>
    public string FormulaCode { get; init; } = "";
    /// <summary>Индекс стоянки.</summary>
    public int? StationIndex { get; init; }
    /// <summary>Координата стоянки.</summary>
    public double? StationS { get; init; }
    /// <summary>Критическая проекция.</summary>
    public double? CriticalC { get; init; }
    /// <summary>Числовые значения.</summary>
    public Dictionary<string, ShearInclinedReportTraceValue> Values { get; init; } = [];
}

/// <summary>Числовое значение шага в Reporting-модели.</summary>
public sealed record ShearInclinedReportTraceValue(double? Value, string? Unit);

/// <summary>Строка приложения со стоянкой.</summary>
public sealed class ShearInclinedReportStation
{
    /// <summary>Плоскость.</summary>
    public string Plane { get; init; } = "";
    /// <summary>Координата стоянки.</summary>
    public double? S { get; init; }
    /// <summary>Продольная сила.</summary>
    public double? N { get; init; }
    /// <summary>φn.</summary>
    public double? PhiN { get; init; }
    /// <summary>Растянута ли положительная грань.</summary>
    public bool? TensionOnPositiveSide { get; init; }
    /// <summary>Поперечная сила.</summary>
    public double? Q { get; init; }
    /// <summary>Критическая проекция по поперечной силе.</summary>
    public double? CriticalC { get; init; }
    /// <summary>Составляющая бетона.</summary>
    public double? Qb { get; init; }
    /// <summary>Составляющая хомутов.</summary>
    public double? Qsw { get; init; }
    /// <summary>η по поперечной силе.</summary>
    public double? Eta { get; init; }
    /// <summary>Момент.</summary>
    public double? MomentApplied { get; init; }
    /// <summary>Критическая проекция по моменту.</summary>
    public double? CriticalCMoment { get; init; }
    /// <summary>Момент продольной арматуры.</summary>
    public double? Ms { get; init; }
    /// <summary>Момент хомутов.</summary>
    public double? Msw { get; init; }
    /// <summary>η по моменту.</summary>
    public double? EtaM { get; init; }
}
