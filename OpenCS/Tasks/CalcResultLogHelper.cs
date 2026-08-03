using System.Text.Json;
using CScore;
using OpenCS.Services;

namespace OpenCS.Tasks;

/// <summary>Определяет уровень и извлекает подробность для записи результата задачи в журнал.</summary>
public static class CalcResultLogHelper
{
    /// <summary>Преобразует статус результата в уровень журнала.</summary>
    public static LogLevel ResolveLevel(CalcResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Status switch
        {
            "error" => LogLevel.Error,
            "not_converged" or "partial" => LogLevel.Warning,
            _ => LogLevel.Info
        };
    }

    /// <summary>Извлекает поля error, message, errors или diagnostics из DataJson.</summary>
    public static string ExtractDetail(CalcResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(result.DataJson))
            return "";

        try
        {
            using JsonDocument document = JsonDocument.Parse(result.DataJson);
            JsonElement root = document.RootElement;
            foreach (string propertyName in new[] { "error", "message" })
            {
                if (root.TryGetProperty(propertyName, out JsonElement value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!;
                }
            }

            foreach (string propertyName in new[] { "errors", "diagnostics" })
            {
                if (!root.TryGetProperty(propertyName, out JsonElement values) ||
                    values.ValueKind != JsonValueKind.Array)
                    continue;

                string detail = string.Join(
                    " | ",
                    values.EnumerateArray()
                        .Where(value => value.ValueKind == JsonValueKind.String)
                        .Select(value => value.GetString())
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(detail))
                    return detail;
            }
        }
        catch (JsonException)
        {
            // Повреждённый DataJson не должен скрывать исходный статус результата.
        }

        return "";
    }
}
