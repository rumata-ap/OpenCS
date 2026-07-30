namespace CScore.PlateRebar;

/// <summary>Техническая грань оболочки: сторона положительной (PlusN) или отрицательной
/// (MinusN) локальной нормали. Никогда не выводится из знака Zsx/Zsy — только явное поле.</summary>
public enum RebarFace { PlusN, MinusN }

/// <summary>Способ объединения армирования зоны с уже накопленным на той же грани при разрешении.</summary>
public enum RebarZoneOperation { Add, Replace }

/// <summary>Вершина полигона зоны армирования в локальной плоскости поверхности (u, v).
/// Для горизонтальных плит u=x, v=y (глобальная плоскость); для стен/наклонных оболочек —
/// локальные координаты поверхности.</summary>
public sealed class RebarZonePoint
{
    public double U { get; set; }
    public double V { get; set; }
}

/// <summary>Локальная полигональная зона армирования оболочечного сечения. Часть
/// <see cref="PlateRebarField"/>. Полигон — открытый контур (без дублирования замыкающей
/// вершины), как ожидает <see cref="CSTriangulation.GeometryUtils.PointInPolygon(double, double, List{(double, double)})"/>.</summary>
public sealed class RebarZone
{
    public string Name { get; set; } = "";
    public List<RebarZonePoint> Polygon { get; set; } = [];
    public RebarFace Face { get; set; }
    public int Priority { get; set; }
    public RebarZoneOperation Operation { get; set; }

    /// <summary>Армирование зоны на грани <see cref="Face"/>. Поле Layout.Face не используется
    /// resolver-ом (переиспользуется тип PlateRebarLayer, у которого Face имеет смысл только
    /// как элемент PlateRebarField.BaseLayout).</summary>
    public PlateRebarLayer Layout { get; set; } = new();

    /// <summary>Глубокая копия зоны.</summary>
    public RebarZone Clone() => new()
    {
        Name = Name,
        Polygon = Polygon.Select(p => new RebarZonePoint { U = p.U, V = p.V }).ToList(),
        Face = Face,
        Priority = Priority,
        Operation = Operation,
        Layout = Layout.Clone(),
    };
}
