namespace CScore.PlateRebar;

/// <summary>Конвертирует Contour пула (App.Contours) в открытый полигон RebarZone.Polygon
/// (без дублирования замыкающей вершины). Contour может быть как замкнут (последняя вершина
/// совпадает с первой в пределах Contour.CloseTolerance), так и разомкнут — в обоих случаях
/// результат не содержит дублирующей вершины.</summary>
public static class RebarZonePolygonConverter
{
    public static List<RebarZonePoint> FromContour(Contour contour)
    {
        int n = contour.X.Count;
        if (n >= 2 &&
            Math.Abs(contour.X[0] - contour.X[n - 1]) < Contour.CloseTolerance &&
            Math.Abs(contour.Y[0] - contour.Y[n - 1]) < Contour.CloseTolerance)
            n--;

        var result = new List<RebarZonePoint>(n);
        for (int i = 0; i < n; i++)
            result.Add(new RebarZonePoint { U = contour.X[i], V = contour.Y[i] });
        return result;
    }
}
