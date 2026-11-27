using System.CommandLine;
using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Cli;

sealed partial class CliApp
{
    readonly Argument<string> _fragmentRef = new("fragment");
    readonly Option<string?> _masksFrom = new("--from");
    readonly Option<bool> _masksAll = new("--all");
    readonly Option<bool> _masksSecant = new("--secant");
    readonly Option<string?> _masksBy = new("--by");
    readonly Option<bool> _masksDelete = new("--delete");

    void DescribeFragmentOptions()
    {
        _fragmentRef.Description = Loc.S("CliArgFragment");
        _masksFrom.Description = Loc.S("CliOptMasksFrom");
        _masksAll.Description = Loc.S("CliOptMasksAll");
        _masksSecant.Description = Loc.S("CliOptMasksSecant");
        _masksBy.Description = Loc.S("CliOptMasksBy");
        _masksDelete.Description = Loc.S("CliOptMasksDelete");
    }

    Command FragmentCommand() => new("fragment", Loc.S("CliCmdFragment"))
    {
        EditCommand("masks", "CliCmdFragmentMasks", FragmentMasksAsync, _fragmentRef, _masksFrom, _masksAll, _masksSecant,
            _masksBy, _masksDelete),
        EditCommand("rebuild", "CliCmdFragmentRebuild", FragmentRebuildAsync, _fragmentRef),
    };

    /// <summary>
    /// <c>opencs fragment rebuild</c> — как «Пересобрать фрагмент…»: состав заново из родителя по тем же отметкам, тот же Id
    /// и имя; постановки, проверки, несовершенства и ГУ разрезов переносятся, маски и результаты сбрасываются. Без вопроса
    /// (CLI); нельзя пересобрать — Usage, отказ построителя — Failed, фрагмент не изменён.
    /// </summary>
    Task<int> FragmentRebuildAsync(CliCall call)
    {
        string spec = call.Parse.GetValue(_fragmentRef)!;
        return Edit(call, session =>
        {
            var db = session.Context.Db;
            var fragment = ResolveFragment(db, spec);
            if (FragmentAuthoring.RebuildBlocker(db, fragment) is { } blocker) throw new CliException(CliExitCode.Usage, blocker);
            var result = FragmentAuthoring.CommitRebuild(db, fragment, FragmentAuthoring.PrepareRebuild(db, fragment));
            if (call.Json)
                CliOutput.WriteJson(call.Out, new
                {
                    Fragment = fragment.Tag, result.IsError, Message = result.LogMessage,
                    Transferred = result.Transfer is { } t
                        ? new { t.Analyses, t.Checks, t.CutBc, t.Imperfections, Lost = t.Lost.Count } : null,
                    Rows = result.Rows.Select(r => new { r.Level, r.Message, r.IsError }).ToList(),
                });
            else
            {
                call.Out.WriteLine(result.LogMessage);
                foreach (var r in result.Rows) call.Out.WriteLine($"  {r.Level}\t{r.Message}");
            }
            if (result.IsError) throw new CliException(CliExitCode.Failed, result.LogMessage);
        });
    }

