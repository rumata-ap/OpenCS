using System.Text;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Cli;

sealed partial class CliApp
{
    /// <summary>
    /// Результат в JSON: заголовок, разобранный <see cref="CalcResult.DataJson"/> и, у проверок по КЭ,
    /// построчные результаты (хранятся отдельной таблицей).
    /// </summary>
    Task<int> ExportResultAsync(CliCall call)
    {
        var p = call.Parse;
        int? resultId = p.GetValue(_resultId), taskId = p.GetValue(_task),
            checkId = p.GetValue(_check), analysisId = p.GetValue(_analysis);
        if (new[] { resultId, taskId, checkId, analysisId }.Count(v => v != null) != 1)
            throw new CliException(CliExitCode.Usage, Loc.S("CliErrExportSelector"));

        using var session = call.OpenProject();
        var ctx = session.Context;
        var db = ctx.Db;
        CalcResult? result;
        if (resultId is { } rid)
        {
            result = db.GetCalcResultById(rid)
                ?? throw NotFound("CliKindResult", rid);
        }
        else if (taskId is { } tid)
        {
            if (ctx.CalcTasks.All(t => t.Id != tid)) throw NotFound("CliKindTask", tid);
            result = db.CalcResults.Where(r => r.TaskId == tid).MaxBy(r => r.Id)
                ?? throw NoResult("CliKindTask", tid);
        }
        else if (checkId is { } cid)
        {
            if (db.FemChecks.All(c => c.Id != cid)) throw NotFound("CliKindCheck", cid);
            result = db.GetCalcResultByFemCheck(cid) ?? throw NoResult("CliKindCheck", cid);
        }
        else
        {
            int aid = analysisId!.Value;
            var analysis = AllAnalyses(ctx).Select(x => x.Analysis).FirstOrDefault(a => a.Id == aid)
                ?? throw NotFound("CliKindAnalysis", aid);
            result = (analysis.ResultId is { } arid ? db.GetCalcResultById(arid) : null)
                ?? throw NoResult("CliKindAnalysis", aid);
        }

        var json = new Dictionary<string, object?>
        {
            ["id"] = result.Id, ["taskId"] = result.TaskId, ["taskKind"] = result.TaskKind,
            ["taskTag"] = result.TaskTag, ["status"] = result.Status, ["created"] = result.Created,
            ["data"] = CliOutput.ParseData(result.DataJson)
        };
        if (checkId != null || db.CountFemCheckRows(result.Id) > 0)
            json["femCheckRows"] = db.GetFemCheckRows(result.Id);

        string? output = p.GetValue(_output);
        if (output == null)
            CliOutput.WriteJson(call.Out, json);
        else
        {
            using var file = new StreamWriter(output, append: false, new UTF8Encoding(false));
            CliOutput.WriteJson(file, json);
        }
        return Task.FromResult(CliExitCode.Ok);

        static CliException NotFound(string kindKey, int id) =>
            new(CliExitCode.Usage, string.Format(Loc.S("CliErrNotFound"), Loc.S(kindKey), id));
        static CliException NoResult(string kindKey, int id) =>
            new(CliExitCode.Failed, string.Format(Loc.S("CliErrNoResult"), Loc.S(kindKey), id));
    }
}
