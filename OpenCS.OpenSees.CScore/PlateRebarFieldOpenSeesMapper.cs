using CScore;
using CScore.PlateRebar;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Строит per-element LayeredShell-секции из PlateRebarField с дедупликацией по
/// уникальным сочетаниям армирования (см. PlateRebarFieldResolver.ResolveMesh).</summary>
public static class PlateRebarFieldOpenSeesMapper
{
    public static PlateRebarFieldShellMappingResult MapMesh(
        PlateSection section,
        PlateRebarField field,
        ShellFrame frame,
        IPlateSectionShellMaterialResolver resolver,
        IReadOnlyList<(int ElementId, double U, double V)> centroids,
        int firstSectionTag = 1)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(centroids);
        if (centroids.Count == 0)
            throw new ArgumentException("Список центроидов не должен быть пустым.", nameof(centroids));
        if (firstSectionTag <= 0)
            throw new CScoreMappingException("firstSectionTag должен быть положительным.");

        IReadOnlyList<ResolvedElementRebar> resolved = PlateRebarFieldResolver.ResolveMesh(field, centroids);

        var groups = resolved
            .GroupBy(r => r.LayoutFingerprint)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var requests = new List<(PlateSection Section, ShellFrame Frame, int SectionTag)>(groups.Count);
        var groupTag = new Dictionary<string, int>(StringComparer.Ordinal);
        int tag = firstSectionTag;
        foreach (var group in groups)
        {
            PlateSection clone = section.CloneForCalc();
            clone.RebarLayers = group.First().Layout.Layers.ToList();
            requests.Add((clone, frame, tag));
            groupTag[group.Key] = tag;
            tag++;
        }

        PlateSectionShellMappingResultBatch batch = PlateSectionOpenSeesMapper.MapMany(requests, resolver);

        var elementSectionTag = resolved.ToDictionary(r => r.ElementId, r => groupTag[r.LayoutFingerprint]);
        var rebarDiagnostics = resolved
            .SelectMany(r => r.Layout.Diagnostics.Select(d => (r.ElementId, d)))
            .ToList();

        return new PlateRebarFieldShellMappingResult(
            batch.Sections, batch.Materials, elementSectionTag, rebarDiagnostics, batch.Diagnostics);
    }
}