    /// <summary>
    /// <c>opencs fragment masks</c> — как окно «Маски из родителя…»: из готового результата родителя (<c>--from</c> постановка,
    /// <c>scad</c> или <c>lira</c>), служебным расчётом родителя по всем загружениям (<c>--all</c>) или секущим
    /// (<c>--secant [--by постановка фрагмента]</c>); <c>--delete</c> — удалить. Без ключа — список источников, код Usage.
    /// Отказ снятия прежние маски не трогает (в проект ничего не пишется).
    /// </summary>
    Task<int> FragmentMasksAsync(CliCall call)
    {
        var p = call.Parse;
        string? from = p.GetValue(_masksFrom)?.Trim(), by = p.GetValue(_masksBy)?.Trim();
        bool all = p.GetValue(_masksAll), secant = p.GetValue(_masksSecant), delete = p.GetValue(_masksDelete);
        int modes = (from != null ? 1 : 0) + (all ? 1 : 0) + (secant ? 1 : 0) + (delete ? 1 : 0);
        if (modes > 1) throw new CliException(CliExitCode.Usage, Loc.S("CliFragMasksOneMode"));
        if (by != null && !secant) throw new CliException(CliExitCode.Usage, Loc.S("CliFragMasksByNeedsSecant"));
        string spec = p.GetValue(_fragmentRef)!;

        if (modes == 0)
        {
            using var session = call.OpenProject();
            var fragment = ResolveFragment(session.Context.Db, spec);
            WriteMaskSources(call, session.Context.Db, fragment);
            return Task.FromResult(CliExitCode.Usage);
        }

        return Edit(call, session =>
        {
            var ctx = session.Context;
            var db = ctx.Db;
            var fragment = ResolveFragment(db, spec);
            if (delete)
            {
                bool had = db.GetFragmentMasks(fragment.Id, items: false) != null;
                if (had) db.DeleteFragmentMasks(fragment.Id);
                string text = string.Format(Loc.S(had ? "StoreyFragmentMasksDeleted" : "StoreyFragmentMasksNoneToDelete"), fragment.Tag);
                if (call.Json) CliOutput.WriteJson(call.Out, new { Fragment = fragment.Tag, Deleted = had });
                else call.Out.WriteLine(text);
                return;
            }

            FragmentMaskSourceItem source;
            if (from != null)
            {
                var sources = StoreyFragmentMaskFlow.Sources(db, fragment, out string? reason);
                source = sources.FirstOrDefault(s => s.SourceKind is FragmentMaskSet.SourceScad or FragmentMaskSet.SourceLira
                        ? string.Equals(s.SourceKind, from, StringComparison.OrdinalIgnoreCase)
                        : string.Equals(s.AnalysisTag.Trim(), from, StringComparison.CurrentCultureIgnoreCase)
                          || s.AnalysisId.ToString(CultureInfo.InvariantCulture) == from)
                    ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliFragMasksSourceNotFound"), from,
                        sources.Count == 0 ? reason ?? "—" : string.Join(", ", sources.Select(SourceKey))));
            }
            else
            {
                int? byId = null;
                if (by != null)
                {
                    var secants = StoreyFragmentMaskSupply.SecantAnalyses(db, fragment);
                    byId = CliRefs.Resolve(secants, by, a => a.Id, a => a.Tag, "CliKindAnalysis").Id;
                }
                string? reason;
                var analysis = secant
                    ? StoreyFragmentMaskSupply.PrepareSecant(db, fragment, out reason, byId)
                    : StoreyFragmentMaskSupply.Prepare(db, fragment, out reason);
                if (analysis == null) throw new CliException(CliExitCode.Usage, reason ?? Loc.S("StoreyFragmentMasksAllNotRun"));
                call.Log.Info(string.Format(Loc.S("CliFragMasksRunning"), analysis.Tag, db.FemSchemas.First(s => s.Id == analysis.SchemaId).Tag));
                var run = FemAnalysisExecutor.RunAndSaveAsync(ctx, analysis).GetAwaiter().GetResult();
                if (run == null) throw new CliException(CliExitCode.Failed, Loc.S("StoreyFragmentMasksAllNotRun"));
                if (run.Status == "error")
                {
                    string? cause = FemCsfeaResultSummary.Parse(run.DataJson)?.Report
                        .FirstOrDefault(l => l.StartsWith("Ошибка: ", StringComparison.Ordinal));
                    throw new CliException(CliExitCode.Failed, string.Format(Loc.S("StoreyFragmentMasksAllFailed"), analysis.Tag)
                        + (cause == null ? "" : " " + cause));
                }
                source = new FragmentMaskSourceItem(analysis.Id,
                    secant ? FragmentMaskSet.SourceCsfeaSecant : FragmentMaskSet.SourceCsfeaLinear, analysis.Tag, analysis.Tag);
            }

            var result = StoreyFragmentMaskFlow.Extract(db, fragment, source, save: false);
            FragmentAuthoring.SaveMasks(db, fragment, result);
            if (call.Json)
                CliOutput.WriteJson(call.Out, new
                {
                    Fragment = fragment.Tag, Source = source.AnalysisTag, result.IsError, Message = result.LogMessage,
                    Rows = result.Rows.Select(r => new { r.Level, r.Message, r.IsError }).ToList(),
                });
            else
            {
                call.Out.WriteLine(result.LogMessage);
                foreach (var r in result.Rows) call.Out.WriteLine($"  {r.Level}\t{r.Message}");
            }
            if (result.IsError) throw new CliException(CliExitCode.Failed, result.LogMessage);
        });
    }

    /// <summary>Фрагмент по имени или Id; не фрагмент — ошибка Usage.</summary>
    static FemSchema ResolveFragment(DatabaseService db, string spec)
    {
        var schema = CliRefs.Resolve(db.FemSchemas, spec, s => s.Id, s => s.Tag, "CliKindSchema");
        return schema.ParentSchemaId != null ? schema
            : throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliFragMasksNotFragment"), schema.Tag));
    }

    /// <summary>Ключ источника для <c>--from</c>: имя постановки родителя или scad / lira.</summary>
    static string SourceKey(FragmentMaskSourceItem s) =>
        s.SourceKind is FragmentMaskSet.SourceScad or FragmentMaskSet.SourceLira ? s.SourceKind : s.AnalysisTag;

    /// <summary>Источники масок фрагмента и действующие маски — подсказка без ключа.</summary>
    static void WriteMaskSources(CliCall call, DatabaseService db, FemSchema fragment)
    {
        var sources = StoreyFragmentMaskFlow.Sources(db, fragment, out string? reason);
        var secants = StoreyFragmentMaskSupply.SecantAnalyses(db, fragment);
        string current = StoreyFragmentMaskFlow.CurrentText(db, fragment);
        if (call.Json)
        {
            CliOutput.WriteJson(call.Out, new
            {
                Fragment = fragment.Tag, Current = current, Reason = reason,
                Sources = sources.Select(s => new { From = SourceKey(s), s.Label }).ToList(),
                SecantBy = secants.Select(a => a.Tag).ToList(),
            });
            return;
        }
        call.Out.WriteLine(fragment.Tag + ": " + current);
        call.Out.WriteLine(Loc.S("CliFragMasksSources"));
        if (sources.Count == 0) call.Out.WriteLine("  " + (reason ?? "—"));
        foreach (var s in sources) call.Out.WriteLine($"  --from \"{SourceKey(s)}\"\t{s.Label}");
        call.Out.WriteLine("  --all\t" + Loc.S("StoreyFragmentMasksAll"));
        call.Out.WriteLine(secants.Count == 0 ? "  --secant\t" + Loc.S("StoreyFragmentMasksSecantNone")
            : "  --secant [--by " + string.Join(" | ", secants.Select(a => $"\"{a.Tag}\"")) + "]");
        call.Out.WriteLine("  --delete");
    }

    /// <summary>Сведения фрагмента для <c>list schemas</c>: родитель, его состояние и маски; не фрагмент — null.</summary>
    static (string Parent, string ParentState, string Masks)? FragmentInfo(DatabaseService db, FemSchema s)
    {
        if (s.ParentSchemaId is not int parentId) return null;
        string parent = db.FemSchemas.FirstOrDefault(x => x.Id == parentId)?.Tag ?? "";
        string state;
        try
        {
            state = StoreyFragmentService.CheckParent(db, s) switch
            {
                FragmentParentStatus.Unchanged => "unchanged",
                FragmentParentStatus.Changed => "changed",
                FragmentParentStatus.ParentDeleted => "deleted",
                _ => "unknown",
            };
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { state = "error"; }
        string masks = db.GetFragmentMasks(s.Id, items: false)?.SourceLabel ?? "";
        return (parent, state, masks);
    }
}
