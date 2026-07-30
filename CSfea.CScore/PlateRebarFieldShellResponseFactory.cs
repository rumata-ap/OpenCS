using CScore;
using CScore.Fem;
using CScore.PlateRebar;
using CSfea.Core;

namespace CSfea.CScoreBridge;

/// <summary>Результат разрешения PlateRebarField для сетки элементов CSfea: уникальные
/// отклики-сечения + индекс элемент→отклик + диагностика резолвера (с ElementId).</summary>
public sealed record PlateRebarFieldShellResponseSet(
    IReadOnlyList<IShellSectionResponse> UniqueResponses,
    IReadOnlyDictionary<int, int> ElementResponseIndex,
    IReadOnlyList<(int ElementId, FemValidationDiagnostic Diagnostic)> Diagnostics)
{
    /// <summary>Разворачивает уникальные отклики в массив по порядку элементов — готов для
    /// <c>new ShellMesh(nodes, elements, responsesPerElement)</c>.</summary>
    public IShellSectionResponse[] ToPerElementArray(IReadOnlyList<int> elementOrder)
    {
        var result = new IShellSectionResponse[elementOrder.Count];
        for (int i = 0; i < elementOrder.Count; i++)
            result[i] = UniqueResponses[ElementResponseIndex[elementOrder[i]]];
        return result;
    }
}

/// <summary>Строит per-element <see cref="IShellSectionResponse"/> из PlateRebarField с
/// дедупликацией по уникальным сочетаниям армирования (см. PlateRebarFieldResolver.ResolveMesh).</summary>
public static class PlateRebarFieldShellResponseFactory
{
    public static PlateRebarFieldShellResponseSet MapMesh(
        PlateSection section,
        PlateRebarField field,
        PlateSectionMaterials materials,
        IReadOnlyList<(int ElementId, double U, double V)> centroids)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(centroids);
        if (centroids.Count == 0)
            throw new ArgumentException("Список центроидов не должен быть пустым.", nameof(centroids));

        IReadOnlyList<ResolvedElementRebar> resolved = PlateRebarFieldResolver.ResolveMesh(field, centroids);

        var groups = resolved
            .GroupBy(r => r.LayoutFingerprint)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var responses = new List<IShellSectionResponse>(groups.Count);
        var groupIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            PlateSection clone = section.CloneForCalc();
            clone.RebarLayers = group.First().Layout.Layers.ToList();
            groupIndex[group.Key] = responses.Count;
            responses.Add(new PlateSectionShellResponse(clone, materials));
        }

        var elementResponseIndex = resolved.ToDictionary(r => r.ElementId, r => groupIndex[r.LayoutFingerprint]);
        var diagnostics = resolved
            .SelectMany(r => r.Layout.Diagnostics.Select(d => (r.ElementId, d)))
            .ToList();

        return new PlateRebarFieldShellResponseSet(responses, elementResponseIndex, diagnostics);
    }
}
