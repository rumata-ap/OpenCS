using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Результат сборки ShellOpenSeesModel из PlanarMeshSnapshot: модель + явный provenance
/// (snapshot-индекс узла/элемента → OpenSees tag) + диагностики маппинга. Stages пустые —
/// нагрузки/граничные условия вне объёма адаптера, добавляются вызывающим.</summary>
public sealed record PlanarMeshShellModelResult(
    ShellOpenSeesModel Model,
    IReadOnlyDictionary<int, int> NodeIndexToTag,
    IReadOnlyDictionary<int, int> ElementIndexToTag,
    IReadOnlyList<(int ElementId, FemValidationDiagnostic Diagnostic)> RebarDiagnostics,
    IReadOnlyList<string> MappingDiagnostics);

/// <summary>Геометрический адаптер PlanarMeshSnapshot (срез 1 Gmsh-сетки) → ShellOpenSeesModel,
/// поверх уже готового моста PlateRebarField → OpenSees (PlateRebarFieldOpenSeesMapper).</summary>
public static class PlanarMeshSnapshotShellModelAdapter
{
    public static PlanarMeshShellModelResult Build(
        PlanarMeshSnapshot snapshot,
        Frame3D frame,
        PlateSection section,
        PlateRebarField field,
        IPlateSectionShellMaterialResolver resolver,
        int firstSectionTag = 1)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ShellFrame shellFrame = frame.ToShellFrame();

        NormalizedShellNode[] nodes = snapshot.Nodes
            .Select(n => new NormalizedShellNode(
                n.Index + 1, n.X, n.Y, n.Z, new bool[6], $"mesh-node:{n.Index}"))
            .ToArray();

        var centroids = snapshot.Elements
            .Select(e => (e.Index, Centroid: e.Centroid(snapshot.Nodes)))
            .Select(t => (t.Index, t.Centroid.U, t.Centroid.V))
            .ToList();

        PlateRebarFieldShellMappingResult mapped = PlateRebarFieldOpenSeesMapper.MapMesh(
            section, field, shellFrame, resolver, centroids, firstSectionTag);
        var sectionByTag = mapped.Sections.ToDictionary(s => s.Tag);

        NormalizedShellElement[] elements = snapshot.Elements
            .Select(e =>
            {
                int sectionTag = mapped.ElementSectionTag[e.Index];
                return new NormalizedShellElement(
                    e.Index + 1,
                    e.Kind == PlanarMeshElementKind.Triangle3
                        ? ShellElementKind.ASDShellT3 : ShellElementKind.ASDShellQ4,
                    e.NodeIndices.Select(i => i + 1).ToList(),
                    sectionTag,
                    sectionByTag[sectionTag].Fingerprint,
                    shellFrame,
                    ShellIntegrationPolicy.Full,
                    $"mesh-element:{e.Index}");
            })
            .ToArray();

        var model = new ShellOpenSeesModel
        {
            Nodes = nodes,
            Materials = mapped.Materials,
            Sections = mapped.Sections,
            Elements = elements,
        };

        var nodeProvenance = snapshot.Nodes.ToDictionary(n => n.Index, n => n.Index + 1);
        var elementProvenance = snapshot.Elements.ToDictionary(e => e.Index, e => e.Index + 1);

        return new PlanarMeshShellModelResult(
            model, nodeProvenance, elementProvenance,
            mapped.RebarDiagnostics, mapped.MappingDiagnostics);
    }
}
