using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Пространственный хеш точек с кубической ячейкой.</summary>
public sealed class SpatialPointIndex
{
    readonly double _cellSizeM;
    readonly Dictionary<(long X, long Y, long Z), List<int>> _cells = [];

    public SpatialPointIndex(double cellSizeM)
    {
        if (!double.IsFinite(cellSizeM) || cellSizeM <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(cellSizeM));
        _cellSizeM = cellSizeM;
    }

    public void Add(int id, PlanarVector3 point)
    {
        var key = Key(point);
        if (!_cells.TryGetValue(key, out var bucket))
            _cells[key] = bucket = [];
        bucket.Add(id);
    }

    /// <summary>Идентификаторы точек из 27 ячеек вокруг заданной точки.</summary>
    public IEnumerable<int> Neighbors(PlanarVector3 point)
    {
        var (x, y, z) = Key(point);
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        for (var dz = -1; dz <= 1; dz++)
        {
            if (!_cells.TryGetValue((x + dx, y + dy, z + dz), out var bucket)) continue;
            foreach (var id in bucket) yield return id;
        }
    }

    (long X, long Y, long Z) Key(PlanarVector3 point) => (
        (long)Math.Floor(point.X / _cellSizeM),
        (long)Math.Floor(point.Y / _cellSizeM),
        (long)Math.Floor(point.Z / _cellSizeM));
}
