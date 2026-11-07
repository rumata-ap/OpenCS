using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Fem.Imperfections;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Задача постановки устойчивости для выбора форм в диалоге несовершенства: подпись, λ форм.</summary>
public sealed record FemBucklingCaseChoice(int Index, string Tag, IReadOnlyList<double> Lambda);

/// <summary>
/// Постановка «Расчётные длины — CSfea» схемы для выбора форм: состояние результата (<see cref="Status"/> — пусто, если
/// формы годятся, иначе почему нет) и задачи.
/// </summary>
public sealed record FemBucklingSourceChoice(int AnalysisId, string Tag, string Status, IReadOnlyList<FemBucklingCaseChoice> Cases);

/// <summary>
/// Формы потери устойчивости постановок <see cref="FemBucklingRunner.AnalysisKind"/> для несовершенств (спека 6):
/// сводка результата, записи форм (<see cref="FemBucklingModeCodec"/>, только v2), сверка отпечатка геометрии
/// (<see cref="FemGeometryFingerprint"/>) с текущей схемой; α_cr для отчёта несовершенств.
/// </summary>
public static class FemBucklingModes
{
    /// <summary>Формы <paramref name="p"/> для схемы <paramref name="schemaId"/> с текущим отпечатком <paramref name="geometry"/>.</summary>
    public static FemModesLookup Lookup(DatabaseService db, int schemaId, string geometry, FemModesParams p)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(p);
        var analysis = db.GetFemAnalyses(schemaId).FirstOrDefault(a => a.Id == p.AnalysisId);
        if (analysis is not { Kind: FemBucklingRunner.AnalysisKind })
            return FemModesLookup.Fail("imperfection_modes_source", $"нет постановки устойчивости #{p.AnalysisId} в схеме.");
        if (Summary(db, analysis, out var saved) is not { } summary)
            return FemModesLookup.Fail("imperfection_modes_source", $"постановка «{analysis.Tag}» не посчитана — выполните расчёт устойчивости.");
        if (summary.Geometry.Length == 0 || summary.Geometry != geometry)
            return FemModesLookup.Fail("imperfection_modes_stale",
                $"формы постановки «{analysis.Tag}» устарели (геометрия схемы изменилась после расчёта устойчивости) — пересчитайте постановку.");
        if (p.CaseIndex < 0 || p.CaseIndex >= summary.Cases.Count)
            return FemModesLookup.Fail("imperfection_modes_source", $"в постановке «{analysis.Tag}» нет задачи №{p.CaseIndex + 1}.");
        var c = summary.Cases[p.CaseIndex];
        var shapes = new Dictionary<int, FemModeShape>();
        foreach (var m in p.Modes)
        {
            if (m.Mode < 1 || m.Mode > c.Lambda.Length) continue;
            if (db.GetFemResultStep(saved!.Id, c.FirstRecord + m.Mode - 1) is not { } data) continue;
            var shape = FemBucklingModeCodec.Decode(data).ToShape();
            if (shape == null)
                return FemModesLookup.Fail("imperfection_modes_stale",
                    $"формы постановки «{analysis.Tag}» записаны прежней версией (без поворотов узлов) — пересчитайте постановку.");
            shapes[m.Mode] = shape;
        }
        return new FemModesLookup
        {
            AnalysisTag = analysis.Tag, CaseTag = CaseLabel(c), Lambda = c.Lambda, Shapes = shapes,
        };
    }

    /// <summary>
    /// Строка α_cr для отчёта несовершенств: наименьшее λ₁ по задачам посчитанных постановок устойчивости схемы с
    /// совпадающим отпечатком геометрии; null — таких нет.
    /// </summary>
    public static string? AlphaCr(DatabaseService db, int schemaId, string geometry)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (geometry.Length == 0) return null;
        (double Lambda, string Analysis, string Case)? best = null;
        foreach (var a in db.GetFemAnalyses(schemaId).Where(a => a.Kind == FemBucklingRunner.AnalysisKind))
        {
            if (Summary(db, a, out _) is not { } s || s.Geometry != geometry) continue;
            foreach (var c in s.Cases.Where(c => c.Lambda.Length > 0))
                if (best == null || c.Lambda[0] < best.Value.Lambda) best = (c.Lambda[0], a.Tag, CaseLabel(c));
        }
        return best is { } b ? FemImperfectionResolver.AlphaCrLine(b.Lambda, b.Analysis, b.Case) : null;
    }

    /// <summary>Постановки устойчивости схемы для диалога: состояние форм против текущего отпечатка <paramref name="geometry"/>.</summary>
    public static List<FemBucklingSourceChoice> Sources(DatabaseService db, int schemaId, string geometry)
    {
        ArgumentNullException.ThrowIfNull(db);
        var result = new List<FemBucklingSourceChoice>();
        foreach (var a in db.GetFemAnalyses(schemaId).Where(a => a.Kind == FemBucklingRunner.AnalysisKind))
        {
            var s = Summary(db, a, out _);
            string status = s == null ? Loc.S("FemImperfectionModesNotComputed")
                : s.Geometry.Length == 0 || s.Geometry != geometry ? Loc.S("FemImperfectionModesStale") : "";
            var cases = s?.Cases.Select((c, i) => new FemBucklingCaseChoice(i, CaseLabel(c), c.Lambda)).ToList() ?? [];
            result.Add(new FemBucklingSourceChoice(a.Id, a.Tag, status, cases));
        }
        return result;
    }

    static string CaseLabel(FemBucklingCaseSummary c) =>
        c.Envelope ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemImperfectionModesEnvelopeCase"), c.Tag) : c.Tag;

    static FemBucklingSummary? Summary(DatabaseService db, FemAnalysis analysis, out CalcResult? saved)
    {
        saved = analysis.ResultId is int rid ? db.GetCalcResultById(rid) : null;
        if (saved is not { TaskKind: FemBucklingRunner.TaskKind, Status: "ok" or "not_converged" }) return null;
        return FemBucklingSummary.Parse(saved.DataJson);
    }
}
