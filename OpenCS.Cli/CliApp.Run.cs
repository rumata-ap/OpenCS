using CScore;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Cli;

sealed partial class CliApp
{
    /// <summary>Объект расчёта: вид для вывода, id, имя и запуск исполнителя.</summary>
    sealed record RunItem(string Kind, string KindKey, int Id, string Tag, Func<Task<CalcResult?>> Run);

    /// <summary>Статусы, при которых расчёт выполнен (непрохождение проверки — результат, а не сбой).</summary>
    static readonly HashSet<string> CompletedStatuses = ["ok", "not_passed", "not_applicable"];

    Task<int> RunTasksAsync(CliCall call) => RunItemsAsync(call, "CliKindTask", ctx =>
        ctx.CalcTasks.OrderBy(t => t.Num).ThenBy(t => t.Id)
            .Select(t => new RunItem("task", "CliKindTask", t.Id, t.Tag, () => CalcTaskExecutor.RunAsync(ctx, t))));

    Task<int> RunChecksAsync(CliCall call) => RunItemsAsync(call, "CliKindCheck", ctx =>
        ctx.Db.FemChecks.OrderBy(c => c.SchemaId).ThenBy(c => c.Id)
            .Select(c => new RunItem("check", "CliKindCheck", c.Id, c.DisplayTag, () => FemCheckExecutor.RunAsync(ctx, c))));

    Task<int> RunAnalysesAsync(CliCall call) => RunItemsAsync(call, "CliKindAnalysis", ctx =>
        AllAnalyses(ctx)
            .Select(x => new RunItem("analysis", "CliKindAnalysis", x.Analysis.Id, x.Analysis.Tag,
                () => FemAnalysisExecutor.RunAndSaveAsync(ctx, x.Analysis))));

    async Task<int> RunItemsAsync(CliCall call, string kindKey, Func<HeadlessProjectContext, IEnumerable<RunItem>> candidates)
    {
        var p = call.Parse;
        int[] ids = p.GetValue(_ids) ?? [];
        bool all = p.GetValue(_all);
        if (ids.Length == 0 && !all) throw new CliException(CliExitCode.Usage, Loc.S("CliErrIdsOrAll"));
        if (ids.Length > 0 && all) throw new CliException(CliExitCode.Usage, Loc.S("CliErrIdsAndAll"));
        bool withData = p.GetValue(_withData);

        using var session = call.OpenProject(
            new ProjectSessionOptions(p.GetValue(_inPlace), p.GetValue(_outFile), p.GetValue(_force)), p.GetValue(_yes));
        var available = candidates(session.Context).ToList();
        var items = all
            ? available
            : ids.Select(id => available.FirstOrDefault(i => i.Id == id)
                ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrNotFound"), Loc.S(kindKey), id))).ToList();

        var report = new List<object>();
        bool failed = false;
        foreach (var item in items)
        {
            if (call.Token.IsCancellationRequested)
            {
                call.Err.WriteLine(Loc.S("CliInterrupted"));
                failed = true;
                break;
            }
            int mark = call.Log.Count;
            var result = await item.Run();
            string status = result?.Status ?? "not_run";
            if (!CompletedStatuses.Contains(status)) failed = true;

            if (call.Json)
            {
                var entry = new Dictionary<string, object?>
                {
                    ["kind"] = item.Kind, ["id"] = item.Id, ["tag"] = item.Tag,
                    ["status"] = status, ["resultId"] = result?.Id,
                    ["messages"] = call.Log.Since(mark).Select(e => new
                    {
                        Level = e.Level.ToString().ToLowerInvariant(), Text = e.Message
                    }).ToList()
                };
                if (withData && result != null) entry["data"] = CliOutput.ParseData(result.DataJson);
                report.Add(entry);
            }
            else if (result == null)
                call.Out.WriteLine(string.Format(Loc.S("CliRunNotDone"), Loc.S(item.KindKey), item.Id, item.Tag));
            else
                call.Out.WriteLine(string.Format(Loc.S("CliRunDone"), Loc.S(item.KindKey), item.Id, item.Tag,
                    status, result.Id));
        }

        bool keeps = session.KeepsResults;
        string? saved = session.Close();
        if (call.Json) CliOutput.WriteJson(call.Out, report);
        if (p.GetValue(_outFile) != null && saved != null)
            call.Err.WriteLine(string.Format(Loc.S("CliSavedTo"), saved));
        else if (!keeps)
            call.Err.WriteLine(Loc.S("CliResultsDiscarded"));
        return failed ? CliExitCode.Failed : CliExitCode.Ok;
    }
}
