using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>Перекрытие схемы: горизонтальные КонЭ-пластины на одной отметке.</summary>
/// <param name="Z">Отметка срединной плоскости (средняя по КонЭ, взвешенная площадью), м.</param>
/// <param name="AreaM2">Площадь в плане по внешним контурам, м².</param>
/// <param name="Outlines">Внешние контуры КонЭ в плане (XY общих осей, без замыкающей вершины).</param>
public sealed record StoreyFloorLevel(double Z, double AreaM2, IReadOnlyList<string> MemberTags,
    IReadOnlyList<IReadOnlyList<PlanarPoint2D>> Outlines);

/// <summary>Полоса фрагмента по перекрытию <paramref name="LevelIndex"/>.</summary>
/// <param name="HeightBelow">Высота этажа ниже; null — этажа ниже нет, z₁ ниже низа схемы.</param>
/// <param name="HeightAbove">Высота этажа выше; null — этажа выше нет, z₂ выше верха схемы.</param>
/// <param name="CrossedLevelZ">Отметки других перекрытий, через которые проходит разрез (плиты другого крыла).</param>
public sealed record StoreyFloorBand(int LevelIndex, double Z1, double Z2, double? HeightBelow, double? HeightAbove,
    IReadOnlyList<double> CrossedLevelZ);

/// <summary>
/// Поиск перекрытий и автоотметок фрагмента этажа (спека 2026-10-29): уровни горизонтальных КонЭ-пластин, соседи выше и
/// ниже по пересечению в плане, разрезы — середины этажей; соседа нет — до края схемы.
/// </summary>
public static class StoreyFloorLevels
{
    /// <summary>Допуск отметки уровня, м: плиты разной толщины со смещением срединной плоскости — одно перекрытие.</summary>
    public const double LevelToleranceM = 0.05;
    /// <summary>Наибольший наклон нормали пластины к вертикали, при котором она считается перекрытием, °.</summary>
    public const double MaxTiltDeg = 2;
    /// <summary>Запас за низом/верхом схемы, когда соседнего этажа нет, м.</summary>
    public const double EdgeMarginM = 1;
    /// <summary>Наименьшая площадь пересечения в плане, при которой перекрытия — соседи, м².</summary>
    public const double MinOverlapM2 = 0.5;
    /// <summary>Наименьшая доля площади меньшего перекрытия, при которой перекрытия — соседи.</summary>
    public const double MinOverlapShare = 0.01;

    /// <summary>Уровни по возрастанию отметки из КонЭ-пластин с областями; наклонные пластины не перекрытия.</summary>
    public static List<StoreyFloorLevel> Find(IEnumerable<FemMember> members, IEnumerable<PlanarRegion> regions)
    {
        var regionById = regions.Where(r => r.Id > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        double cosTilt = Math.Cos(MaxTiltDeg * Math.PI / 180);
        var plates = new List<(string Tag, double Z, double Area, IReadOnlyList<PlanarPoint2D> Outline)>();
        foreach (var m in members)
        {
            if (m.PlanarRegionId is not int id || !regionById.TryGetValue(id, out var region) || region.Hull is null) continue;
            if (Math.Abs(region.Frame.LocalZ.Z) < cosTilt) continue;
            var outline = GlobalOutline(region);
            if (outline.Count < 3) continue;
            double area = Math.Abs(PlanarPolygonOps.SignedArea(outline.Select(p => new PlanarPoint2D(p.X, p.Y)).ToList()));
            if (area <= 0) continue;
            plates.Add((m.ElemTag, outline.Average(p => p.Z), area,
                outline.Select(p => new PlanarPoint2D(p.X, p.Y)).ToList()));
        }

        var levels = new List<StoreyFloorLevel>();
        var group = new List<(string Tag, double Z, double Area, IReadOnlyList<PlanarPoint2D> Outline)>();
        foreach (var p in plates.OrderBy(p => p.Z))
        {
            if (group.Count > 0 && p.Z - group[^1].Z > LevelToleranceM)
            {
                levels.Add(ToLevel(group));
                group.Clear();
            }
            group.Add(p);
        }
        if (group.Count > 0) levels.Add(ToLevel(group));
        return levels;
    }

    /// <summary>
    /// Полоса по уровню <paramref name="index"/>: середины этажей до ближайших соседей, пересекающихся в плане; соседа
    /// нет — край схемы с запасом <see cref="EdgeMarginM"/>.
    /// </summary>
    public static StoreyFloorBand Band(IReadOnlyList<StoreyFloorLevel> levels, int index, double schemaZMin, double schemaZMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, levels.Count);
        var level = levels[index];
        StoreyFloorLevel? below = null, above = null;
        for (int i = index - 1; i >= 0 && below is null; i--)
            if (Overlap(level, levels[i])) below = levels[i];
        for (int i = index + 1; i < levels.Count && above is null; i++)
            if (Overlap(level, levels[i])) above = levels[i];

        double z1 = below is null ? Math.Min(schemaZMin, level.Z) - EdgeMarginM : (level.Z + below.Z) / 2;
        double z2 = above is null ? Math.Max(schemaZMax, level.Z) + EdgeMarginM : (level.Z + above.Z) / 2;
        var crossed = levels.Where((l, i) => i != index && (Math.Abs(l.Z - z1) <= LevelToleranceM || Math.Abs(l.Z - z2) <= LevelToleranceM))
            .Select(l => l.Z).ToList();
        return new(index, z1, z2, level.Z - below?.Z, above?.Z - level.Z, crossed);
    }

