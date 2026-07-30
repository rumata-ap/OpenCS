namespace CScore.PlateRebar;

/// <summary>Пространственное поле армирования оболочечного сечения: базовый равномерный
/// layout (источник истины — PlateSection.RebarLayers) + локальные полигональные зоны.
/// Не персистентный объект сам по себе — конструируется из PlateSection по требованию.</summary>
public sealed record PlateRebarField(IReadOnlyList<PlateRebarLayer> BaseLayout, IReadOnlyList<RebarZone> Zones)
{
    public static PlateRebarField From(PlateSection section) =>
        new(section.RebarLayers, section.RebarZones);
}
