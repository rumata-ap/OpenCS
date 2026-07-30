using CScore.Fem;

namespace CScore.PlateRebar;

/// <summary>Результат разрешения PlateRebarField для одного центроида КЭ: эффективный список
/// слоёв армирования по обеим граням + диагностика конфликтов приоритетов.</summary>
public sealed record ResolvedRebarLayout(IReadOnlyList<PlateRebarLayer> Layers, IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Чистая функция разрешения пространственного поля армирования в эффективный
/// список слоёв для конкретного центроида КЭ (u, v) в локальной плоскости поверхности.
/// Не знает о сетке/КЭ — только о геометрии зон и координатах, переданных вызывающей стороной.</summary>
public static class PlateRebarFieldResolver
{
    public static ResolvedRebarLayout Resolve(PlateRebarField field, double centroidU, double centroidV)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        var layers = new List<PlateRebarLayer>();

        foreach (var face in new[] { RebarFace.PlusN, RebarFace.MinusN })
        {
            var accumulated = field.BaseLayout.Where(l => l.Face == face).ToList();

            var candidateGroups = field.Zones
                .Where(z => z.Face == face && IsCentroidInside(z, centroidU, centroidV))
                .GroupBy(z => z.Priority)
                .OrderBy(g => g.Key);

            foreach (var group in candidateGroups)
            {
                var zonesAtPriority = group.ToList();
                if (zonesAtPriority.Count > 1)
                {
                    diagnostics.Add(new FemValidationDiagnostic(
                        "plate_rebar_zone_priority_conflict",
                        $"На грани {face} несколько зон с одинаковым приоритетом {group.Key} применимы к точке ({centroidU}, {centroidV}) — результат неоднозначен, применение этой группы приоритета пропущено."));
                    continue;
                }

                var zone = zonesAtPriority[0];
                accumulated = zone.Operation == RebarZoneOperation.Replace
                    ? [zone.Layout]
                    : [.. accumulated, zone.Layout];
            }

            layers.AddRange(accumulated);
        }

        return new ResolvedRebarLayout(layers, diagnostics);
    }

    static bool IsCentroidInside(RebarZone zone, double u, double v) =>
        CSTriangulation.GeometryUtils.PointInPolygon(u, v, zone.Polygon.Select(p => (p.U, p.V)).ToList());
}
