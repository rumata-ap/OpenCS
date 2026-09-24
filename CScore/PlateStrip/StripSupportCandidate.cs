using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Происхождение опоры полосы: задана вручную или найдена в родительской схеме.</summary>
public enum StripSupportKind { Manual, NodalRestraint, Column, Wall }

/// <summary>Кандидат в опору полосы, найденный в родительской FEM-схеме
/// (<c>StripSupportCandidateCollector</c>). Источник опор — геометрия схемы, а не теги узлов.
///
/// <para><see cref="Id"/> детерминирован: <c>support:{kind}:{sourceKind}:{sourceId}:{ordinal}</c>,
/// где ordinal — номер следа внутри одного источника (стена, разрезанная отверстием, даёт два
/// следа). Он же служит Id встроенного constraint сетки.</para></summary>
/// <param name="Footprint">След опоры во внутренних координатах (U, V) региона плиты: Point или Curve.</param>
/// <param name="RestrainedDofs">Закреплённые глобальные DOF UX..RZ; только у NodalRestraint,
/// у Column/Wall все false (монолитная опора закреплений поворота не задаёт).</param>
public sealed record StripSupportCandidate(
    string Id,
    StripSupportKind Kind,
    PlanarConstraintGeometry Footprint,
    bool[] RestrainedDofs,
    PlanarBoundarySourceReference Source)
{
    /// <summary>Детерминированный Id кандидата.</summary>
    public static string BuildId(StripSupportKind kind, PlanarBoundarySourceReference source, int ordinal) =>
        $"support:{kind.ToString().ToLowerInvariant()}:{source.SourceKind}:{source.SourceId}:{ordinal}";
}
