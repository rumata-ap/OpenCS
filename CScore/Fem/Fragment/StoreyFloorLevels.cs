using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>Перекрытие схемы: горизонтальные КонЭ-пластины и КЭ-оболочки сетки без КонЭ на одной отметке.</summary>
/// <param name="Z">Отметка срединной плоскости (средняя, взвешенная площадью), м.</param>
/// <param name="AreaM2">Площадь в плане, м² (КонЭ — по внешним контурам).</param>
/// <param name="MemberTags">КонЭ-пластины уровня.</param>
/// <param name="ElementCount">КЭ-оболочки сетки без КонЭ на уровне (импорт без КонЭ).</param>
/// <param name="Cells">Занятые ячейки растра плана <see cref="StoreyFloorLevels.CellM"/> — для пересечения уровней.</param>
public sealed record StoreyFloorLevel(double Z, double AreaM2, IReadOnlyList<string> MemberTags, int ElementCount,
    IReadOnlySet<long> Cells);

/// <summary>Полоса фрагмента по перекрытию <paramref name="LevelIndex"/>.</summary>
/// <param name="HeightBelow">Высота этажа ниже; null — этажа ниже нет, z₁ ниже низа схемы.</param>
/// <param name="HeightAbove">Высота этажа выше; null — этажа выше нет, z₂ выше верха схемы.</param>
/// <param name="CrossedLevelZ">Отметки других перекрытий, через которые проходит разрез (плиты другого крыла).</param>
public sealed record StoreyFloorBand(int LevelIndex, double Z1, double Z2, double? HeightBelow, double? HeightAbove,
    IReadOnlyList<double> CrossedLevelZ);

