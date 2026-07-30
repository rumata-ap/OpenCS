using CScore.Planar;

namespace CScore.PlateRebar;

/// <summary>Пространственное поле армирования оболочечного сечения: базовый равномерный
/// layout (источник истины — PlateSection.RebarLayers) + локальные полигональные зоны
/// (источник истины — PlanarRegion.RebarZones). Не персистентный объект сам по себе —
/// конструируется по требованию.</summary>
public sealed record PlateRebarField(IReadOnlyList<PlateRebarLayer> BaseLayout, IReadOnlyList<RebarZone> Zones)
{
    public static PlateRebarField From(PlateSection section, PlanarRegion region) =>
        new(section.RebarLayers, region.RebarZones);
}
