using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Сведения о расчёте для признака второго порядка наборов (<see cref="FragmentSecondOrder"/>, спека 2026-11-06, срез 3):
/// порядок источника масок фрагмента (без колонки БД — по виду источника и сводке результата постановки-источника), торцы
/// по ГУ разрезов, сетка вертикальных КонЭ. Читает БД — вызывать в потоке соединения.
/// </summary>
public static class FragmentSecondOrderCheck
{
    /// <summary>
    /// Порядок источника масок: SCAD и ЛИРА-САПР — первый; CSfea — по сводке результата постановки
    /// (<see cref="FemCsfeaResultSummary.SecondOrder"/>: секущий с геомнелином, в т. ч. «Большие перемещения», или линейный
    /// с P-Δ источника), кроме режима «только P-δ» (P-Δ не учтён). null — неизвестно: масок нет, они не
    /// <see cref="FragmentMaskStatus.Current"/> или нет сводки результата.
    /// </summary>
    public static bool? SourceSecondOrder(FragmentMaskSet? set, FragmentMaskStatus status, FemCsfeaResultSummary? summary)
    {
        if (set == null) return null;
        if (set.SourceKind is FragmentMaskSet.SourceScad or FragmentMaskSet.SourceLira) return false;
        if (status != FragmentMaskStatus.Current || summary == null) return null;
        return summary.SecondOrder && !summary.LocalPDelta;
    }

    /// <summary>Сводка результата постановки-источника масок CSfea; null — не CSfea, нет постановки или результата.</summary>
    static FemCsfeaResultSummary? SourceSummary(DatabaseService db, FragmentMaskSet set)
    {
        if (set.SourceKind is not (FragmentMaskSet.SourceCsfeaLinear or FragmentMaskSet.SourceCsfeaSecant)) return null;
        if (!int.TryParse(set.SourceRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            || db.GetFemAnalysis(id) is not { ResultId: int resultId } || db.GetCalcResultById(resultId) is not { } result)
            return null;
        return FemCsfeaResultSummary.Parse(result.DataJson);
    }

    /// <summary>
    /// Сведения о расчёте схемы <paramref name="schema"/> с параметрами <paramref name="csfea"/>: геомнелин
    /// <paramref name="geometric"/>, режим «только P-δ» <paramref name="localOnly"/> и λ_cr каркаса <paramref name="lambda"/>;
    /// у фрагмента — маски и торцы. Сетка — <paramref name="mesh"/>, <paramref name="meshNodes"/> (вход расчёта).
    /// </summary>
    public static FragmentSecondOrderFacts Facts(DatabaseService db, FemSchema schema, FemCsfeaParams csfea, bool geometric,
        bool localOnly, double? lambda, IReadOnlyList<FemElement> mesh, IReadOnlyList<FemMeshNode> meshNodes)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(csfea);
        var facts = new FragmentSecondOrderFacts
        {
            Geometric = geometric, LocalOnly = localOnly, LambdaCr = lambda, IsFragment = schema.IsFragment,
            AllowFirstOrderMasks = csfea.FragmentSecondOrderOnFirstOrderMasks,
            CoarseMembers = geometric ? FragmentSecondOrder.Coarse(mesh, meshNodes) : [],
        };
        if (!geometric || !schema.IsFragment || localOnly) return facts;
        var cuts = db.GetFragmentCutsWithIds(schema.Id).Select(c => (c.Id, c.Cut)).ToList();
        var staticEnds = FragmentSecondOrder.StaticEnds(cuts, FragmentCutBc.ParsePreset(schema.FragmentBcPreset),
            FragmentCutBc.ParseLateralPreset(schema.FragmentLateralBcPreset), db.GetFragmentCutBc(schema.Id), out bool masksUsed);
        var set = db.GetFragmentMasks(schema.Id, items: false);
        var status = set == null ? FragmentMaskStatus.None : StoreyFragmentMaskService.CheckMasks(db, schema);
        var summary = set == null ? null : SourceSummary(db, set);
        return facts with
        {
            MasksUsed = masksUsed, StaticEndMembers = staticEnds,
            SourceSecondOrder = SourceSecondOrder(set, status, summary), SourceLabel = set?.SourceLabel ?? "",
        };
    }
}
