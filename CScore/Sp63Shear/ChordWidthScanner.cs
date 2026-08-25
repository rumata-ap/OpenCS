namespace CScore.Sp63Shear;

/// <summary>
/// Определяет расчётную ширину бетонного сечения как минимальную суммарную длину хорд.
/// Длина хорд многоугольника кусочно-линейна между координатами вершин, поэтому минимум
/// ищется точно по узловым уровням, без равномерной сетки.
/// </summary>
/// <remarks>
/// Пересечения считаются по полуоткрытому правилу, поэтому на самой верхней границе тела
/// хорда вырождается в ноль. Чтобы этот ноль не попал в минимум, ширина определяется
/// только на открытом диапазоне: пробуются уровни, сдвинутые внутрь, и середины
/// подынтервалов, но никогда — само значение в узловом уровне.
/// </remarks>
public static class ChordWidthScanner
{
    /// <summary>Отступ внутрь интервала при пробе уровня вершины, м.</summary>
    const double LevelEpsilon = 1e-9;

    /// <summary>Суммарная длина хорд пересечения прямой заданного уровня с бетоном, м.</summary>
    public static double ChordLengthAt(
        IReadOnlyList<MaterialArea> areas, ShearPlane plane, double level)
    {
        ArgumentNullException.ThrowIfNull(areas);
        double total = 0.0;
        foreach (var area in areas)
        {
            if (area.Hull is null) continue;
            total += RingChordLength(area.Hull, plane, level);
            foreach (var hole in area.Holes)
                total -= RingChordLength(hole, plane, level);
        }
        return total > 0.0 ? total : 0.0;
    }

    /// <summary>Минимальная ширина бетона в диапазоне уровней, м.</summary>
    public static double MinWidth(
        IReadOnlyList<MaterialArea> areas, ShearPlane plane, double from, double to)
    {
        ArgumentNullException.ThrowIfNull(areas);
        double lo = Math.Min(from, to);
        double hi = Math.Max(from, to);
        if (hi - lo < LevelEpsilon)
            return ChordLengthAt(areas, plane, lo);

        var levels = new SortedSet<double> { lo, hi };
        foreach (var area in areas)
        {
            if (area.Hull is null) continue;
            CollectLevels(area.Hull, plane, lo, hi, levels);
            foreach (var hole in area.Holes)
                CollectLevels(hole, plane, lo, hi, levels);
        }

        // Пробы только строго внутри диапазона: значение в самом узловом уровне
        // вырождено (полуоткрытое правило пересечений) и в минимум не берётся.
        var probes = new SortedSet<double>();
        var ordered = levels.ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            AddProbe(probes, ordered[i] - LevelEpsilon, lo, hi);
            AddProbe(probes, ordered[i] + LevelEpsilon, lo, hi);
            if (i + 1 < ordered.Count)
                AddProbe(probes, 0.5 * (ordered[i] + ordered[i + 1]), lo, hi);
        }
        if (probes.Count == 0)
            AddProbe(probes, 0.5 * (lo + hi), lo, hi);

        double min = double.MaxValue;
        foreach (double level in probes)
            min = Math.Min(min, ChordLengthAt(areas, plane, level));
        return min == double.MaxValue ? 0.0 : min;
    }

    /// <summary>Добавляет пробный уровень, если он лежит строго внутри диапазона.</summary>
    static void AddProbe(SortedSet<double> probes, double level, double lo, double hi)
    {
        if (level > lo && level < hi) probes.Add(level);
    }

    /// <summary>Добавляет уровни вершин контура, попадающие в диапазон.</summary>
    static void CollectLevels(
        Contour ring, ShearPlane plane, double lo, double hi, SortedSet<double> levels)
    {
        var along = plane == ShearPlane.Vy ? ring.Y : ring.X;
        foreach (double value in along)
            if (value > lo && value < hi)
                levels.Add(value);
    }

    /// <summary>Суммарная длина отрезков пересечения прямой с одним замкнутым контуром.</summary>
    static double RingChordLength(Contour ring, ShearPlane plane, double level)
    {
        var along = plane == ShearPlane.Vy ? ring.Y : ring.X;
        var across = plane == ShearPlane.Vy ? ring.X : ring.Y;
        int count = Math.Min(along.Count, across.Count);
        if (count < 3) return 0.0;

        var crossings = new List<double>();
        for (int i = 0; i < count - 1; i++)
        {
            double a0 = along[i], a1 = along[i + 1];
            if (Math.Abs(a1 - a0) < LevelEpsilon) continue;
            double min = Math.Min(a0, a1), max = Math.Max(a0, a1);
            if (level < min || level >= max) continue;
            double t = (level - a0) / (a1 - a0);
            crossings.Add(across[i] + t * (across[i + 1] - across[i]));
        }

        if (crossings.Count < 2) return 0.0;
        crossings.Sort();
        double total = 0.0;
        for (int i = 0; i + 1 < crossings.Count; i += 2)
            total += crossings[i + 1] - crossings[i];
        return total;
    }
}
