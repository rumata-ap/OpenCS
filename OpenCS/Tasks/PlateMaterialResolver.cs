using System;
using System.Collections.Generic;
using System.Linq;
using CScore;

namespace OpenCS.Tasks;

/// <summary>
/// Резолвинг материалов плитного сечения в диаграммы для заданного CalcType.
/// Бетон — SP63, арматура — L2 (как в CScoreBridge.SectionBridgeFactory);
/// для арматуры с условным пределом текучести L2 не существует — берётся L3.
/// </summary>
public static class PlateMaterialResolver
{
    public static (Diagramm cDiag, Diagramm rDiag, Diagramm?[] layerDiags, double concreteE)
        Resolve(PlateSection section, IEnumerable<Material> materials, CalcType calc)
    {
        var matList = materials as IList<Material> ?? materials.ToList();

        var concrete = matList.FirstOrDefault(m => m.Id == section.ConcreteMaterialId)
            ?? throw new InvalidOperationException($"Материал бетона id={section.ConcreteMaterialId} не найден.");
        var rebar = matList.FirstOrDefault(m => m.Id == section.RebarMaterialId)
            ?? throw new InvalidOperationException($"Материал арматуры id={section.RebarMaterialId} не найден.");

        var cDiag = concrete.GetDiagramms(section.ConcreteDiagramType)?[calc]
            ?? throw new InvalidOperationException("Диаграмма бетона не построена.");
        var rDiag = rebar.GetDiagramms(DiagrammCompatibility.Coerce(rebar.Type, DiagrammType.L2))?[calc]
            ?? throw new InvalidOperationException("Диаграмма арматуры не построена.");

        var layerDiags = new Diagramm?[section.RebarLayers.Count];
        for (int i = 0; i < section.RebarLayers.Count; i++)
        {
            var layer = section.RebarLayers[i];
            if (layer.MaterialId > 0)
            {
                var lm = matList.FirstOrDefault(m => m.Id == layer.MaterialId);
                if (lm != null)
                    layerDiags[i] = lm.GetDiagramms(
                        DiagrammCompatibility.Coerce(lm.Type, DiagrammType.L2))?[calc];
            }
        }
        return (cDiag, rDiag, layerDiags, concrete.E);
    }
}
