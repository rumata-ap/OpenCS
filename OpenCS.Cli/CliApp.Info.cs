using CScore;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Cli;

sealed partial class CliApp
{
    Task<int> InfoAsync(CliCall call)
    {
        using var session = call.OpenProject();
        var ctx = session.Context;
        var db = ctx.Db;
        int analyses = ctx.FemSchemas.Sum(s => db.GetFemAnalyses(s.Id).Count);
        var counts = new (string Key, string Json, int Value)[]
        {
            ("CliInfoMaterials", "materials", ctx.Materials.Count),
            ("CliInfoDiagrams", "diagrams", ctx.Diagrams.Count),
            ("CliInfoSections", "crossSections", ctx.CrossSections.Count),
            ("CliInfoPlates", "plateSections", ctx.PlateSections.Count),
            ("CliInfoFireSections", "fireSections", ctx.FireSections.Count),
            ("CliInfoForceSets", "forceSets", ctx.ForceSets.Count),
            ("CliInfoTasks", "calcTasks", ctx.CalcTasks.Count),
            ("CliInfoResults", "calcResults", db.CalcResults.Count),
            ("CliInfoSchemas", "femSchemas", ctx.FemSchemas.Count),
            ("CliInfoChecks", "femChecks", db.FemChecks.Count),
            ("CliInfoAnalyses", "femAnalyses", analyses),
        };

        if (call.Json)
        {
            var json = new Dictionary<string, object>
            {
                ["project"] = session.SourcePath,
                ["schemaVersion"] = session.FileSchemaVersion,
                ["programSchemaVersion"] = DatabaseService.SchemaVersion,
            };
            foreach (var c in counts) json[c.Json] = c.Value;
            CliOutput.WriteJson(call.Out, json);
        }
        else
        {
            CliOutput.WritePairs(call.Out,
                new[]
                {
                    (Loc.S("CliInfoProject"), session.SourcePath),
                    (Loc.S("CliInfoSchema"),
                        $"{session.FileSchemaVersion} ({Loc.S("CliInfoProgramSchema")}: {DatabaseService.SchemaVersion})"),
                }.Concat(counts.Select(c => (Loc.S(c.Key), c.Value.ToString()))));
        }
        return Task.FromResult(CliExitCode.Ok);
    }

    Task<int> ListTasksAsync(CliCall call)
    {
        using var session = call.OpenProject();
        var ctx = session.Context;
        var rows = ctx.CalcTasks.OrderBy(t => t.Num).ThenBy(t => t.Id).Select(t =>
        {
            var last = ctx.Db.CalcResults.Where(r => r.TaskId == t.Id).MaxBy(r => r.Id);
            return new
            {
                t.Id, t.Num, t.Tag, t.Kind,
                Section = SectionTag(ctx, t),
                Result = last == null ? null : new { last.Id, last.Status, last.Created }
            };
        }).ToList();

        if (call.Json) CliOutput.WriteJson(call.Out, rows);
        else
            CliOutput.WriteTable(call.Out,
                [Loc.S("CliColId"), Loc.S("CliColNum"), Loc.S("CliColTag"), Loc.S("CliColKind"),
                 Loc.S("CliColSection"), Loc.S("CliColResult")],
                rows.Select(r => new[]
                {
                    r.Id.ToString(), r.Num.ToString(), r.Tag, r.Kind, r.Section ?? Loc.S("CliNone"),
                    r.Result == null ? Loc.S("CliNone") : $"#{r.Result.Id} {r.Result.Status} {r.Result.Created}"
                }));
        return Task.FromResult(CliExitCode.Ok);
    }

    /// <summary>Имя сечения задачи: оболочечные задачи ссылаются на сечение плиты.</summary>
    static string? SectionTag(OpenCS.Services.IProjectContext ctx, CalcTask task) =>
        task.Kind.StartsWith("shell_", StringComparison.Ordinal)
            ? ctx.PlateSections.FirstOrDefault(s => s.Id == task.SectionId)?.Tag
            : ctx.CrossSections.FirstOrDefault(s => s.Id == task.SectionId)?.Tag;

