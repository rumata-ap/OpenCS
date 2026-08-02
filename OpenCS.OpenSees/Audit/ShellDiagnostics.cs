namespace OpenCS.OpenSees.Audit;

/// <summary>Severity структурированной диагностики shell audit.</summary>
public enum ShellDiagnosticSeverity
{
    /// <summary>Информация без влияния на verdict.</summary>
    Info,

    /// <summary>Предупреждение: результат usable с ограничениями.</summary>
    Warning,

    /// <summary>Блокирующая диагностика: verdict должен быть Blocked.</summary>
    Blocking
}

/// <summary>Структурированная диагностика с кодом, severity и контекстом.</summary>
public sealed record ShellDiagnostic(
    string Code,
    ShellDiagnosticSeverity Severity,
    string Message,
    int? ElementTag = null,
    int? IntegrationPoint = null,
    int? LayerIndex = null,
    string? ArtifactDirectory = null,
    string? SourceFingerprint = null);

/// <summary>Стабильные коды blocking/warning диагностик shell audit.</summary>
public static class ShellDiagnosticCodes
{
    /// <summary>Угол арматуры не является конечным числом.</summary>
    public const string RebarAngleInvalid = "rebar_angle_invalid";

    /// <summary>Material-state catalog не содержит provenance.</summary>
    public const string StateCatalogProvenanceMissing = "state_catalog_provenance_missing";

    /// <summary>Native response не поддерживается материалом.</summary>
    public const string UnsupportedShellResponse = "unsupported_shell_response";

    /// <summary>Выбор recorder позиции не существует в topology.</summary>
    public const string RecordingSelectionInvalid = "recording_selection_invalid";

    /// <summary>Native tangent material недоступен.</summary>
    public const string MaterialTangentUnavailable = "material_tangent_unavailable";

    /// <summary>Запрошенная regularization не применена.</summary>
    public const string RegularizationUnsupported = "regularization_unsupported";

    /// <summary>Требуемая energy response недоступна.</summary>
    public const string EnergyUnavailable = "energy_unavailable";

    /// <summary>Равновесие не удовлетворяет допускам.</summary>
    public const string EquilibriumNotSatisfied = "equilibrium_not_satisfied";

    /// <summary>Результат зависит от mesh.</summary>
    public const string MeshDependent = "mesh_dependent";

    /// <summary>Выходные файлы расчёта неполны.</summary>
    public const string ResultOutputIncomplete = "result_output_incomplete";

    /// <summary>Sensitivity case завершён неполно.</summary>
    public const string SensitivityCaseIncomplete = "sensitivity_case_incomplete";
}
