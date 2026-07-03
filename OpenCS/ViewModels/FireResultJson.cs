using System.Globalization;
using System.Text.Json;

namespace OpenCS.ViewModels;

/// <summary>Разбор JSON результатов огневых задач.</summary>
internal static class FireResultJson
{
    public static bool TryGetError(string? dataJson, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(dataJson))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
            {
                error = e.GetString() ?? "";
                return !string.IsNullOrEmpty(error);
            }
        }
        catch { /* ignore */ }
        return false;
    }

    public static JsonElement Root(string dataJson)
    {
        using var doc = JsonDocument.Parse(dataJson);
        return doc.RootElement.Clone();
    }

    public static JsonElement Details(JsonElement root)
        => root.TryGetProperty("details", out var d) ? d : root;

    public static string Str(JsonElement el, string name, string fallback = "—")
    {
        if (!el.TryGetProperty(name, out var v))
            return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? fallback,
            JsonValueKind.Number => v.GetDouble().ToString("G6", CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => fallback,
            _ => v.ToString()
        };
    }

    public static double Dbl(JsonElement el, string name, double fallback = 0.0)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number)
            return fallback;
        return v.GetDouble();
    }

    public static bool Bool(JsonElement el, string name, bool fallback = false)
    {
        if (!el.TryGetProperty(name, out var v))
            return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => Math.Abs(v.GetDouble()) > 1e-12,
            _ => fallback
        };
    }

    public static string Fmt(double value, int decimals = 3)
        => value.ToString($"F{decimals}", CultureInfo.InvariantCulture);

    public static string FmtEps(double value)
        => value.ToString("+0.000000;-0.000000", CultureInfo.InvariantCulture);
}