    Task<int> ListChecksAsync(CliCall call)
    {
        using var session = call.OpenProject();
        var ctx = session.Context;
        var rows = ctx.Db.FemChecks.OrderBy(c => c.SchemaId).ThenBy(c => c.Id).Select(c =>
        {
            var last = ctx.Db.GetCalcResultHeaderByFemCheck(c.Id);
            return new
            {
                c.Id, c.SchemaId,
                Schema = ctx.FemSchemas.FirstOrDefault(s => s.Id == c.SchemaId)?.Tag,
                Target = c.TargetsElement
                    ? string.Format(Loc.S("CliTargetElement"), c.ElementId)
                    : string.Format(Loc.S("CliTargetMember"), c.MemberId),
                c.NormCode, Tag = c.DisplayTag,
                Result = last == null ? null : new { last.Id, last.Status, last.Created }
            };
        }).ToList();

        if (call.Json) CliOutput.WriteJson(call.Out, rows);
        else
            CliOutput.WriteTable(call.Out,
                [Loc.S("CliColId"), Loc.S("CliColSchema"), Loc.S("CliColTarget"), Loc.S("CliColNorm"),
                 Loc.S("CliColTag"), Loc.S("CliColResult")],
                rows.Select(r => new[]
                {
                    r.Id.ToString(), r.Schema ?? $"#{r.SchemaId}", r.Target, r.NormCode, r.Tag,
                    r.Result == null ? Loc.S("CliNone") : $"#{r.Result.Id} {r.Result.Status} {r.Result.Created}"
                }));
        return Task.FromResult(CliExitCode.Ok);
    }

    Task<int> ListAnalysesAsync(CliCall call)
    {
        using var session = call.OpenProject();
        var rows = AllAnalyses(session.Context).Select(x => new
        {
            x.Analysis.Id, x.Analysis.SchemaId, Schema = x.Schema.Tag, x.Analysis.Tag, x.Analysis.Kind,
            x.Analysis.Status, x.Analysis.ResultId
        }).ToList();

        if (call.Json) CliOutput.WriteJson(call.Out, rows);
        else
            CliOutput.WriteTable(call.Out,
                [Loc.S("CliColId"), Loc.S("CliColSchema"), Loc.S("CliColTag"), Loc.S("CliColKind"),
                 Loc.S("CliColStatus"), Loc.S("CliColResult")],
                rows.Select(r => new[]
                {
                    r.Id.ToString(), r.Schema, r.Tag, r.Kind, r.Status,
                    r.ResultId is { } id ? $"#{id}" : Loc.S("CliNone")
                }));
        return Task.FromResult(CliExitCode.Ok);
    }

    /// <summary>Постановки расчёта всех схем проекта (в БД они читаются по схеме).</summary>
    static IEnumerable<(FemSchema Schema, FemAnalysis Analysis)> AllAnalyses(OpenCS.Services.IProjectContext ctx) =>
        ctx.FemSchemas.SelectMany(s => ctx.Db.GetFemAnalyses(s.Id).Select(a => (s, a)));

    Task<int> ListResultsAsync(CliCall call)
    {
        int? taskId = call.Parse.GetValue(_task);
        using var session = call.OpenProject();
        var rows = session.Context.Db.CalcResults
            .Where(r => taskId == null || r.TaskId == taskId)
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.TaskId, r.TaskTag, r.TaskKind, r.Status, r.Created })
            .ToList();

        if (call.Json) CliOutput.WriteJson(call.Out, rows);
        else
            CliOutput.WriteTable(call.Out,
                [Loc.S("CliColId"), Loc.S("CliColTask"), Loc.S("CliColTag"), Loc.S("CliColKind"),
                 Loc.S("CliColStatus"), Loc.S("CliColCreated")],
                rows.Select(r => new[]
                {
                    r.Id.ToString(), r.TaskId.ToString(), r.TaskTag, r.TaskKind, r.Status, r.Created
                }));
        return Task.FromResult(CliExitCode.Ok);
    }
}
