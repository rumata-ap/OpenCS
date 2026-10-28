using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Источник масок в окне «Маски из родителя…»: постановка CSfea родителя с сохранённым результатом.</summary>
/// <param name="SourceKind"><see cref="FragmentMaskSet.SourceCsfeaLinear"/> или <see cref="FragmentMaskSet.SourceCsfeaSecant"/>.</param>
public sealed record FragmentMaskSourceItem(int AnalysisId, string SourceKind, string AnalysisTag, string Label);

/// <summary>Итог снятия масок из окна: отказ (прежние маски целы), строки отчёта, строка журнала.</summary>
public sealed record StoreyFragmentMaskFlowResult(bool IsError, IReadOnlyList<StoreyFragmentReportRow> Rows, string LogMessage);

/// <summary>
/// Команда «Маски из родителя…» без окон: список источников (постановки CSfea родителя со статусом «ok» и
/// результатом) и снятие сервисом <see cref="StoreyFragmentMaskService"/> → строки отчёта. Работа с БД — в
/// вызывающем потоке (запись набора масок).
/// </summary>
public static class StoreyFragmentMaskFlow
{
    /// <summary>Код итоговой строки отчёта снятия.</summary>
    const string SummaryCode = "fragment_mask_summary";

    /// <summary>
    /// Источники масок фрагмента — линейные и секущие постановки CSfea родителя с сохранённым результатом, в порядке
    /// постановок. Пусто — <paramref name="reason"/>: не фрагмент, родитель удалён или у него нет посчитанного CSfea.
    /// </summary>
    public static IReadOnlyList<FragmentMaskSourceItem> Sources(DatabaseService db, FemSchema fragment, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fragment);
        reason = null;
        if (fragment.ParentSchemaId is not int parentId)
        {
            reason = Loc.S("StoreyFragmentMasksNotFragment");
            return [];
        }
        if (db.GetFemSchemaSourceType(parentId) is null)
        {
            reason = Loc.S("StoreyFragmentMasksParentDeleted");
            return [];
        }
        var items = new List<FragmentMaskSourceItem>();
        foreach (var a in db.GetFemAnalyses(parentId))
        {
            bool linear = a.Kind == FemCsfeaRunner.LinearAnalysisKind;
            if (!linear && a.Kind != FemCsfeaRunner.AnalysisKind) continue;
            if (a.Status != "ok" || a.ResultId is not int resultId || db.GetCalcResultById(resultId) is not { } result) continue;
            string created = result.Created.Length > 16 ? result.Created[..16] : result.Created;
            items.Add(new(a.Id, linear ? FragmentMaskSet.SourceCsfeaLinear : FragmentMaskSet.SourceCsfeaSecant, a.Tag,
                string.Format(Loc.S("StoreyFragmentMaskSourceLabel"), a.Tag,
                    Loc.S(linear ? "FemAnalysisKindCsfeaLinear" : "FemAnalysisKindCsfea"), created)));
        }
        if (items.Count == 0) reason = Loc.S("StoreyFragmentMasksNoSources");
        return items;
    }

    /// <summary>Источник, выбранный по умолчанию: тот, из которого сняты действующие маски, иначе первый.</summary>
    public static FragmentMaskSourceItem? DefaultSource(DatabaseService db, FemSchema fragment, IReadOnlyList<FragmentMaskSourceItem> sources)
    {
        if (sources.Count == 0) return null;
        var set = db.GetFragmentMasks(fragment.Id, items: false);
        return sources.FirstOrDefault(s => set != null && s.SourceKind == set.SourceKind
            && s.AnalysisId.ToString(CultureInfo.InvariantCulture) == set.SourceRef) ?? sources[0];
    }

    /// <summary>Снять маски из <paramref name="source"/>; отказ БД не меняет.</summary>
    public static StoreyFragmentMaskFlowResult Extract(DatabaseService db, FemSchema fragment, FragmentMaskSourceItem source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var r = StoreyFragmentMaskService.Extract(db, fragment, source.SourceKind,
            source.AnalysisId.ToString(CultureInfo.InvariantCulture));
        var rows = Rows(r.Diagnostics);
        string log = r.HasErrors
            ? string.Format(Loc.S("StoreyFragmentMasksFailed"), fragment.Tag, source.AnalysisTag, rows.Count(x => x.IsError))
            : string.Format(Loc.S("StoreyFragmentMasksTaken"), fragment.Tag, source.AnalysisTag,
                r.Set!.Items.Select(i => i.CutId).Distinct().Count(), rows.Count(x => !x.IsError && !x.IsSummary));
        return new(r.HasErrors, rows, log);
    }

    /// <summary>Отчёт снятия → строки окна: итог первым, затем ошибки и предупреждения.</summary>
    public static IReadOnlyList<StoreyFragmentReportRow> Rows(IEnumerable<FemValidationDiagnostic> diagnostics)
    {
        var list = diagnostics.ToList();
        var summary = list.Where(d => d.Code == SummaryCode)
            .Select(d => new StoreyFragmentReportRow(Loc.S("StoreyFragmentReportSummary"), d.Message, false, IsSummary: true));
        return summary.Concat(StoreyFragmentReportRows.From(list.Where(d => d.Code != SummaryCode))).ToList();
    }
}
