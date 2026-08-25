using System.Text.Json;
using CScore;
using OpenCS.OpenSees.Structural;

namespace OpenCS.ViewModels;

/// <summary>Усилия одного шага расчёта вместе с его признаками.</summary>
/// <param name="Forces">Концевые усилия элементов на этом шаге.</param>
/// <param name="StepIndex">Фактический индекс шага в результате.</param>
/// <param name="StepLabel">Человекочитаемая подпись шага.</param>
/// <param name="Converged">Шаг сошёлся.</param>
public sealed record FemStepForces(
    IReadOnlyList<FemElementEndForces> Forces, int StepIndex, string StepLabel, bool Converged);

/// <summary>Извлекает усилия стержней из линейного или нелинейного результата FEM.</summary>
public static class FemMemberForceResultResolver
{
    /// <summary>Возвращает усилия последнего сошедшегося шага; несошедшийся шаг не используется.</summary>
    public static IReadOnlyList<FemElementEndForces> ResolveElementForces(CalcResult result) =>
        ResolveStep(result, null) is { Converged: true } step ? step.Forces : [];

    /// <summary>
    /// Возвращает усилия выбранного шага: <c>null</c> в <paramref name="stepIndex"/> —
    /// последний сошедшийся. Возвращает <c>null</c>, если такого шага в результате нет;
    /// несошедшийся шаг возвращается с <see cref="FemStepForces.Converged"/> = false,
    /// решение по нему принимает вызывающий.
    /// </summary>
    public static FemStepForces? ResolveStep(CalcResult result, int? stepIndex)
    {
        ArgumentNullException.ThrowIfNull(result);
        try
        {
            using var document = JsonDocument.Parse(result.DataJson);
            var root = document.RootElement;

            if (root.TryGetProperty("Steps", out _))
            {
                var nonlinear = JsonSerializer.Deserialize<FemNonlinearResult>(result.DataJson);
                var steps = nonlinear?.Steps ?? [];
                if (steps.Count == 0) return null;

                var step = stepIndex is int requested
                    ? steps.FirstOrDefault(s => s.StepIndex == requested)
                    : steps.LastOrDefault(s => s.Converged);
                if (step is null) return null;

                return new FemStepForces(
                    step.ElementForces, step.StepIndex,
                    $"λ = {step.LoadFactor:F3}", step.Converged);
            }

            if (root.TryGetProperty("Displacements", out _))
            {
                if (stepIndex is int index && index != 0) return null;
                var linear = JsonSerializer.Deserialize<FemLinearResult>(result.DataJson);
                return linear is null
                    ? null
                    : new FemStepForces(linear.ElementForces, 0, "линейный расчёт", true);
            }
        }
        catch (JsonException)
        {
            // Повреждённый JSON не должен ломать окно эпюр.
        }

        return null;
    }
}
