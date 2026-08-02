using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Запускает sensitivity cases через IShellAnalysisRunner и оценивает verdict
/// по относительному отклонению метрик.</summary>
public sealed class ShellSensitivityRunner
{
    private readonly IShellSensitivityCaseFactory _factory;
    private readonly IShellAnalysisRunner _analysisRunner;

    /// <summary>Создаёт sensitivity runner поверх фабрики и analysis runner.</summary>
    public ShellSensitivityRunner(IShellSensitivityCaseFactory factory, IShellAnalysisRunner analysisRunner)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _analysisRunner = analysisRunner ?? throw new ArgumentNullException(nameof(analysisRunner));
    }

    /// <summary>Извлекает максимальную по модулю компоненту nodal reactions.</summary>
    public static double MetricFor(ShellResult? result)
    {
        if (result is null)
            return 0;

        double max = 0;
        foreach (ShellNodeReaction reaction in result.Reactions)
        {
            max = Math.Max(max, Math.Abs(reaction.Fx));
            max = Math.Max(max, Math.Abs(reaction.Fy));
            max = Math.Max(max, Math.Abs(reaction.Fz));
            max = Math.Max(max, Math.Abs(reaction.Mx));
            max = Math.Max(max, Math.Abs(reaction.My));
            max = Math.Max(max, Math.Abs(reaction.Mz));
        }
        return max;
    }

    /// <summary>Оценивает verdict по полноте cases, fingerprints и относительному отклонению.</summary>
    public static ShellMeshSensitivityReport Evaluate(
        IReadOnlyList<ShellSensitivityCaseReport> cases,
        double relativeTolerance)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var diagnostics = new List<ShellDiagnostic>();
        ShellAuditVerdict verdict = ShellAuditVerdict.Passed;

        if (cases.Count < 2)
        {
            verdict = ShellAuditVerdict.Blocked;
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.SensitivityCaseIncomplete, ShellDiagnosticSeverity.Blocking,
                $"Sensitivity требует минимум 2 запуска, получено {cases.Count}."));
        }

        if (cases.Select(sensitivityCase => sensitivityCase.SourceFingerprint).Distinct().Count() != cases.Count)
        {
            verdict = ShellAuditVerdict.Blocked;
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.SensitivityCaseIncomplete, ShellDiagnosticSeverity.Blocking,
                "Sensitivity-запуски имеют одинаковые source fingerprints — сравнение разных сеток невозможно."));
        }

        ShellSensitivityCaseReport? failed = cases.FirstOrDefault(
            sensitivityCase => sensitivityCase.Outcome != ShellAnalysisOutcome.Completed);
        if (failed is not null)
        {
            verdict = ShellAuditVerdict.Blocked;
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.SensitivityCaseIncomplete, ShellDiagnosticSeverity.Blocking,
                $"Sensitivity-запуск {failed.Level} завершился с {failed.Outcome}."));
        }

        double maxDeviation = 0;
        for (int i = 0; i < cases.Count; i++)
        {
            for (int j = i + 1; j < cases.Count; j++)
            {
                double denominator = Math.Max(
                    Math.Max(Math.Abs(cases[i].Metric), Math.Abs(cases[j].Metric)), 1e-12);
                maxDeviation = Math.Max(maxDeviation,
                    Math.Abs(cases[i].Metric - cases[j].Metric) / denominator);
            }
        }

        if (verdict == ShellAuditVerdict.Passed && maxDeviation > relativeTolerance)
        {
            verdict = ShellAuditVerdict.MeshDependent;
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.MeshDependent, ShellDiagnosticSeverity.Warning,
                $"Относительное отклонение sensitivity-метрик {maxDeviation:G3} превышает допуск {relativeTolerance:G3}."));
        }

        return new ShellMeshSensitivityReport(cases, maxDeviation, verdict, diagnostics);
    }

    /// <summary>Создаёт и последовательно выполняет sensitivity cases, затем строит отчёт.</summary>
    public async Task<ShellMeshSensitivityReport> RunAsync(
        ShellAuditPolicy policy,
        string executablePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        IReadOnlyList<ShellSensitivityCase> shellCases = _factory.Create(policy.SensitivityLevels);
        var reports = new List<ShellSensitivityCaseReport>(shellCases.Count);
        foreach (ShellSensitivityCase shellCase in shellCases)
        {
            ShellAnalysisRunResult run = await _analysisRunner.RunAsync(
                shellCase.Model, executablePath, cancellationToken);
            reports.Add(new ShellSensitivityCaseReport(
                shellCase.Level, MetricFor(run.Result), shellCase.SourceFingerprint, run.Outcome));
        }
        return Evaluate(reports, policy.SensitivityRelativeTolerance);
    }
}
