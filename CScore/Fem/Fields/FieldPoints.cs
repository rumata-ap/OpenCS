namespace CScore.Fem.Fields;

/// <summary>Операции над набором точек таблицы.</summary>
public static class FieldPoints
{
    /// <summary>
    /// Сливает точки ближе <paramref name="tolerance"/> (м, после совмещения) в одну — значения усредняются. Узлы на
    /// стыках плит в выгрузке ГТО повторяются, и без слияния триангуляция получает совпадающие вершины.
    /// </summary>
    public static (List<FieldPoint> Points, int Merged) MergeDuplicates(IReadOnlyList<FieldPoint> points, double tolerance = 1e-3)
    {
        var result = new List<FieldPoint>(points.Count);
        var counts = new List<int>(points.Count);
        var cells = new Dictionary<(long, long, long), List<int>>();
        int merged = 0;
        foreach (var p in points)
        {
            var key = Key(p.P, tolerance);
            int found = -1;
            for (long dx = -1; dx <= 1 && found < 0; dx++)
            for (long dy = -1; dy <= 1 && found < 0; dy++)
            for (long dz = -1; dz <= 1 && found < 0; dz++)
                if (cells.TryGetValue((key.Item1 + dx, key.Item2 + dy, key.Item3 + dz), out var list))
                    foreach (int i in list)
                        if ((result[i].P - p.P).Length <= tolerance) { found = i; break; }
            if (found < 0)
            {
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = [];
                list.Add(result.Count);
                result.Add(new FieldPoint(p.P, (double[])p.Values.Clone()));
                counts.Add(1);
            }
            else
            {
                merged++;
                var sum = result[found].Values;
                for (int k = 0; k < sum.Length; k++) sum[k] += p.Values[k];
                counts[found]++;
            }
        }
        for (int i = 0; i < result.Count; i++)
            if (counts[i] > 1)
                for (int k = 0; k < result[i].Values.Length; k++) result[i].Values[k] /= counts[i];
        return (result, merged);
    }

    static (long, long, long) Key(FieldVec3 p, double tol) =>
        ((long)Math.Floor(p.X / tol), (long)Math.Floor(p.Y / tol), (long)Math.Floor(p.Z / tol));
}
