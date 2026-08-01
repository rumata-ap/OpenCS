using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCS.Tasks;

/// <summary>Сериализация результата кручения без NaN/Infinity (System.Text.Json их не пишет).</summary>
internal static class TorsionJsonHelper
{
    static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static string Serialize(object data) => JsonSerializer.Serialize(data, Options);

    internal static double? Finite(double v) => double.IsFinite(v) ? v : null;

    internal static double[]? FiniteArray(double[]? a)
    {
        if (a == null) return null;
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
            r[i] = double.IsFinite(a[i]) ? a[i] : 0.0;
        return r;
    }
}
