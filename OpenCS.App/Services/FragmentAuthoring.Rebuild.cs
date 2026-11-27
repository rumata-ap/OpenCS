using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Подготовленная пересборка: запрос по рецепту фрагмента и черновик построителя; отказ — строки диагностики.</summary>
public sealed record FragmentRebuildPrepared(StoreyFragmentRequest? Request, StoreyFragmentPrepared? Prepared,
    IReadOnlyList<FemValidationDiagnostic> Refusal)
{
    public bool IsError => Prepared is null || Prepared.Draft.HasErrors;
}

/// <summary>Итог пересборки: отказ или перенос; строки отчёта (сборка и перенос) и строка журнала.</summary>
public sealed record FragmentRebuildResult(bool IsError, IReadOnlyList<StoreyFragmentReportRow> Rows, string LogMessage,
    FragmentRebuildTransferReport? Transfer);

/// <summary>Что сохранится и что сбросится — для вопроса перед пересборкой.</summary>
public sealed record FragmentRebuildImpact(string ParentTag, string Range, int Analyses, int Checks, int CutBcOverrides,
    bool HasMasks, int Results);

public static partial class FragmentAuthoring
{
    /// <summary>
    /// Почему фрагмент нельзя пересобрать: не фрагмент, родитель удалён, у родной схемы-родителя нет сетки (снимок полосы
    /// читается с сетки). null — можно.
    /// </summary>
    public static string? RebuildBlocker(DatabaseService db, FemSchema fragment)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.ParentSchemaId is not int parentId || StoreyFragmentService.ParentRequest(fragment) is null)
            return Loc.S("FragmentRebuildNotFragment");
        if (db.FemSchemas.FirstOrDefault(s => s.Id == parentId) is not { } parent || db.GetFemSchemaSourceType(parentId) is null)
            return Loc.S("FragmentRebuildParentDeleted");
        if (FemSchemaAuthoring.IsNative(parent) && db.GetFemMeshElements(parentId).Count == 0)
            return string.Format(Loc.S("FragmentRebuildParentNoMesh"), parent.Tag.Trim());
        return null;
    }

    /// <summary>Сведения для вопроса: родитель, отметки, что перенесётся и что сбросится.</summary>
    public static FragmentRebuildImpact RebuildImpact(DatabaseService db, FemSchema fragment)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fragment);
        string parentTag = db.FemSchemas.FirstOrDefault(s => s.Id == fragment.ParentSchemaId)?.Tag ?? $"#{fragment.ParentSchemaId}";
        string F(double? v) => v?.ToString("0.###", CultureInfo.CurrentCulture) ?? "—";
        int results = fragment.Analyses.Count(a => a.ResultId != null) + db.FemChecks.Count(c => c.SchemaId == fragment.Id && c.ResultId != null);
        return new(parentTag, $"{F(fragment.FragmentZ1)}…{F(fragment.FragmentZ2)}", fragment.Analyses.Count,
            db.FemChecks.Count(c => c.SchemaId == fragment.Id), db.GetFragmentCutBc(fragment.Id).Count,
            db.GetFragmentMasks(fragment.Id, items: false) != null, results);
    }

    /// <summary>Текст вопроса перед пересборкой (окно; CLI печатает те же сведения в отчёте).</summary>
    public static string RebuildQuestion(FemSchema fragment, FragmentRebuildImpact impact) =>
        string.Format(Loc.S("FragmentRebuildConfirm"), fragment.Tag, impact.ParentTag, impact.Range,
            impact.Analyses, impact.Checks, impact.CutBcOverrides);

    /// <summary>
    /// Запрос пересборки по рецепту фрагмента: те же отметки и «Сохранять сетку родителя»; у фрагмента вокруг элементов
    /// призма строится заново по тем же КонЭ и отступу d (колонна у родителя могла сдвинуться). КонЭ призмы больше нет —
    /// отказ построителя призмы.
    /// </summary>
    public static FragmentRebuildPrepared PrepareRebuild(DatabaseService db, FemSchema fragment)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fragment);
        if (RebuildBlocker(db, fragment) is { } blocker)
            return new(null, null, [new FemValidationDiagnostic("fragment_rebuild_blocked", blocker)]);
        int parentId = fragment.ParentSchemaId!.Value;
        var request = StoreyFragmentService.ParentRequest(fragment)!;
        var info = FragmentPrismCodec.Decode(fragment.FragmentPrismJson);
        if (info is { MemberTags: { Count: > 0 } tags, OffsetM: double d })
        {
            var selection = ElementFragmentService.PrismFromSelection(db, parentId, tags, d);
            if (selection.HasErrors || selection.Prism is not { } prism)
                return new(request, null, selection.Diagnostics.Where(x => x.IsError).DefaultIfEmpty(
                    new FemValidationDiagnostic("fragment_prism_empty", Loc.S("FragmentRebuildPrismFailed"))).ToList());
            request = request with { Prism = prism, PrismInfo = info with { Prism = prism }, MeshRebuildMemberTags = selection.BarTags };
        }
        return new(request, StoreyFragmentService.Prepare(db, parentId, request), []);
    }

    /// <summary>
    /// Запись пересборки (в потоке соединения БД): отказ построителя — фрагмент не меняется; иначе состав заменяется на
    /// месте с переносом постановок, проверок, несовершенств и ГУ разрезов. Строки отчёта — сборка, затем перенос.
    /// </summary>
    public static FragmentRebuildResult CommitRebuild(DatabaseService db, FemSchema fragment, FragmentRebuildPrepared prepared)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(prepared);
        string parentTag = db.FemSchemas.FirstOrDefault(s => s.Id == fragment.ParentSchemaId)?.Tag ?? $"#{fragment.ParentSchemaId}";
        if (prepared.IsError || prepared.Request is not { } request)
        {
            var refusal = prepared.Prepared?.Draft.Diagnostics ?? prepared.Refusal;
            var rows = StoreyFragmentReportRows.From(refusal).ToList();
            return new(true, rows, string.Format(Loc.S("FragmentRebuildRefused"), fragment.Tag, rows.Count(r => r.IsError)), null);
        }
        var draft = prepared.Prepared!.Draft;
        var transfer = db.RebuildStoreyFragmentSchema(fragment, request, draft, prepared.Prepared.Fingerprint);
        var report = StoreyFragmentReportRows.From(draft.Diagnostics.Concat(transfer.Lost)).ToList();
        if (draft.Cuts.Count == 0)
            report.Insert(0, new StoreyFragmentReportRow(Loc.S("StoreyFragmentReportWarning"), Loc.S("StoreyFragmentNoCuts"), false));
        report.Insert(0, new StoreyFragmentReportRow(Loc.S("FragmentRebuildReportSummary"), string.Format(Loc.S("FragmentRebuildSummary"),
            transfer.Analyses, transfer.Checks, transfer.CutBc, transfer.Imperfections), false));
        string log = string.Format(Loc.S("FragmentRebuildDone"), fragment.Tag, parentTag, draft.Members.Count,
            transfer.Analyses, transfer.Checks, transfer.CutBc, transfer.Lost.Count);
        return new(false, report, log, transfer);
    }
}
