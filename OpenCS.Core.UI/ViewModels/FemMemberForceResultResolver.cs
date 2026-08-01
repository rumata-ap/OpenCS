using System.Text.Json;
using CScore;
using OpenCS.OpenSees.Structural;

namespace OpenCS.ViewModels;

/// <summary>Извлекает усилия стержней из линейного или нелинейного результата FEM.</summary>
public static class FemMemberForceResultResolver
{
    /// <summary>Возвращает усилия последнего сошедшегося шага; несошедшийся шаг не используется.</summary>
    public static IReadOnlyList<FemElementEndForces> ResolveElementForces(CalcResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        try
        {
            using var document = JsonDocument.Parse(result.DataJson);
            var root = document.RootElement;
            if (root.TryGetProperty("Steps", out _))
            {
                var nonlinear = JsonSerializer.Deserialize<FemNonlinearResult>(result.DataJson);
                return nonlinear?.Steps
                    .LastOrDefault(step => step.Converged)
                    ?.ElementForces ?? [];
            }

            if (root.TryGetProperty("Displacements", out _))
            {
                var linear = JsonSerializer.Deserialize<FemLinearResult>(result.DataJson);
                return linear?.ElementForces ?? [];
            }
        }
        catch (JsonException)
        {
            // Повреждённый JSON не должен ломать окно эпюр.
        }

        return [];
    }
}