    /// <summary>Пересекаются ли перекрытия в плане: площадь пересечения контуров больше
    /// max(<see cref="MinOverlapM2"/>; <see cref="MinOverlapShare"/> меньшей площади). Отверстия не вычитаются.</summary>
    public static bool Overlap(StoreyFloorLevel a, StoreyFloorLevel b)
    {
        double threshold = Math.Max(MinOverlapM2, MinOverlapShare * Math.Min(a.AreaM2, b.AreaM2));
        var trianglesB = b.Outlines.Select(o => (Box: Box(o), Triangles: PlanarPolygonOps.Triangulate(o))).ToList();
        double sum = 0;
        foreach (var oa in a.Outlines)
        {
            var boxA = Box(oa);
            var trianglesA = PlanarPolygonOps.Triangulate(oa);
            foreach (var (boxB, trisB) in trianglesB)
            {
                if (!Intersects(boxA, boxB)) continue;
                foreach (var ta in trianglesA)
                    foreach (var tb in trisB)
                    {
                        var clip = PlanarPolygonOps.ClipByConvex(ta, tb);
                        if (clip.Count >= 3) sum += Math.Abs(PlanarPolygonOps.SignedArea(clip));
                        if (sum > threshold) return true;
                    }
            }
        }
        return false;
    }

    /// <summary>
    /// Отметка перекрытия по точкам выделения: разброс z не больше <see cref="LevelToleranceM"/> — средняя z; иначе
    /// null и ключ строки ошибки (<c>StoreyFloorPickEmpty</c> / <c>StoreyFloorPickTilted</c>).
    /// </summary>
    public static double? PickZ(IReadOnlyCollection<PlanarVector3> points, out string? errorKey)
    {
        errorKey = null;
        if (points.Count == 0) { errorKey = "StoreyFloorPickEmpty"; return null; }
        double min = points.Min(p => p.Z), max = points.Max(p => p.Z);
        if (max - min > LevelToleranceM) { errorKey = "StoreyFloorPickTilted"; return null; }
        return points.Average(p => p.Z);
    }

    /// <summary>Индекс уровня с отметкой в пределах <see cref="LevelToleranceM"/> от <paramref name="z"/> (ближайший).</summary>
    public static int? LevelAt(IReadOnlyList<StoreyFloorLevel> levels, double z)
    {
        int? best = null;
        for (int i = 0; i < levels.Count; i++)
            if (Math.Abs(levels[i].Z - z) <= LevelToleranceM && (best is null || Math.Abs(levels[i].Z - z) < Math.Abs(levels[best.Value].Z - z)))
                best = i;
        return best;
    }

    static StoreyFloorLevel ToLevel(List<(string Tag, double Z, double Area, IReadOnlyList<PlanarPoint2D> Outline)> group)
    {
        double area = group.Sum(p => p.Area);
        return new(group.Sum(p => p.Z * p.Area) / area, area, group.Select(p => p.Tag).ToList(),
            group.Select(p => p.Outline).ToList());
    }

    /// <summary>Вершины внешнего контура области в общих осях, без замыкающей.</summary>
    static List<PlanarVector3> GlobalOutline(PlanarRegion region)
    {
        var hull = region.RequireHull();
        int n = hull.X.Count;
        if (n > 1 && hull.X[0] == hull.X[n - 1] && hull.Y[0] == hull.Y[n - 1]) n--;
        return Enumerable.Range(0, n)
            .Select(i => PlanarBoundaryFrameConverter.ToGlobalPoint(region.Frame, new PlanarVector3(hull.X[i], hull.Y[i], 0)))
            .ToList();
    }

    static (double X0, double Y0, double X1, double Y1) Box(IReadOnlyList<PlanarPoint2D> p) =>
        (p.Min(q => q.U), p.Min(q => q.V), p.Max(q => q.U), p.Max(q => q.V));

    static bool Intersects((double X0, double Y0, double X1, double Y1) a, (double X0, double Y0, double X1, double Y1) b) =>
        a.X0 < b.X1 && b.X0 < a.X1 && a.Y0 < b.Y1 && b.Y0 < a.Y1;
}
