using CScore.Fem;

namespace CScore.Submodel;

/// <summary>Проверка согласованности локальных кадров вдоль цепочки.</summary>
public static class ChainFrameConsistency
{
    public static IReadOnlyList<FemValidationDiagnostic> Check(StraightBeamChain chain,
        IBeamLocalFrameProvider? frameProvider, ResolvedTolerances tolerances)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        if (chain.Segments.Count == 0) return diagnostics;
        if (chain.Segments.Select(s => s.Source.BetaSource).Distinct().Count() > 1)
            diagnostics.Add(new("chain_beta_source_mixed", "В цепочке смешаны элементы с известным и неизвестным углом поворота сечения.", false, chain.Segments.Select(s => s.Source.SourceKey).ToList()));
        if (frameProvider is null)
        {
            diagnostics.Add(new("chain_frame_check_skipped", "Проверка локальных осей не выполнялась: поставщик кадров не задан.", false, []));
            return diagnostics;
        }
        var reference = frameProvider.Frame(chain.AxisDirection, EffectiveBeta(chain.Segments[0]));
        foreach (var segment in chain.Segments.Skip(1))
        {
            var angle = ChainAngleMath.AngleBetweenLinesDeg(reference.LocalY, frameProvider.Frame(chain.AxisDirection, EffectiveBeta(segment)).LocalY);
            if (angle > tolerances.AngularDeg + 1e-12)
                diagnostics.Add(new("chain_frame_mismatch", $"Локальные оси элемента {segment.Source.SourceKey} отличаются от осей начала цепочки на {angle:F2}°.", false, [chain.Segments[0].Source.SourceKey, segment.Source.SourceKey]));
        }
        return diagnostics;
    }
    static double EffectiveBeta(OrderedBeamSegment segment) => segment.IsReversed ? -segment.Source.BetaDeg : segment.Source.BetaDeg;
}
