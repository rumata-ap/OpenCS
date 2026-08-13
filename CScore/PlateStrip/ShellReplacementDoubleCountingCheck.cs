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
    public static ShellReplacementCheckResult CheckLoads(
        ShellReplacementManifest manifest,
        IReadOnlyList<string> loadsStillActiveOnShell)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(loadsStillActiveOnShell);

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

        var candidates = manifests.Where(m => m.Policy == ShellReplacementPolicy.ReplaceShellRegion).ToList();

        foreach (var manifest in candidates)
        {
            if (manifest.ReplacedRegionPolygon.Count < 3 || manifest.ReplacedRegionPolygon.Any(p => !p.IsFinite))
                diagnostics.Add(new("plate_strip_shell_replacement_invalid_input",
                    $"Полоса «{manifest.StripId}» имеет вырожденный коридор (< 3 точек или нечисловые координаты)."));
        }

        var comparable = candidates
            .Where(m => m.ReplacedRegionPolygon.Count >= 3 && m.ReplacedRegionPolygon.All(p => p.IsFinite))
            .GroupBy(m => m.SourceRegionId);

        foreach (var group in comparable)
        {
            var list = group.ToList();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    if (PolygonsOverlap(list[i].ReplacedRegionPolygon, list[j].ReplacedRegionPolygon))
                        diagnostics.Add(new("plate_strip_shell_replacement_stiffness_double_count",
                            $"Полосы «{list[i].StripId}» и «{list[j].StripId}» с политикой ReplaceShellRegion " +
                            $"на регионе {group.Key} имеют пересекающиеся коридоры — двойной учёт жёсткости."));
        }

        return new(diagnostics.All(d => !d.IsError), diagnostics);
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
