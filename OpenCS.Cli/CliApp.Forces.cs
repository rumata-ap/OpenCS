using System.CommandLine;
using CScore;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>Наборы усилий стержней: импорт таблицы (CSV/XLSX/DOCX, HTML ЛИРА-САПР) и выделение строк по номерам КЭ.</summary>
sealed partial class CliApp
{
    readonly Argument<string> _forceFile = new("file");
    readonly Option<string> _axes = new("--axes") { DefaultValueFactory = _ => "auto" };
    readonly Option<string> _units = new("--units") { DefaultValueFactory = _ => "kN" };
    readonly Option<string?> _sheet = new("--sheet");
    readonly Option<string> _liraMode = new("--lira-mode") { DefaultValueFactory = _ => "rsu" };
    readonly Option<string> _set = new("--set") { Required = true };

    void DescribeForceOptions()
    {
        _forceFile.Description = Loc.S("CliArgForceFile");
        _axes.Description = Loc.S("CliOptAxes");
        _axes.AcceptOnlyFromAmong("auto", "lira", "opencs");
        _units.Description = Loc.S("CliOptUnits");
        _units.AcceptOnlyFromAmong("kN", "kn", "t");
        _sheet.Description = Loc.S("CliOptSheet");
        _liraMode.Description = Loc.S("CliOptLiraMode");
        _liraMode.AcceptOnlyFromAmong("rsu", "rsn", "loadcases");
        _set.Description = Loc.S("CliOptSet");
    }

    Command ImportForcesCommand() => EditCommand("forces", "CliCmdImportForces", ImportForcesAsync,
        _forceFile, _tag, _axes, _units, _elements, _sheet, _liraMode);

    Command SplitForcesCommand() => EditCommand("forces", "CliCmdSplitForces", SplitForcesAsync, _set, _elements, _tag);

    static readonly string[] HtmlExtensions = [".htm", ".html"];

    Task<int> ImportForcesAsync(CliCall call)
    {
        var p = call.Parse;
        string file = p.GetValue(_forceFile)!;
        string path = Path.GetFullPath(file);
        if (!File.Exists(path)) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrFileNotFound"), file));
        string ext = Path.GetExtension(path).ToLowerInvariant();
        var elements = p.GetValue(_elements) is { } list ? ElementList.Parse(list) : null;
        string? tag = p.GetValue(_tag);

        List<ForceSet> sets;
        string axesName;
        IReadOnlyList<string> columns = [];
        var warnings = new List<string>();
        if (HtmlExtensions.Contains(ext))
        {
            var mode = p.GetValue(_liraMode) switch { "rsn" => LiraImportMode.Rsn, "loadcases" => LiraImportMode.LoadCases, _ => LiraImportMode.Rsu };
            var lira = LiraImporter.ImportFile(path, mode);
            if (!lira.Success) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrImport"), lira.Error));
            warnings.AddRange(lira.Warnings);
            sets = [];
            foreach (var set in lira.ForceSets)
            {
                if (set.Kind != "bar")
                {
                    warnings.Add(string.Format(Loc.S("CliImportShellSkipped"), set.Tag));
                    continue;
                }
                if (elements != null) set.Items.RemoveAll(i => i.SourceElementNum is not int e || !elements.Contains(e));
                if (set.Items.Count > 0) sets.Add(set);
            }
            if (sets.Count == 0)
                throw new CliException(CliExitCode.Usage, elements != null
                    ? string.Format(Loc.S("CliErrNoRowsForElements"), ElementList.Format(elements))
                    : string.Format(Loc.S("CliErrImport"), "—"));
            if (tag != null)
                foreach (var set in sets) set.Tag = sets.Count == 1 ? tag : $"{tag}: {set.Tag}";
            axesName = "lira";
        }
        else if (ForceTableReader.Extensions.Contains(ext))
        {
            var options = new ForceTableOptions
            {
                Axes = p.GetValue(_axes) switch { "lira" => ForceTableAxes.Lira, "opencs" => ForceTableAxes.OpenCs, _ => ForceTableAxes.Auto },
                DefaultScale = p.GetValue(_units) == "t" ? 9.80665 : 1,
                Elements = elements,
            };
            var table = ForceTableImporter.ImportFile(path, options, p.GetValue(_sheet));
            if (!table.Success) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrImport"), table.Error));
            warnings.AddRange(table.Warnings);
            axesName = table.Axes == ForceTableAxes.Lira ? "lira" : "opencs";
            columns = table.Columns;
            var set = table.Set!;
            if (tag != null) set.Tag = tag;
            set.Description = string.Format(Loc.S("CliImportDescription"), Path.GetFileName(path), axesName);
            sets = [set];
        }
        else
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrImportFormat"), Path.GetFileName(path)));

        return Edit(call, session =>
        {
            var db = session.Context.Db;
            int num = NextNum(db.ForceSets, f => f.Num);
            foreach (var set in sets)
            {
                set.Num = num++;
                db.SaveForceSet(set);
            }
            ReportImport(call, sets, axesName, columns, warnings);
        });
    }

