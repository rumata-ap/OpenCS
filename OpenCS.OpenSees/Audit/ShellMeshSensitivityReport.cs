namespace OpenCS.OpenSees.Audit;

/// <summary>Отчёт одного sensitivity-запуска: метрика и исход расчёта.</summary>
public sealed record ShellSensitivityCaseReport(
    ShellSensitivityLevel Level,
    double Metric,
    string SourceFingerprint,
    ShellAnalysisOutcome Outcome);

/// <summary>Итоговый отчёт mesh sensitivity с метриками, verdict и diagnostics.</summary>
public sealed record ShellMeshSensitivityReport(
    IReadOnlyList<ShellSensitivityCaseReport> Cases,
    double MaxRelativeDeviation,
    ShellAuditVerdict Verdict,
    IReadOnlyList<ShellDiagnostic> Diagnostics);
