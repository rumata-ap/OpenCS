namespace CScore.Planar;

public enum BoundaryLoop { Outer, Hole }

/// <summary>Роль размеченного участка границы PlanarRegion. Разметка — свойство геометрии,
/// сама по себе не создаёт связей между объектами (см. PlanarConnection — будущая спека).</summary>
public enum BoundaryRole
{
    Unclassified,
    Free,
    Support,
    BeamJunction,
    WallJunction,
    Opening,
    Load
}

/// <summary>Размеченный участок границы контура PlanarRegion между двумя соседними вершинами
/// одного замкнутого контура (внешнего или одного из отверстий).</summary>
public sealed class BoundarySegment
{
    public BoundaryLoop Loop { get; set; } = BoundaryLoop.Outer;
    /// <summary>Индекс отверстия в PlanarRegion.Holes. Игнорируется при Loop == Outer.</summary>
    public int HoleIndex { get; set; }
    public int StartVertex { get; set; }
    public int EndVertex { get; set; }
    public BoundaryRole Role { get; set; } = BoundaryRole.Unclassified;
    public string? Provenance { get; set; }

    public void Validate(int loopVertexCount)
    {
        if (loopVertexCount < 3)
            throw new InvalidOperationException("Контур должен содержать не менее 3 вершин для разметки границ.");
        if (StartVertex < 0 || StartVertex >= loopVertexCount)
            throw new InvalidOperationException($"StartVertex вне диапазона контура (0..{loopVertexCount - 1}).");
        if (EndVertex < 0 || EndVertex >= loopVertexCount)
            throw new InvalidOperationException($"EndVertex вне диапазона контура (0..{loopVertexCount - 1}).");
        if (StartVertex == EndVertex)
            throw new InvalidOperationException("BoundarySegment не может иметь нулевую длину (StartVertex == EndVertex).");
    }
}
