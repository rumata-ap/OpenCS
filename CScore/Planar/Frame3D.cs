namespace CScore.Planar;

/// <summary>Ортонормированный правый локальный базис PlanarRegion. LocalZ — направление
/// толщины; знаки оболочечных усилий определяются этим базисом, а не словами «верх»/«низ».</summary>
public sealed record Frame3D(PlanarVector3 Origin, PlanarVector3 LocalX, PlanarVector3 LocalY, PlanarVector3 LocalZ)
{
    public const double OrthogonalityTolerance = 1e-9;
    public const double UnitLengthTolerance = 1e-6;
    public const double RightHandedTolerance = 1e-6;

    public static Frame3D Identity { get; } = new(
        PlanarVector3.Zero,
        new PlanarVector3(1, 0, 0),
        new PlanarVector3(0, 1, 0),
        new PlanarVector3(0, 0, 1));

    public void Validate()
    {
        if (!Origin.IsFinite || !LocalX.IsFinite || !LocalY.IsFinite || !LocalZ.IsFinite)
            throw new InvalidOperationException("Frame3D содержит нечисловые компоненты.");

        CheckUnit(LocalX, nameof(LocalX));
        CheckUnit(LocalY, nameof(LocalY));
        CheckUnit(LocalZ, nameof(LocalZ));

        if (Math.Abs(LocalX.Dot(LocalY)) > OrthogonalityTolerance)
            throw new InvalidOperationException("LocalX и LocalY не ортогональны.");
        if (Math.Abs(LocalX.Dot(LocalZ)) > OrthogonalityTolerance)
            throw new InvalidOperationException("LocalX и LocalZ не ортогональны.");
        if (Math.Abs(LocalY.Dot(LocalZ)) > OrthogonalityTolerance)
            throw new InvalidOperationException("LocalY и LocalZ не ортогональны.");

        var cross = LocalX.Cross(LocalY);
        if (cross.Dot(LocalZ) < 1 - RightHandedTolerance)
            throw new InvalidOperationException("Frame3D не является правой тройкой (LocalX × LocalY ≠ LocalZ).");
    }

    static void CheckUnit(PlanarVector3 v, string name)
    {
        if (Math.Abs(v.Length - 1.0) > UnitLengthTolerance)
            throw new InvalidOperationException($"{name} не является единичным вектором.");
    }

    /// <summary>Восстанавливает Frame3D по геометрии замкнутого полигона (без дублирующей
    /// замыкающей вершины). Нормаль — по методу Ньюэлла (устойчив к невыпуклым контурам),
    /// LocalX — проекция первого ребра на плоскость, Origin — среднее вершин.</summary>
    public static Frame3D FromPolygon(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double> z)
    {
        if (x.Count != y.Count || x.Count != z.Count)
            throw new ArgumentException("Массивы x, y, z должны быть одной длины.");
        if (x.Count < 3)
            throw new ArgumentException("Для построения Frame3D нужно не менее 3 вершин контура.");

        int n = x.Count;
        double nx = 0, ny = 0, nz = 0;
        double ox = 0, oy = 0, oz = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            nx += (y[i] - y[j]) * (z[i] + z[j]);
            ny += (z[i] - z[j]) * (x[i] + x[j]);
            nz += (x[i] - x[j]) * (y[i] + y[j]);
            ox += x[i]; oy += y[i]; oz += z[i];
        }

        var origin = new PlanarVector3(ox / n, oy / n, oz / n);
        var normalRaw = new PlanarVector3(nx, ny, nz);
        if (normalRaw.Length < 1e-12)
            throw new InvalidOperationException("Не удалось построить нормаль: контур вырожден (нулевая площадь).");
        var localZ = normalRaw.Normalize();

        var edge = new PlanarVector3(x[1] - x[0], y[1] - y[0], z[1] - z[0]);
        var edgeOnPlane = edge - localZ * edge.Dot(localZ);
        if (edgeOnPlane.Length < 1e-9)
            throw new InvalidOperationException("Не удалось построить LocalX: первое ребро контура коллинеарно нормали.");
        var localX = edgeOnPlane.Normalize();
        var localY = localZ.Cross(localX);

        var frame = new Frame3D(origin, localX, localY, localZ);
        frame.Validate();
        return frame;
    }
}
