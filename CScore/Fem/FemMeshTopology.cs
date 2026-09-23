using System.Globalization;
using System.Text.Json;

namespace CScore.Fem;

/// <summary>Чтение связности mesh-элементов в канонических строковых тегах узлов.</summary>
public static class FemMeshTopology
{
    /// <summary>
    /// Возвращает теги узлов элемента из <see cref="FemElement.NodeIdsJson"/> (там хранятся целые теги).
    /// Null — если JSON повреждён либо число узлов не равно <paramref name="expectedCount"/> (когда задано).
    /// </summary>
    public static IReadOnlyList<string>? ReadNodeTags(FemElement element, int? expectedCount = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        int[]? ids;
        try { ids = JsonSerializer.Deserialize<int[]>(element.NodeIdsJson); }
        catch (JsonException) { return null; }
        if (ids is null) return null;
        if (expectedCount is int count && ids.Length != count) return null;
        return ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
    }

    /// <summary>Канонический строковый вид целочисленного тега mesh-узла ("01" → "1"); null — не число.</summary>
    public static string? CanonicalNodeTag(string? tag) =>
        int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : null;
}
