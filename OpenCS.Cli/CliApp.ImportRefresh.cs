using System.CommandLine;
using CScore.Fem;
using CScore.Import;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>
/// После импорта схемы: «Дочитать граничные условия» / «Дочитать нагрузки» (<c>import boundary</c>, <c>import loads</c>)
/// и «КонЭ из кБ ЛИРЫ» (<c>convert blocks</c>) — те же службы, что у окна. Без COM: ЛИРА — по текстовому файлу схемы,
/// SCAD — по вложению, записанному импортом .SPR.
/// </summary>
sealed partial class CliApp
{
    readonly Option<string> _refreshSchema = new("--schema") { Required = true };
    readonly Argument<string?> _refreshFile = new("file") { Arity = ArgumentArity.ZeroOrOne };
    readonly Option<string> _blocks = new("--blocks") { DefaultValueFactory = _ => "all" };
    readonly Option<string?> _blockType = new("--type");
    readonly Option<string?> _blockFloor = new("--floor");

    void DescribeRefreshOptions()
    {
        _refreshSchema.Description = Loc.S("CliOptRefreshSchema");
        _refreshFile.Description = Loc.S("CliArgLiraTextFile");
        _blocks.Description = Loc.S("CliOptBlocks");
        _blockType.Description = Loc.S("CliOptBlockType");
        _blockFloor.Description = Loc.S("CliOptBlockFloor");
    }

    Command ImportBoundaryCommand() => EditCommand("boundary", "CliCmdImportBoundary", c => RefreshFromSourceAsync(c, boundary: true),
        _refreshSchema, _refreshFile);

    Command ImportLoadsCommand() => EditCommand("loads", "CliCmdImportLoads", c => RefreshFromSourceAsync(c, boundary: false),
        _refreshSchema, _refreshFile, _designLoads);

    Command ConvertBlocksCommand() => EditCommand("blocks", "CliCmdConvertBlocks", ConvertBlocksAsync,
        _refreshSchema, _blocks, _blockType, _blockFloor);

    /// <summary>ГУ или нагрузки схемы ЛИРЫ (текстовый файл) или SCAD (вложение импорта) — с заменой перенесённых раньше.</summary>
    Task<int> RefreshFromSourceAsync(CliCall call, bool boundary)
    {
        string? file = call.Parse.GetValue(_refreshFile);
        return Edit(call, session =>
        {
            var ctx = session.Context;
            var db = ctx.Db;
            var schema = CliRefs.Resolve(db.FemSchemas, call.Parse.GetValue(_refreshSchema)!, s => s.Id, s => s.Tag, "CliKindSchema");
            var import = new FemSchemaImport(db, call.Log, db.LoadLiraImportSettings() ?? LiraImportSettings.Default, ctx.CalcSettings);
            FemBoundarySummary? summary = null;
            FemLoadTransferOutcome? loads = null;
            switch (schema.SourceType)
            {
                case "lira":
                {
                    string path = LiraTextFor(import, schema, file);
                    var txt = import.ReadLiraTextAsync(path, InlineImportWork.Instance).GetAwaiter().GetResult()
                        ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrImport"), Path.GetFileName(path)));
                    var nodes = db.GetFemMeshNodes(schema.Id);
                    var elements = db.GetFemMeshElements(schema.Id);
                    var match = LiraTextMeshMatch.Compare(txt, nodes, elements);
                    if (!match.Ok) throw new CliException(CliExitCode.Usage, FemSchemaImport.MismatchMessage(schema, path, match));
                    if (boundary)
                        summary = import.TransferLiraBoundary(schema, txt, nodes, elements, FemSchemaImport.ImportedFoundationC1(elements));
                    else
                        loads = import.TransferLiraLoads(schema, txt, nodes, elements, InlineImportWork.Instance).GetAwaiter().GetResult();
                    if (!string.Equals(schema.SourcePath, path, StringComparison.OrdinalIgnoreCase)) db.UpdateFemSchemaSourcePath(schema, path);
                    break;
                }
                case "scad":
                {
                    if (file != null) throw new CliException(CliExitCode.Usage, Loc.S("CliErrScadRefreshNoFile"));
                    var model = import.LoadScadAnalysisModel(schema.Id)
                        ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrScadModelMissing"), schema.Tag));
                    if (boundary && model is not { HasBoundaryV2: true, HasBeds: true, HasRodAxes: true, HasVerticalAxesFix: true })
                        call.Log.Warning(string.Format(Loc.S("CliScadModelOld"), schema.Tag));
                    if (boundary) summary = import.TransferScadBoundary(schema, model);
                    else loads = import.TransferScadLoads(schema, model);
                    break;
                }
                default:
                    throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrRefreshSourceType"), schema.Tag));
            }
            bool switched = !boundary && SwitchToDesignLoads(call, ctx, loads);
            ReportSchemaImport(call, db, new SchemaImportOutcome(schema, "", summary, loads), switched);
        });
    }

    /// <summary>Текстовый файл ЛИРЫ схемы: аргумент, путь прошлого переноса, поиск по имени схемы в каталогах ЛИРЫ.</summary>
    static string LiraTextFor(FemSchemaImport import, FemSchema schema, string? file)
    {
        if (file != null)
            return File.Exists(file) ? Path.GetFullPath(file)
                : throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrFileNotFound"), file));
        if (schema.SourcePath is { } sp && sp.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && File.Exists(sp)) return sp;
        return import.FindLiraText(schema.Tag, import.LiraTextDirectories(), null)
            ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrLiraTextNotFound"), schema.Tag));
    }

    /// <summary>кБ ЛИРЫ → КонЭ: все (по умолчанию) или по номерам, типу, этажу.</summary>
    Task<int> ConvertBlocksAsync(CliCall call)
    {
        var p = call.Parse;
        string blocksArg = p.GetValue(_blocks)!;
        var ids = blocksArg.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : ElementList.Parse(blocksArg);
        string? type = p.GetValue(_blockType), floor = p.GetValue(_blockFloor);
        return Edit(call, session =>
        {
            var db = session.Context.Db;
            var schema = CliRefs.Resolve(db.FemSchemas, p.GetValue(_refreshSchema)!, s => s.Id, s => s.Tag, "CliKindSchema");
            var all = db.GetLiraBlocks(schema.Id);
            if (all.Count == 0) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrNoLiraBlocks"), schema.Tag));
            var selected = all.Where(b => (ids == null || ids.Contains(b.Id))
                                       && (type == null || string.Equals(b.Type, type, StringComparison.OrdinalIgnoreCase))
                                       && (floor == null || string.Equals(b.Floor, floor, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (selected.Count == 0)
                throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrNoBlocksSelected"), all.Count,
                    string.Join(", ", all.Select(b => b.Type).Distinct().Order())));
            string? done;
            try { done = LiraBlockConversion.Convert(db, call.Log, schema, selected); }
            catch (InvalidOperationException ex) { throw new CliException(CliExitCode.Usage, ex.Message); }
            if (done == null) throw new CliException(CliExitCode.Failed, Loc.S("CliErrBlocksNotConverted"));
            var members = db.GetFemMembers(schema.Id);
            if (call.Json)
                CliOutput.WriteJson(call.Out, new
                {
                    Schema = new { schema.Id, schema.Tag },
                    Blocks = selected.Select(b => new { b.Id, b.Tag }),
                    Members = members.Count,
                });
            else
                call.Out.WriteLine(done);
        });
    }
}
