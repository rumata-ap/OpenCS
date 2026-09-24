using CScore.Fem;
using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Компоненты концевых действий, которые берутся из эпюры родителя.</summary>
[Flags]
public enum StripEndActionComponents { None = 0, N = 1, My = 2, Mz = 4 }

/// <summary>Результат автовывода одного конца полосы.</summary>
/// <param name="Candidates">Совпавшие с опорой кандидаты, по Id.</param>
/// <param name="TransferFromParent">Компоненты эпюры родителя, переносимые на этот конец
/// (соответствующий DOF конца свободен).</param>
/// <param name="Kind">Происхождение опоры для provenance: NodalRestraint &gt; Column &gt; Wall.</param>
public sealed record StripEndDerivation(
    IReadOnlyList<StripSupportCandidate> Candidates,
    StripBeamEndCondition Condition,
    StripEndActionComponents TransferFromParent,
    StripSupportKind Kind);

/// <summary>Результат автовывода опорной схемы полосы.</summary>
public sealed record StripSupportDerivationResult(
    bool IsCalculable,
    StripBeamSupportScheme? Scheme,
    StripEndDerivation? Start,
    StripEndDerivation? End,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    /// <summary>Записать Kind и SourceReferences в опоры полосы. Frame и StructuralMode не
    /// меняются.</summary>
    public void ApplyTo(PlateStripBeamAnalogy analogy)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        if (!IsCalculable || Start == null || End == null)
            throw new InvalidOperationException("Автовывод опор не выполнен — применять нечего.");
        Apply(analogy.StartSupportLocus, Start);
        Apply(analogy.EndSupportLocus, End);
    }

    static void Apply(SupportLocus locus, StripEndDerivation end)
    {
        locus.Kind = end.Kind;
        locus.SourceReferences = end.Candidates.Select(c => c.Source).ToList();
    }
}

/// <summary>Автовывод опорной схемы полосы из кандидатов родительской схемы (спека Среза 8a, A.2).
///
/// <b>Правило (решение пользователя 2026-09-24):</b> конец защемлён (Fixed) только если у
/// совпавшего узлового закрепления закреплён изгибный поворот полосы θy; во всех остальных
/// случаях — колонна, стена, опора без закрепления поворота — шарнир (Pinned) плюс концевые
/// моменты родителя (<c>StripParentEndActions</c>). В линейной постановке это точно
/// воспроизводит эпюру родителя.
///
/// <b>Следствие для нелинейного расчёта:</b> при шарнире с заданными концевыми моментами эпюра
/// моментов статически определима, моменты заморожены на значениях линейного родителя × λ —
/// перераспределения на опоры нет.</summary>
public static class StripSupportDerivation
{
    /// <summary>Допуск совпадения точки опоры со следом кандидата, м.</summary>
    public const double DefaultMatchToleranceM = 0.05;

    const double ProjectionEpsilon = 1e-9;

    public static StripSupportDerivationResult Derive(
        PlateStripBeamAnalogy analogy, Frame3D regionFrame,
        IReadOnlyList<StripSupportCandidate> candidates,
        double matchToleranceM = DefaultMatchToleranceM)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        ArgumentNullException.ThrowIfNull(regionFrame);
        ArgumentNullException.ThrowIfNull(candidates);
        if (!(matchToleranceM >= 0.0) || !double.IsFinite(matchToleranceM))
            throw new ArgumentOutOfRangeException(nameof(matchToleranceM));

        var diagnostics = new List<FemValidationDiagnostic>();
        var frame = analogy.StripFrame;

        var startMatches = Match(analogy.StartSupportLocus, regionFrame, candidates, matchToleranceM);
        var endMatches = Match(analogy.EndSupportLocus, regionFrame, candidates, matchToleranceM);
        if (startMatches.Count == 0) diagnostics.Add(NotFound(analogy, "начала", matchToleranceM));
        if (endMatches.Count == 0) diagnostics.Add(NotFound(analogy, "конца", matchToleranceM));
        if (diagnostics.Count > 0)
            return new(false, null, null, null, diagnostics);

