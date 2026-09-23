using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Submodel;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Итог адаптации нелинейного результата: состояния шагов либо блокирующая диагностика.</summary>
public sealed record NonlinearStatesAdapterOutcome(
    IReadOnlyList<SubmodelStepState>? Steps, bool MemberLoadsLumped, FemValidationDiagnostic? Error);

/// <summary>
/// Превращает сохранённый нелинейный результат OpenSees (<c>TaskKind = "fem_nonlinear"</c>) в состояния
/// сошедшихся шагов для сверки субмодели. Статусы <c>ok</c>/<c>not_converged</c>/<c>partial</c>
/// допускаются, если есть хотя бы один сошедшийся шаг; <c>error</c>, <c>cancelled</c> и любые
/// незнакомые — отказ. Сверяется стадия с максимальным <c>StageIndex</c> среди сошедшихся шагов
/// (постановка субмодели одностадийная); шаги уточнения включаются — это реальные точки пути.
/// </summary>
public static class FemNonlinearResultParentAdapter
{
    public const string NonlinearTaskKind = "fem_nonlinear";
    static readonly HashSet<string> AcceptedStatuses = new(StringComparer.Ordinal) { "ok", "not_converged", "partial" };

    public static NonlinearStatesAdapterOutcome FromCalcResult(CalcResult calcResult)
    {
        ArgumentNullException.ThrowIfNull(calcResult);
        if (calcResult.TaskKind != NonlinearTaskKind)
            return Fail(SubmodelNonlinearDiagnostics.ResultInvalid,
                $"Результат #{calcResult.Id} вида '{calcResult.TaskKind}' не является нелинейным расчётом OpenSees.");

        // Статус записи результата проверяется до разбора: ошибка резолва пишет в DataJson не
        // FemNonlinearResult, а {error, errors}.
        if (!AcceptedStatuses.Contains(calcResult.Status))
            return Fail(SubmodelNonlinearDiagnostics.ResultInvalid, StatusMessage(calcResult.Status, ErrorTexts(calcResult.DataJson)));

        FemNonlinearResult? parsed;
        try { parsed = JsonSerializer.Deserialize<FemNonlinearResult>(calcResult.DataJson); }
        catch (JsonException ex)
        {
            return Fail(SubmodelNonlinearDiagnostics.ResultInvalid, $"Нелинейный результат #{calcResult.Id} повреждён: {ex.Message}");
        }
        if (parsed is null)
            return Fail(SubmodelNonlinearDiagnostics.ResultInvalid, $"Нелинейный результат #{calcResult.Id} пуст.");
        return FromNonlinearResult(parsed);
    }

    /// <summary>Тексты ошибок из DataJson: массив <c>errors</c> (ошибка резолва) или <c>Diagnostics</c> (результат расчёта).</summary>
    static IReadOnlyList<string> ErrorTexts(string dataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            foreach (var name in new[] { "errors", nameof(FemNonlinearResult.Diagnostics) })
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var array)
                    && array.ValueKind == JsonValueKind.Array)
                    return array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
        }
        catch (JsonException) { }
        return [];
    }

    public static NonlinearStatesAdapterOutcome FromNonlinearResult(FemNonlinearResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!AcceptedStatuses.Contains(result.Status))
            return Fail(SubmodelNonlinearDiagnostics.ResultInvalid, StatusMessage(result.Status, result.Diagnostics));

        var converged = result.Steps.Where(s => s.Converged).ToList();
        if (converged.Count == 0)
            return Fail(SubmodelNonlinearDiagnostics.NoConvergedSteps,
                "Нелинейный расчёт не дал ни одного сошедшегося шага — сверять нечего.");
        if (converged.Any(s => s.StageIndex < 0))
            return Fail(SubmodelNonlinearDiagnostics.ResultInvalid, "Структурный конфликт стадий: отрицательный индекс стадии шага.");
        int stage = converged.Max(s => s.StageIndex);
        if (result.StageTags.Count > 0 && stage >= result.StageTags.Count)
            return Fail(SubmodelNonlinearDiagnostics.ResultInvalid,
                $"Структурный конфликт стадий: шаг стадии {stage}, а стадий в результате {result.StageTags.Count}.");

        var selected = converged.Where(s => s.StageIndex == stage).OrderBy(s => s.StepIndex).ToList();
        if (selected.Any(s => !double.IsFinite(s.LoadFactor) || s.LoadFactor <= 0))
            return Fail(SubmodelNonlinearDiagnostics.ResultInvalid, "Шаг с неположительным или нечисловым коэффициентом λ.");

        var states = selected.Select(s => new SubmodelStepState(s.StepIndex, s.LoadFactor,
            FemLinearResultParentAdapter.Build(s.Displacements, s.Reactions, s.ElementForces, isLinear: false))).ToList();
        return new NonlinearStatesAdapterOutcome(states, result.MemberLoadsLumped, null);
    }

    static string StatusMessage(string status, IReadOnlyList<string> errors)
    {
        string details = errors.Count > 0 ? ": " + string.Join(" | ", errors.Take(3)) : "";
        return $"Нелинейный расчёт завершился со статусом '{status}' — сверка невозможна{details}";
    }

    static NonlinearStatesAdapterOutcome Fail(string code, string message) =>
        new(null, false, new FemValidationDiagnostic(code, message, true, []));
}
