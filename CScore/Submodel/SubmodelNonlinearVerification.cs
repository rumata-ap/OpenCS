using System.Globalization;
using CScore.Fem;

namespace CScore.Submodel;

/// <summary>Состояние одного сошедшегося шага нелинейного расчёта субмодели при коэффициенте <see cref="Lambda"/>.</summary>
public sealed record SubmodelStepState(int StepIndex, double Lambda, IParentLinearResult State);

/// <summary>
/// Невязка фиксации жёстких мод на одном шаге: максимум <c>|R − λ·(p_control − p_boundary)|</c> по
/// поступательным (<see cref="MaxForce"/>, Н) и вращательным (<see cref="MaxMoment"/>, Н·м) gauge-DOF.
/// <c>null</c> — gauge-DOF такого вида нет (или ни один из них не удалось сверить), а не ноль.
/// </summary>
public sealed record GaugeResidualStep(int StepIndex, double Lambda, double? MaxForce, double? MaxMoment);

/// <summary>
/// Отчёт нелинейной сверки субмодели с линейным родителем. <see cref="AtReached"/> — расхождения в
/// последней сошедшейся точке с <c>λ_reached · родитель</c>; его <c>Passed</c> не интерпретируется:
/// расхождение перемещений в нелинейном расчёте ожидаемо и само является результатом.
/// </summary>
public sealed record SubmodelNonlinearVerificationReport(
    double ReachedLambda,
    bool LambdaReached,
    bool MemberLoadsLumped,
    SubmodelVerificationReport AtReached,
    IReadOnlyList<GaugeResidualStep> GaugeHistory,
    GaugeResidualStep? MaxGaugeForce,
    GaugeResidualStep? MaxGaugeMoment,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Коды диагностик нелинейного расчёта и сверки субмодели (срез 4b).</summary>
public static class SubmodelNonlinearDiagnostics
{
    public const string ParamsInvalid = "submodel_nonlinear_params_invalid";
    public const string ResultInvalid = "submodel_nonlinear_result_invalid";
    public const string NoConvergedSteps = "submodel_nonlinear_no_converged_steps";
    public const string LambdaNotReached = "submodel_nonlinear_lambda_not_reached";
    public const string PrecheckFailed = "submodel_nonlinear_precheck_failed";
    public const string MemberLoadsApproximated = "submodel_nonlinear_member_loads_approximated";
    public const string Info = "submodel_nonlinear_info";
}

/// <summary>
/// Сверка нелинейного расчёта материализованной субмодели (путь <c>λ·p, λ·U</c>) с линейным родителем:
/// расхождения в последней сошедшейся точке и история невязки фиксации жёстких мод по шагам. При
/// <c>geomTransf Linear</c> невязка ≈ 0 (равновесие в недеформированной конфигурации); при
/// PDelta/Corotational отличие от нуля — физический эффект, а не ошибка.
/// </summary>
public static class SubmodelNonlinearVerification
{
    const double LambdaTolerance = 1e-9;

    public static SubmodelNonlinearVerificationReport Compare(SubmodelExtraction extraction, BoundaryScenario scenario,
        SubmodelMaterializationSummary summary, IParentLinearResult parent, IReadOnlyList<SubmodelStepState> steps,
        bool memberLoadsLumped, VerificationTolerances? tolerances = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
            throw new ArgumentException("Нет ни одного сошедшегося шага — сверять нечего.", nameof(steps));
        if (steps.Any(s => !double.IsFinite(s.Lambda) || s.Lambda <= 0))
            throw new ArgumentException("Коэффициент λ шага должен быть конечным и положительным.", nameof(steps));

        var diagnostics = new List<FemValidationDiagnostic>();
        void Info(string message) => diagnostics.Add(new(SubmodelNonlinearDiagnostics.Info, message, false, []));

        var last = steps[^1];
        bool reached = Math.Abs(last.Lambda - 1) <= LambdaTolerance;
        if (!reached)
            diagnostics.Add(new(SubmodelNonlinearDiagnostics.LambdaNotReached,
                $"λ = 1 не достигнут: последний сошедшийся λ = {F(last.Lambda)}; сверка выполнена при этом λ.", false, []));
        if (memberLoadsLumped)
            diagnostics.Add(new(SubmodelNonlinearDiagnostics.MemberLoadsApproximated,
                "Нагрузки в пролёте стержней переведены в эквивалентные узловые (geomTransf Corotational): " +
                "концевые усилия и отклик субмодели приближённые.", false, []));

        var atReached = SubmodelLinearVerification.Compare(extraction, scenario, summary, parent, last.State,
            tolerances, lambda: last.Lambda);
        foreach (var d in atReached.Diagnostics)
            diagnostics.Add(d.Code == SubmodelMaterializationDiagnostics.VerificationMismatch
                ? new(SubmodelNonlinearDiagnostics.Info, d.Message, false, d.SourceKeys)
                : d);

        var history = GaugeHistory(scenario, summary, steps, out var skipped);
        if (skipped.Count > 0)
            Info("Невязка фиксации не сверяется в DOF без контрольного остатка или реакции: " + string.Join(", ", skipped) + ".");

        var maxForce = history.Where(h => h.MaxForce is not null).MaxBy(h => h.MaxForce!.Value);
        var maxMoment = history.Where(h => h.MaxMoment is not null).MaxBy(h => h.MaxMoment!.Value);
        if (summary.GaugeDofs.Count > 0)
            Info("Максимальная невязка фиксации: силы — " + Describe(maxForce, maxForce?.MaxForce, "Н") +
                 "; моменты — " + Describe(maxMoment, maxMoment?.MaxMoment, "Н·м") + ".");

        return new SubmodelNonlinearVerificationReport(last.Lambda, reached, memberLoadsLumped, atReached,
            history, maxForce, maxMoment, diagnostics);
    }

    static List<GaugeResidualStep> GaugeHistory(BoundaryScenario scenario, SubmodelMaterializationSummary summary,
        IReadOnlyList<SubmodelStepState> steps, out SortedSet<string> skipped)
    {
        skipped = new SortedSet<string>(StringComparer.Ordinal);
        var history = new List<GaugeResidualStep>();
        if (summary.GaugeDofs.Count == 0) return history;

        var endByTag = scenario.Ends.ToDictionary(e => e.ChildNodeTag, StringComparer.Ordinal);
        foreach (var step in steps)
        {
            double? maxForce = null, maxMoment = null;
            foreach (var gauge in summary.GaugeDofs)
            {
                var end = endByTag[gauge.ChildNodeTag];
                if (end.Control.PControl is not { } control || !step.State.TryGetReaction(gauge.ChildNodeTag, out var reaction))
                {
                    skipped.Add($"{gauge.ChildNodeTag} DOF {gauge.Dof + 1}");
                    continue;
                }
                double expected = step.Lambda * (control[gauge.Dof] - (end.Dofs[gauge.Dof].Value ?? 0));
                double residual = Math.Abs(reaction[gauge.Dof] - expected);
                if (gauge.Dof < 3) maxForce = Math.Max(maxForce ?? 0, residual);
                else maxMoment = Math.Max(maxMoment ?? 0, residual);
            }
            history.Add(new GaugeResidualStep(step.StepIndex, step.Lambda, maxForce, maxMoment));
        }
        return history;
    }

    static string Describe(GaugeResidualStep? step, double? value, string unit) =>
        step is null ? "нет" : $"{value!.Value:G6} {unit} (шаг {step.StepIndex}, λ = {F(step.Lambda)})";

    static string F(double value) => value.ToString("G6", CultureInfo.InvariantCulture);
}
