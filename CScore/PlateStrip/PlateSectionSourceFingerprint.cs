using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.PlateStrip;

/// <summary>Общее построение отпечатка источников, основанных на PlateSection. Вынесено из
/// PlateSectionTangentSnapshot в Срезе 7, когда появился второй такой источник
/// (PlateSectionLiveResponse): вторая реализация SHA не заводится, чтобы замороженный и
/// живой источники не разошлись в том, что вообще считается изменением входа.</summary>
internal static class PlateSectionSourceFingerprint
{
    /// <summary>Части отпечатка, общие для любого источника на базе PlateSection: сама секция,
    /// её слои, материалы и диаграммы. Касательная сюда не входит — у живого источника она не
    /// константа.</summary>
    internal static List<string> BaseParts(
        PlateSection section, Diagramm concrete, Diagramm rebar,
        IReadOnlyList<Diagramm?>? layerDiagrams)
    {
        var parts = new List<string>
        {
            $"section:{section.Id}:{section.Tag}:{section.H.ToString("G17", CultureInfo.InvariantCulture)}",
            $"layers:{section.NLayers}:{section.TensionConcrete}:{section.SofteningModel}:{section.PlateModel}",
            $"materials:{section.ConcreteMaterialId}:{section.RebarMaterialId}",
            $"diagrams:{concrete.Id}:{rebar.Id}"
        };
        if (layerDiagrams != null)
            parts.Add("layer-diagrams:" + string.Join(",", layerDiagrams.Select(d => d?.Id ?? 0)));
        return parts;
    }

    /// <summary>Добавляет матрицу поэлементно, в инвариантном формате без потери разрядов.</summary>
    internal static void AddMatrix(List<string> parts, double[,] matrix, string name)
    {
        for (int i = 0; i < matrix.GetLength(0); i++)
        for (int j = 0; j < matrix.GetLength(1); j++)
            parts.Add($"{name}:{i}:{j}:{matrix[i, j].ToString("G17", CultureInfo.InvariantCulture)}");
    }

    /// <summary>Детерминированный хеш собранных частей.</summary>
    internal static string Hash(IEnumerable<string> parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))));
}