        var startCondition = Condition(startMatches, frame, "начала", analogy, diagnostics);
        var endCondition = Condition(endMatches, frame, "конца", analogy, diagnostics);

        bool axialBoth = IsFixed(startMatches, frame.LocalX, rotation: false) &&
                         IsFixed(endMatches, frame.LocalX, rotation: false);
        var axial = axialBoth ? StripAxialRestraint.BothEnds : StripAxialRestraint.StartOnly;

        var startTransfer = Transfer(startCondition, axialFree: false);
        var endTransfer = Transfer(endCondition, axialFree: axial == StripAxialRestraint.StartOnly);

        return new(
            true,
            new StripBeamSupportScheme(startCondition, endCondition, axial),
            new StripEndDerivation(startMatches, startCondition, startTransfer, KindOf(startMatches)),
            new StripEndDerivation(endMatches, endCondition, endTransfer, KindOf(endMatches)),
            diagnostics);
    }

    static List<StripSupportCandidate> Match(
        SupportLocus locus, Frame3D regionFrame,
        IReadOnlyList<StripSupportCandidate> candidates, double tolerance)
    {
        var local = PlanarBoundaryFrameConverter.ToLocalPoint(regionFrame, locus.Frame.Origin);
        var point = new PlanarPoint2D(local.X, local.Y);
        return candidates
            .Where(c => StripSupportGeometry.DistanceToFootprint(point, c.Footprint) <= tolerance)
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
    }

    static StripBeamEndCondition Condition(
        List<StripSupportCandidate> matches, Frame3D stripFrame, string end,
        PlateStripBeamAnalogy analogy, List<FemValidationDiagnostic> diagnostics)
    {
        if (!IsFixed(matches, stripFrame.LocalY, rotation: true))
            return StripBeamEndCondition.Pinned;

        if (!IsFixed(matches, stripFrame.LocalZ, rotation: true))
            diagnostics.Add(new("plate_strip_support_partial_rotation_fixity",
                $"Опора {end} полосы «{analogy.Id}»: изгибный поворот закреплён, поворот вокруг " +
                "нормали плиты — нет; принято защемление (изгиб полосы в плоскости плиты несущественен).",
                false));
        return StripBeamEndCondition.Fixed;
    }

    /// <summary>Истина, если хотя бы у одного совпавшего узлового закрепления закреплена каждая
    /// глобальная компонента перемещения (или поворота) с ненулевой проекцией оси.</summary>
    static bool IsFixed(List<StripSupportCandidate> matches, PlanarVector3 axis, bool rotation)
    {
        double[] components = [axis.X, axis.Y, axis.Z];
        int offset = rotation ? 3 : 0;
        return matches
            .Where(c => c.Kind == StripSupportKind.NodalRestraint && c.RestrainedDofs.Length == 6)
            .Any(c =>
            {
                for (int i = 0; i < 3; i++)
                    if (Math.Abs(components[i]) > ProjectionEpsilon && !c.RestrainedDofs[offset + i])
                        return false;
                return true;
            });
    }

    static StripEndActionComponents Transfer(StripBeamEndCondition condition, bool axialFree)
    {
        var result = StripEndActionComponents.None;
        if (condition == StripBeamEndCondition.Pinned)
            result |= StripEndActionComponents.My | StripEndActionComponents.Mz;
        if (axialFree)
            result |= StripEndActionComponents.N;
        return result;
    }

    static StripSupportKind KindOf(List<StripSupportCandidate> matches) =>
        matches.Any(c => c.Kind == StripSupportKind.NodalRestraint) ? StripSupportKind.NodalRestraint
        : matches.Any(c => c.Kind == StripSupportKind.Column) ? StripSupportKind.Column
        : StripSupportKind.Wall;

    static FemValidationDiagnostic NotFound(PlateStripBeamAnalogy analogy, string end, double tolerance) =>
        new("plate_strip_support_not_found",
            $"Опора {end} полосы «{analogy.Id}» не совпадает ни с узловым закреплением, ни с " +
            $"колонной, ни со стеной родительской схемы в пределах {tolerance:G4} м.");
}
