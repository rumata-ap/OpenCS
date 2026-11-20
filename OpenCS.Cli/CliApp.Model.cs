using System.CommandLine;
using OpenCS.Cli.Model;
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
            using var session = call.OpenProject();
            var planned = ModelDocument.Plan(doc!, session.Context, errors);
            errors.ThrowIfAny();
            session.Discard();
            ReportChanges(call, planned, dryRun: true);
            return CliExitCode.Ok;
        }

        IReadOnlyList<ModelChange> applied = [];
        int code = await Edit(call, session =>
        {
            var changes = ModelDocument.Plan(doc!, session.Context, errors);
            errors.ThrowIfAny();
            foreach (var change in changes) change.Commit(session.Context);
            applied = changes;
        });
        ReportChanges(call, applied, dryRun: false);
        return code;
    }

    static void ReportChanges(CliCall call, IReadOnlyList<ModelChange> changes, bool dryRun)
    {
        if (call.Json)
        {
            CliOutput.WriteJson(call.Out, new
            {
                DryRun = dryRun,
                Changes = changes.Select(c => new { c.Kind, c.Key, Action = c.Create ? "create" : "update" })
            });
            return;
        }
        if (changes.Count == 0) call.Out.WriteLine(Loc.S("CliModelNothing"));
        foreach (var c in changes)
            call.Out.WriteLine(string.Format(Loc.S("CliModelChange"),
                Loc.S(c.Create ? "CliModelActionCreate" : "CliModelActionUpdate"), c.Kind, c.Key));
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
