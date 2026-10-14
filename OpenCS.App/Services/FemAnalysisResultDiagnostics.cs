using System.Text.Json;
using OpenCS.OpenSees.Structural;

namespace OpenCS.Services;

/// <summary>Диагностика и каталог артефактов OpenSees-результата постановки (линейного или нелинейного) из DataJson.</summary>
public static class FemAnalysisResultDiagnostics
{
    /// <summary>Диагностика прогона и каталог артефактов; для неразборного DataJson — только поля error/errors.</summary>
    public static (IReadOnlyList<string> Diagnostics, string? ArtifactDirectory) Read(string dataJson)
    {
        if (TryDeserialize<FemNonlinearResult>(dataJson, "Steps") is { } nonlinear)
            return (Collect(dataJson, nonlinear.Diagnostics), nonlinear.ArtifactDirectory);
        var linear = TryDeserialize<FemLinearResult>(dataJson, "Displacements");
        return (Collect(dataJson, linear?.Diagnostics ?? []), linear?.ArtifactDirectory);
    }

    /// <summary>Разобранная диагностика прогона плюс ошибки верхнего уровня DataJson (errors, иначе error).</summary>
    public static IReadOnlyList<string> Collect(string dataJson, IReadOnlyList<string> parsedDiagnostics)
    {
        var list = new List<string>();
        if (parsedDiagnostics.Count > 0) list.AddRange(parsedDiagnostics);
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in errors.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString()!);
            }
            else if (list.Count == 0 && doc.RootElement.TryGetProperty("error", out var err)
                     && err.ValueKind == JsonValueKind.String)
                list.Add(err.GetString()!);
        }
        catch (JsonException) { }
        return list;
    }

    static T? TryDeserialize<T>(string dataJson, string marker) where T : class
    {
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (!doc.RootElement.TryGetProperty(marker, out _)) return null;
            return JsonSerializer.Deserialize<T>(dataJson);
        }
        catch (JsonException) { return null; }
    }
}
