using CScore.Fem;
using CScore.Planar;

namespace CScore.PlateStrip;

public sealed record ShellReplacementCheckResult(
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Проверки двойного учёта между PlateStripBeamAnalogy и её shell-регионом источника —
/// чисто доменные диагностики над уже существующими данными (геометрия полосы Среза 1,
/// StripLoadSet Среза 4), без реальной сборки shell+beam (появится в Срезе 7). См.
/// docs/superpowers/specs/2026-08-13-plate-strip-shell-replacement-policy-design.md.</summary>
public static class ShellReplacementDoubleCountingCheck
{
    /// <summary>Сравнивает StripLoadSourceTags манифеста с явно переданным списком тегов, ещё
    /// активных на shell-регионе. Обязательная конвенция для корректности (см. спеку): если
    /// Surface-нагрузка покрывает регион шире коридора полосы, вызывающий код обязан задать её
    /// как две раздельные PlanarLoad с разными тегами — иначе пересечение множеств тегов не
    /// отличимо от легитимного частичного покрытия.</summary>
    /// <param name="retainedLoadTags">Теги нагрузок, которые по замыслу остаются на shell вне
    /// разбиения. Используются только политикой CoupledWithExplicitPartition (Срез 7).</param>
    public static ShellReplacementCheckResult CheckLoads(
        ShellReplacementManifest manifest,
        IReadOnlyList<string> loadsStillActiveOnShell,
        IReadOnlyList<string>? retainedLoadTags = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(loadsStillActiveOnShell);
        retainedLoadTags ??= [];

        var shellTags = new HashSet<string>(loadsStillActiveOnShell);
        var diagnostics = new List<FemValidationDiagnostic>();

        switch (manifest.Policy)
        {
            case ShellReplacementPolicy.ReplaceShellRegion:
                foreach (string tag in manifest.StripLoadSourceTags)
                {
                    if (shellTags.Contains(tag))
                        diagnostics.Add(new("plate_strip_shell_replacement_load_double_count",
                            $"Нагрузка «{tag}» полосы «{manifest.StripId}» с политикой ReplaceShellRegion " +
                            $"всё ещё активна на исходном shell-регионе {manifest.SourceRegionId} — двойной учёт."));
                }
                break;

            case ShellReplacementPolicy.DiagnosticOnly:
                foreach (string tag in manifest.StripLoadSourceTags)
                {
                    if (!shellTags.Contains(tag))
                        diagnostics.Add(new("plate_strip_shell_replacement_diagnostic_incomplete",
                            $"Нагрузка «{tag}» полосы «{manifest.StripId}» с политикой DiagnosticOnly " +
                            $"отсутствует на исходном shell-регионе {manifest.SourceRegionId} — shell перестал " +
                            "быть единственным владельцем без объявленной ReplaceShellRegion."));
                }
                break;

            case ShellReplacementPolicy.CoupledWithExplicitPartition:
                // Нагрузки внутри разбиения перешли к балке и не должны оставаться на shell;
                // нагрузки вне разбиения обязаны на нём остаться — иначе область теряет
                // владельца. Какие теги относятся к разбиению, объявляет вызывающая сторона
                // тем же правилом раздельных тегов, что и для частичного покрытия.
                foreach (string tag in manifest.StripLoadSourceTags)
                {
                    if (shellTags.Contains(tag))
                        diagnostics.Add(new("plate_strip_shell_replacement_load_double_count",
                            $"Нагрузка «{tag}» полосы «{manifest.StripId}» перенесена на разбиение, но " +
                            $"всё ещё активна на shell-регионе {manifest.SourceRegionId} — двойной учёт."));
                }
                foreach (string tag in retainedLoadTags)
                {
                    if (!shellTags.Contains(tag))
                        diagnostics.Add(new("plate_strip_partition_load_orphaned",
                            $"Нагрузка «{tag}» лежит вне разбиения полосы «{manifest.StripId}», но снята с " +
                            $"shell-региона {manifest.SourceRegionId} — область осталась без владельца нагрузки."));
                }
                break;

            default:
                diagnostics.Add(new("plate_strip_shell_replacement_invalid_input",
                    $"Полоса «{manifest.StripId}»: политика {manifest.Policy} не распознана проверкой " +
                    "двойного учёта."));
                break;
        }

        return new(diagnostics.All(d => !d.IsError), diagnostics);
    }

    /// <summary>Обнаруживает пересечение коридоров нескольких полос с ReplaceShellRegion на
    /// одном SourceRegionId — только такие записи претендуют на замену жёсткости.
    /// DiagnosticOnly-записи и записи с разных регионов в проверку не входят (регионы плиты по
    /// построению не пересекаются — коридор полосы клиппирован по Hull своего региона, Срез 1
    /// — так что коридоры разных регионов физически не могут пересекаться).</summary>
    public static ShellReplacementCheckResult CheckStiffness(IReadOnlyList<ShellReplacementManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        var diagnostics = new List<FemValidationDiagnostic>();

        // На жёсткость претендуют обе замещающие политики: ReplaceShellRegion — всем коридором,
        // CoupledWithExplicitPartition — своим разбиением (ClaimedPolygon).
        var candidates = manifests
            .Where(m => m.Policy is ShellReplacementPolicy.ReplaceShellRegion
                                 or ShellReplacementPolicy.CoupledWithExplicitPartition)
            .ToList();

        foreach (var manifest in candidates)
        {
            if (manifest.ClaimedPolygon.Count < 3 || manifest.ClaimedPolygon.Any(p => !p.IsFinite))
                diagnostics.Add(new("plate_strip_shell_replacement_invalid_input",
                    $"Полоса «{manifest.StripId}» имеет вырожденную заменяемую область " +
                    "(< 3 точек или нечисловые координаты)."));
        }

        var comparable = candidates
            .Where(m => m.ClaimedPolygon.Count >= 3 && m.ClaimedPolygon.All(p => p.IsFinite))
            .GroupBy(m => m.SourceRegionId);

        foreach (var group in comparable)
        {
            var list = group.ToList();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    if (PolygonsOverlap(list[i].ClaimedPolygon, list[j].ClaimedPolygon))
                        diagnostics.Add(new(
                            list[i].Policy == ShellReplacementPolicy.CoupledWithExplicitPartition ||
                            list[j].Policy == ShellReplacementPolicy.CoupledWithExplicitPartition
                                ? "plate_strip_partition_overlap"
                                : "plate_strip_shell_replacement_stiffness_double_count",
                            $"Полосы «{list[i].StripId}» и «{list[j].StripId}» на регионе {group.Key} " +
                            "претендуют на пересекающиеся области — двойной учёт жёсткости."));
        }

        return new(diagnostics.All(d => !d.IsError), diagnostics);
    }

    /// <summary>Проверки, специфичные для частичной замены региона (Срез 7): согласованность
    /// декларации, вложенность разбиения в коридор и покрытие его границы объявленными
    /// StripBoundaryInterface. Без последнего действия сохраняемой части передавались бы молча.</summary>
    public static ShellReplacementCheckResult CheckPartition(
        ShellReplacementManifest manifest,
        IReadOnlyList<StripBoundaryInterface> interfaces)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(interfaces);
        var diagnostics = new List<FemValidationDiagnostic>();

        bool isPartition = manifest.Policy == ShellReplacementPolicy.CoupledWithExplicitPartition;
        bool hasPolygon = manifest.PartitionPolygon.Count > 0;

        if (isPartition && !hasPolygon)
        {
            diagnostics.Add(new("plate_strip_partition_polygon_required",
                $"Полоса «{manifest.StripId}» объявлена CoupledWithExplicitPartition, но разбиение " +
                "не задано: заменять нечего."));
            return new(false, diagnostics);
        }
        if (!isPartition && hasPolygon)
        {
            diagnostics.Add(new("plate_strip_partition_polygon_unexpected",
                $"Полоса «{manifest.StripId}» с политикой {manifest.Policy} несёт PartitionPolygon — " +
                "декларация несогласована."));
            return new(false, diagnostics);
        }
        if (!isPartition)
            return new(true, diagnostics);

        if (manifest.PartitionPolygon.Count < 3 || manifest.PartitionPolygon.Any(p => !p.IsFinite))
        {
            diagnostics.Add(new("plate_strip_shell_replacement_invalid_input",
                $"Полоса «{manifest.StripId}»: разбиение вырождено (< 3 точек или нечисловые координаты)."));
            return new(false, diagnostics);
        }

        var corridor = manifest.ReplacedRegionPolygon;
        if (corridor.Count >= 3 && corridor.All(p => p.IsFinite))
        {
            double[][] corridorPoly = corridor.Select(p => new[] { p.U, p.V }).ToArray();
            foreach (var point in manifest.PartitionPolygon)
                // Вершина строго внутри ИЛИ на границе коридора: разбиение по построению
                // опирается на края коридора, а PointInPolygon границу внутренней не считает.
                if (!CSTriangulation.GeometryUtils.PointInPolygon(point.U, point.V, corridorPoly) &&
                    !PointOnBoundary(point, corridor))
                {
                    diagnostics.Add(new("plate_strip_partition_outside_corridor",
                        $"Полоса «{manifest.StripId}»: вершина разбиения ({point.U:G6}, {point.V:G6}) " +
                        "лежит вне коридора полосы — редукция для этой области не строилась."));
                    break;
                }
        }

        var declared = new HashSet<string>(manifest.BoundaryInterfaceIds);
        var available = interfaces
            .Where(i => i.StripId == manifest.StripId && declared.Contains(i.Id))
            .ToList();
        foreach (string id in manifest.BoundaryInterfaceIds)
            if (available.All(i => i.Id != id))
                diagnostics.Add(new("plate_strip_partition_boundary_uncovered",
                    $"Полоса «{manifest.StripId}»: объявленный интерфейс «{id}» не найден среди " +
                    "переданных границ."));

        int freeEdges = CountEdgesOutsideCorridor(manifest.PartitionPolygon, corridor);
        if (freeEdges > available.Count)
            diagnostics.Add(new("plate_strip_partition_boundary_uncovered",
                $"Полоса «{manifest.StripId}»: у разбиения {freeEdges} внутренних граничных " +
                $"участков, а объявлено интерфейсов {available.Count} — часть границы не покрыта, " +
                "действия сохраняемой части передавались бы молча."));

        return new(diagnostics.All(d => !d.IsError), diagnostics);
    }

    /// <summary>Число рёбер разбиения, не лежащих на границе коридора: именно они разделяют
    /// заменяемую и сохраняемую части и потому требуют интерфейса.</summary>
    static int CountEdgesOutsideCorridor(
        IReadOnlyList<PlanarPoint2D> partition, IReadOnlyList<PlanarPoint2D> corridor)
    {
        if (corridor.Count < 2) return partition.Count;

        int count = 0;
        for (int i = 0; i < partition.Count; i++)
        {
            var a = partition[i];
            var b = partition[(i + 1) % partition.Count];
            if (!EdgeLiesOnCorridor(a, b, corridor)) count++;
        }
        return count;
    }

    static bool EdgeLiesOnCorridor(PlanarPoint2D a, PlanarPoint2D b, IReadOnlyList<PlanarPoint2D> corridor)
    {
        for (int j = 0; j < corridor.Count; j++)
        {
            var c = corridor[j];
            var d = corridor[(j + 1) % corridor.Count];
            if (PointOnSegment(a, c, d) && PointOnSegment(b, c, d)) return true;
        }
        return false;
    }

    /// <summary>Лежит ли точка на любом ребре замкнутого контура.</summary>
    static bool PointOnBoundary(PlanarPoint2D p, IReadOnlyList<PlanarPoint2D> polygon)
    {
        for (int i = 0; i < polygon.Count; i++)
            if (PointOnSegment(p, polygon[i], polygon[(i + 1) % polygon.Count])) return true;
        return false;
    }

    static bool PointOnSegment(PlanarPoint2D p, PlanarPoint2D a, PlanarPoint2D b)
    {
        double cross = (b.U - a.U) * (p.V - a.V) - (b.V - a.V) * (p.U - a.U);
        if (Math.Abs(cross) > 1e-9) return false;
        double dot = (p.U - a.U) * (b.U - a.U) + (p.V - a.V) * (b.V - a.V);
        double lengthSquared = (b.U - a.U) * (b.U - a.U) + (b.V - a.V) * (b.V - a.V);
        return dot >= -1e-9 && dot <= lengthSquared + 1e-9;
    }

    /// <summary>Пересечение по аналогии с PlateStripGeometryBuilder.SegmentIntersectsHull (Срез
    /// 1): вершина внутри ИЛИ рёбра пересекаются — необходимо и достаточно для невырожденных
    /// простых полигонов (включая полное вложение). Поведение ровно на границе (только касание)
    /// намеренно консервативно (PointInPolygon/SegmentsIntersect не гарантируют строгую
    /// семантику там) — false positive безопаснее false negative для проверки двойного
    /// учёта.</summary>
    static bool PolygonsOverlap(IReadOnlyList<PlanarPoint2D> a, IReadOnlyList<PlanarPoint2D> b)
    {
        double[][] polyA = a.Select(p => new[] { p.U, p.V }).ToArray();
        double[][] polyB = b.Select(p => new[] { p.U, p.V }).ToArray();

        foreach (var p in a)
            if (CSTriangulation.GeometryUtils.PointInPolygon(p.U, p.V, polyB)) return true;
        foreach (var p in b)
            if (CSTriangulation.GeometryUtils.PointInPolygon(p.U, p.V, polyA)) return true;

        for (int i = 0; i < polyA.Length; i++)
        {
            int i2 = (i + 1) % polyA.Length;
            for (int j = 0; j < polyB.Length; j++)
            {
                int j2 = (j + 1) % polyB.Length;
                if (CSTriangulation.GeometryUtils.SegmentsIntersect(
                        polyA[i][0], polyA[i][1], polyA[i2][0], polyA[i2][1],
                        polyB[j][0], polyB[j][1], polyB[j2][0], polyB[j2][1]))
                    return true;
            }
        }
        return false;
    }
}