/// <summary>
/// Поиск перекрытий и автоотметок фрагмента этажа (спека 2026-10-29): уровни горизонтальных пластин (КонЭ и КЭ сетки без
/// КонЭ), соседи выше и ниже по пересечению в плане, разрезы — середины этажей; соседа нет — до края схемы.
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
    /// <summary>
    /// Наименьшая доля площади самого большого перекрытия, при которой уровень может быть соседом: площадки лестниц,
    /// приямки и мелкие надстройки в списке остаются, но высоту этажа не сбивают.
    /// </summary>
    public const double MinNeighbourAreaShare = 0.05;
    /// <summary>Шаг растра плана для пересечения уровней, м.</summary>
    public const double CellM = 0.5;

    sealed record Piece(double Z, double Area, string? MemberTag, HashSet<long> Cells);

    /// <summary>
    /// Уровни по возрастанию отметки: КонЭ-пластины с областями и КЭ-оболочки сетки, не покрытые КонЭ из
    /// <paramref name="members"/>; наклонные пластины не перекрытия.
    /// </summary>
    public static List<StoreyFloorLevel> Find(IEnumerable<FemMember> members, IEnumerable<PlanarRegion> regions,
        IEnumerable<FemMeshNode> meshNodes, IEnumerable<FemElement> meshElements)
    {
        var memberList = members.ToList();
        var regionById = regions.Where(r => r.Id > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        double cosTilt = Math.Cos(MaxTiltDeg * Math.PI / 180);
        var pieces = new List<Piece>();
        foreach (var m in memberList)
        {
            if (m.PlanarRegionId is not int id || !regionById.TryGetValue(id, out var region) || region.Hull is null) continue;
            if (Math.Abs(region.Frame.LocalZ.Z) < cosTilt) continue;
            var outline = GlobalOutline(region);
            if (outline.Count < 3) continue;
            var plan = outline.Select(p => new PlanarPoint2D(p.X, p.Y)).ToList();
            double area = Math.Abs(PlanarPolygonOps.SignedArea(plan));
            if (area <= 0) continue;
            pieces.Add(new(outline.Average(p => p.Z), area, m.ElemTag, PolygonCells(plan)));
        }

        var memberTags = memberList.Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        var nodesByTag = meshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var e in meshElements)
        {
            if (e.ElemType != "shell" || (e.SourceMemberTag is { } owner && memberTags.Contains(owner))) continue;
            if (FemElementGeometry.Of(e, nodesByTag) is not { IsShell: true } g) continue;
            var p = g.Contour.Select(i => g.Points[i]).ToList();
            double nx = 0, ny = 0, nz = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % p.Count];
                nx += (a.Y - b.Y) * (a.Z + b.Z);
                ny += (a.Z - b.Z) * (a.X + b.X);
                nz += (a.X - b.X) * (a.Y + b.Y);
            }
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len <= 0 || Math.Abs(nz) / len < cosTilt) continue;
            pieces.Add(new(p.Average(q => q.Z), len / 2, null, BoxCells(p)));
        }

        var levels = new List<StoreyFloorLevel>();
        var group = new List<Piece>();
        foreach (var piece in pieces.OrderBy(p => p.Z))
        {
            if (group.Count > 0 && piece.Z - group[^1].Z > LevelToleranceM)
            {
                levels.Add(ToLevel(group));
                group.Clear();
            }
            group.Add(piece);
        }
        if (group.Count > 0) levels.Add(ToLevel(group));
        return levels;
    }

    /// <summary>
    /// Полоса по уровню <paramref name="index"/>: середины этажей до ближайших соседей — уровней не меньше
    /// <see cref="MinNeighbourAreaShare"/> самого большого, пересекающихся в плане; соседа нет — край схемы с запасом
    /// <see cref="EdgeMarginM"/>.
    /// </summary>
    public static StoreyFloorBand Band(IReadOnlyList<StoreyFloorLevel> levels, int index, double schemaZMin, double schemaZMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, levels.Count);
        var level = levels[index];
        double minArea = MinNeighbourAreaShare * levels.Max(l => l.AreaM2);
        bool Neighbour(StoreyFloorLevel other) => other.AreaM2 >= minArea && Overlap(level, other);
        StoreyFloorLevel? below = null, above = null;
        for (int i = index - 1; i >= 0 && below is null; i--)
            if (Neighbour(levels[i])) below = levels[i];
        for (int i = index + 1; i < levels.Count && above is null; i++)
            if (Neighbour(levels[i])) above = levels[i];

        double z1 = below is null ? Math.Min(schemaZMin, level.Z) - EdgeMarginM : (level.Z + below.Z) / 2;
        double z2 = above is null ? Math.Max(schemaZMax, level.Z) + EdgeMarginM : (level.Z + above.Z) / 2;
        var crossed = levels.Where((l, i) => i != index && (Math.Abs(l.Z - z1) <= LevelToleranceM || Math.Abs(l.Z - z2) <= LevelToleranceM))
            .Select(l => l.Z).ToList();
        return new(index, z1, z2, level.Z - below?.Z, above?.Z - level.Z, crossed);
    }

    /// <summary>Пересекаются ли перекрытия в плане: площадь общих ячеек растра больше
    /// max(<see cref="MinOverlapM2"/>; <see cref="MinOverlapShare"/> меньшей площади).</summary>
    public static bool Overlap(StoreyFloorLevel a, StoreyFloorLevel b)
    {
        double threshold = Math.Max(MinOverlapM2, MinOverlapShare * Math.Min(a.AreaM2, b.AreaM2));
        var (small, large) = a.Cells.Count <= b.Cells.Count ? (a.Cells, b.Cells) : (b.Cells, a.Cells);
        double cell = CellM * CellM, sum = 0;
        foreach (var c in small)
            if (large.Contains(c) && (sum += cell) > threshold) return true;
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

    static StoreyFloorLevel ToLevel(List<Piece> group)
    {
        double area = group.Sum(p => p.Area);
        var cells = new HashSet<long>();
        foreach (var p in group) cells.UnionWith(p.Cells);
        return new(group.Sum(p => p.Z * p.Area) / area, area,
            group.Select(p => p.MemberTag).OfType<string>().ToList(), group.Count(p => p.MemberTag is null), cells);
    }

    static long Key(long i, long j) => (i << 32) ^ (j & 0xFFFFFFFFL);

    /// <summary>Ячейки, центры которых внутри многоугольника; малый многоугольник — ячейка его первой вершины.</summary>
    static HashSet<long> PolygonCells(IReadOnlyList<PlanarPoint2D> plan)
    {
        var cells = new HashSet<long>();
        long i0 = (long)Math.Floor(plan.Min(p => p.U) / CellM), i1 = (long)Math.Floor(plan.Max(p => p.U) / CellM);
        long j0 = (long)Math.Floor(plan.Min(p => p.V) / CellM), j1 = (long)Math.Floor(plan.Max(p => p.V) / CellM);
        for (long i = i0; i <= i1; i++)
            for (long j = j0; j <= j1; j++)
                if (PlanarPolygonOps.Contains(plan, new PlanarPoint2D((i + 0.5) * CellM, (j + 0.5) * CellM), 1e-9))
                    cells.Add(Key(i, j));
        if (cells.Count == 0)
            cells.Add(Key((long)Math.Floor(plan[0].U / CellM), (long)Math.Floor(plan[0].V / CellM)));
        return cells;
    }

    /// <summary>Ячейки габарита КЭ в плане (КЭ сетки мелкие — габарит достаточно точен).</summary>
    static HashSet<long> BoxCells(IReadOnlyList<PlanarVector3> p)
    {
        const double eps = 1e-6;
        var cells = new HashSet<long>();
        long i0 = (long)Math.Floor((p.Min(q => q.X) + eps) / CellM), i1 = (long)Math.Floor((p.Max(q => q.X) - eps) / CellM);
        long j0 = (long)Math.Floor((p.Min(q => q.Y) + eps) / CellM), j1 = (long)Math.Floor((p.Max(q => q.Y) - eps) / CellM);
        for (long i = i0; i <= Math.Max(i0, i1); i++)
            for (long j = j0; j <= Math.Max(j0, j1); j++)
                cells.Add(Key(i, j));
        return cells;
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
}
