using System.Text.Json;

namespace OpenCS.Tasks;

/// <summary>Преобразует старые параметры максимальной кривизны в шаговый формат.</summary>
internal static class OpenSeesCurvatureParameterMigration
{
    /// <summary>Читает новые параметры и поддерживает сохранённые JSON старого формата.</summary>
    public static (double CurvatureStep, int MaxSteps) Resolve(
        string? json,
        double curvatureStep,
        int maxSteps)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return (curvatureStep, maxSteps);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        bool hasNewStep = TryGet(root, "curvatureStep", out _);
        bool hasNewMaxSteps = TryGet(root, "maxSteps", out _);
        bool hasOldMaxCurvature = TryGet(root, "maxCurvature", out JsonElement oldMaxElement);
        bool hasOldIncrements = TryGet(root, "increments", out JsonElement oldIncrementsElement);

        if (!hasNewStep && hasOldMaxCurvature && oldMaxElement.TryGetDouble(out double oldMaxCurvature))
        {
            int divisor = oldIncrementsElement.TryGetInt32(out int oldIncrements) && oldIncrements > 0
                ? oldIncrements
                : maxSteps;
            curvatureStep = oldMaxCurvature / divisor;
        }

        if (!hasNewMaxSteps && hasOldIncrements &&
            oldIncrementsElement.TryGetInt32(out int legacyMaxSteps) && legacyMaxSteps <= 0)
            maxSteps = legacyMaxSteps;

        return (curvatureStep, maxSteps);
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
