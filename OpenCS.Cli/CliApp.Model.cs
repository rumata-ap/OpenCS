using System.CommandLine;
using OpenCS.Cli.Model;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>Документ opencs-model: выгрузка исходных данных, применение (создание/обновление), JSON-схемы видов.</summary>
sealed partial class CliApp
{
    readonly Argument<string> _modelFile = new("file");
    readonly Option<string> _modelTo = new("--to") { Required = true };
    readonly Option<string?> _modelOnly = new("--only");
    readonly Option<bool> _dryRun = new("--dry-run");
    readonly Argument<string> _schemaKind = new("kind");

    void DescribeModelOptions()
    {
        _modelFile.Description = Loc.S("CliArgModelFile");
        _modelTo.Description = Loc.S("CliOptModelTo");
        _modelOnly.Description = Loc.S("CliOptModelOnly");
        _dryRun.Description = Loc.S("CliOptDryRun");
        _schemaKind.Description = Loc.S("CliArgSchemaKind");
    }

    Command ExportModelCommand()
    {
        var command = new Command("model", Loc.S("CliCmdExportModel")) { _project, _modelTo, _modelOnly };
        command.SetAction((p, ct) => Handle(p, ct, ExportModelAsync));
        return command;
    }

    Command ApplyCommand() => EditCommand("apply", "CliCmdApply", ApplyAsync, _modelFile, _dryRun);

    Command SchemaCommand()
    {
        var command = new Command("schema", Loc.S("CliCmdSchema")) { _schemaKind };
        command.SetAction((p, ct) => Handle(p, ct, SchemaAsync));
        return command;
    }

    static HashSet<string>? ParseOnly(string? only)
    {
        if (string.IsNullOrWhiteSpace(only)) return null;
        var names = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        foreach (var name in names)
            if (ModelDocument.Find(name) == null)
                throw UnknownKind(name);
        return names;
    }

    static CliException UnknownKind(string name) => new(CliExitCode.Usage, string.Format(Loc.S("CliModelErrUnknownKind"),
        name, string.Join(", ", ModelDocument.Kinds.Select(k => k.Name))));

    Task<int> ExportModelAsync(CliCall call)
    {
        var only = ParseOnly(call.Parse.GetValue(_modelOnly));
        string to = Path.GetFullPath(call.Parse.GetValue(_modelTo)!);
        using var session = call.OpenProject();
        var doc = ModelDocument.Write(session.Context, only, w => call.Log.Warning(w));
        string? dir = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(to, doc.ToJsonString(CliOutput.Json));
        if (call.Json) CliOutput.WriteJson(call.Out, new { File = to });
        else call.Out.WriteLine(string.Format(Loc.S("CliModelExported"), to));
        return Task.FromResult(CliExitCode.Ok);
    }

    async Task<int> ApplyAsync(CliCall call)
    {
        var errors = new ModelErrors();
        var doc = ModelDocument.Read(call.Parse.GetValue(_modelFile)!, errors);
        errors.ThrowIfAny();

        if (call.Parse.GetValue(_dryRun))
        {
            // Проба — на рабочей копии проекта (сессия без --in-place), которая отбрасывается: так план знает,
            // какие объекты документа уже совпадают с проектом («без изменений»).
            using var session = call.OpenProject();
            var planned = ModelDocument.Plan(doc!, session.Context, errors);
            errors.ThrowIfAny();
            var tried = CommitAll(planned, session.Context);
            session.Discard();
            ReportChanges(call, tried, dryRun: true);
            return CliExitCode.Ok;
        }

        IReadOnlyList<AppliedChange> applied = [];
        int code = await Edit(call, session =>
        {
            var changes = ModelDocument.Plan(doc!, session.Context, errors);
            errors.ThrowIfAny();
            applied = CommitAll(changes, session.Context);
        });
        ReportChanges(call, applied, dryRun: false);
        return code;
    }

    /// <summary>Изменение плана после записи: <c>Unchanged</c> — выгрузка вида до и после записи совпала.</summary>
    sealed record AppliedChange(ModelChange Change, bool Unchanged);

    /// <summary>
    /// Записывает изменения по порядку плана; у обновлений сравнивает выгрузку своего вида до и после записи —
    /// совпала (объект в документе такой же, как в проекте) → «без изменений».
    /// </summary>
    static List<AppliedChange> CommitAll(IReadOnlyList<ModelChange> changes, HeadlessProjectContext ctx)
    {
        var result = new List<AppliedChange>(changes.Count);
        foreach (var change in changes)
        {
            var kind = change.Create ? null : ModelDocument.Find(change.Kind);
            string? before = kind == null ? null : Snapshot(kind, ctx);
            change.Commit(ctx);
            result.Add(new(change, before != null && before == Snapshot(kind!, ctx)));
        }
        return result;
    }

    static string Snapshot(IModelKind kind, HeadlessProjectContext ctx) => kind.Export(ctx, _ => { }).ToJsonString();

    static void ReportChanges(CliCall call, IReadOnlyList<AppliedChange> changes, bool dryRun)
    {
        static string Action(AppliedChange c) => c.Change.Create ? "create" : c.Unchanged ? "unchanged" : "update";
        if (call.Json)
        {
            CliOutput.WriteJson(call.Out, new
            {
                DryRun = dryRun,
                Changes = changes.Select(c => new { c.Change.Kind, c.Change.Key, Action = Action(c) })
            });
            return;
        }
        if (changes.Count == 0) call.Out.WriteLine(Loc.S("CliModelNothing"));
        foreach (var c in changes)
            call.Out.WriteLine(string.Format(Loc.S("CliModelChange"), Loc.S(Action(c) switch
            {
                "create" => "CliModelActionCreate",
                "unchanged" => "CliModelActionUnchanged",
                _ => "CliModelActionUpdate"
            }), c.Change.Kind, c.Change.Key));
        if (dryRun) call.Out.WriteLine(Loc.S("CliModelDryRun"));
    }

    Task<int> SchemaAsync(CliCall call)
    {
        string name = call.Parse.GetValue(_schemaKind)!;
        var kind = ModelDocument.Find(name) ?? throw UnknownKind(name);
        call.Out.WriteLine(ModelDocument.ReadSchema(kind));
        return Task.FromResult(CliExitCode.Ok);
    }
}