    static void ReportImport(CliCall call, List<ForceSet> sets, string axesName, IReadOnlyList<string> columns,
        List<string> warnings)
    {
        foreach (string w in warnings) call.Log.Warning(w);
        if (call.Json)
            CliOutput.WriteJson(call.Out, new
            {
                Sets = sets.Select(SetSummary).ToList(), Axes = axesName, Columns = columns
            });
        else
        {
            foreach (var set in sets)
                call.Out.WriteLine(string.Format(Loc.S("CliImportedSet"), set.Id, set.Tag, set.Items.Count));
            if (columns.Count > 0)
                call.Out.WriteLine(string.Format(Loc.S("CliImportColumns"), axesName, string.Join("; ", columns)));
        }
    }

    static object SetSummary(ForceSet set) => new
    {
        set.Id, set.Num, set.Tag, Rows = set.Items.Count,
        Elements = ElementList.Format(set.Items.Select(i => i.SourceElementNum).OfType<int>())
    };

    Task<int> SplitForcesAsync(CliCall call)
    {
        var p = call.Parse;
        string elementsText = p.GetValue(_elements)
            ?? throw new CliException(CliExitCode.Usage, Loc.S("CliErrElementsRequired"));
        var elements = ElementList.Parse(elementsText);

        return Edit(call, session => SplitForces(call, session.Context.Db, elements));
    }

    void SplitForces(CliCall call, DatabaseService db, IReadOnlySet<int> elements)
    {
        var p = call.Parse;
        var source = CliRefs.Resolve(db.ForceSets, p.GetValue(_set)!, f => f.Id, f => f.Tag, "CliKindForceSet");
        if (source.Kind != "bar")
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrSetNotBar"), source.Tag));
        string list = ElementList.Format(elements);
        var set = new ForceSet
        {
            Num = NextNum(db.ForceSets, f => f.Num), Kind = "bar",
            Tag = p.GetValue(_tag) ?? $"{source.Tag} — КЭ {list}",
            Description = string.Format(Loc.S("CliSplitDescription"), source.Tag, list),
            SourceType = source.SourceType,
        };
        foreach (var i in source.Items.Where(i => i.SourceElementNum is int e && elements.Contains(e)))
            set.Items.Add(new LoadItem
            {
                Num = set.Items.Count + 1, Label = i.Label, N = i.N, Mx = i.Mx, My = i.My, Vx = i.Vx, Vy = i.Vy, T = i.T,
                SourceElementNum = i.SourceElementNum, SourceSectionNum = i.SourceSectionNum
            });
        if (set.Items.Count == 0)
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrNoRowsForElements"), list));
        db.SaveForceSet(set);

        if (call.Json) CliOutput.WriteJson(call.Out, SetSummary(set));
        else call.Out.WriteLine(string.Format(Loc.S("CliImportedSet"), set.Id, set.Tag, set.Items.Count));
    }
}
