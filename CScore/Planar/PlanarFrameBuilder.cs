namespace CScore.Planar;

/// <summary>Построение Frame3D по узлам расчётной схемы, выбранным кликом в 3D-виде
/// (см. FemSchemaEditorVM.BuildPlateFrame/BuildWallFrame/BuildSpatialPlateFrame). Origin всегда —
/// координаты первого выбранного узла.</summary>
public static class PlanarFrameBuilder
{
    public const double CoincidenceTolerance = 1e-6;

    /// <summary>Плита: горизонтальный базис на уровне узла P, оси совпадают с глобальными.</summary>
    public static Frame3D BuildPlateFrame(PlanarVector3 origin)
    {
        var frame = new Frame3D(origin,
            new PlanarVector3(1, 0, 0),
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(0, 0, 1));
        frame.Validate();
        return frame;
    }

    /// <summary>Стена: LocalX — горизонтальное направление A→B (угол поворота в плане), LocalY —
    /// глобальный "верх", LocalZ — горизонтальная нормаль стены. Z узла B не участвует в
    /// построении — важна только проекция направления на план.</summary>
    public static Frame3D BuildWallFrame(PlanarVector3 a, PlanarVector3 b)
    {
        var d = new PlanarVector3(b.X - a.X, b.Y - a.Y, 0);
        if (d.Length < CoincidenceTolerance)
            throw new InvalidOperationException("[planar_frame_nodes_coincide_in_plan] Узлы стены совпадают в плане — невозможно определить направление и угол поворота.");

        var localX = d.Normalize();
        var localY = new PlanarVector3(0, 0, 1);
        var localZ = localX.Cross(localY);

        var frame = new Frame3D(a, localX, localY, localZ);
        frame.Validate();
        return frame;
    }

    /// <summary>Произвольная пластина: LocalX — направление A→B, LocalY — ортогонализация
    /// (Грам-Шмидт) направления A→C относительно LocalX, LocalZ = LocalX×LocalY.</summary>
    public static Frame3D BuildSpatialPlateFrame(PlanarVector3 a, PlanarVector3 b, PlanarVector3 c)
    {
        var rawX = b - a;
        if (rawX.Length < CoincidenceTolerance)
            throw new InvalidOperationException("[planar_frame_nodes_identical] Узлы A и B совпадают — невозможно построить локальную ось X.");
        var localX = rawX.Normalize();

        var v = c - a;
        var rawY = v - localX * v.Dot(localX);
        if (rawY.Length < CoincidenceTolerance)
            throw new InvalidOperationException("[planar_frame_nodes_collinear] Узлы A, B и C коллинеарны — невозможно построить плоскость построения.");
        var localY = rawY.Normalize();
        var localZ = localX.Cross(localY);

        var frame = new Frame3D(a, localX, localY, localZ);
        frame.Validate();
        return frame;
    }
}
