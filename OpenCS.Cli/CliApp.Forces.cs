using System.CommandLine;
using CScore;
using CScore.Import;
using OpenCS.Services;
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
    readonly Option<string> _sp20Sets = new("--sets") { Required = true };
    readonly Option<string> _sp20Mode = new("--mode") { DefaultValueFactory = _ => "both" };
    readonly Option<string> _sp20Group = new("--group") { DefaultValueFactory = _ => "C" };
    readonly Option<bool> _sp20Envelope = new("--envelope");
    readonly Option<bool> _sp20Cases = new("--cases");
    readonly Option<string?> _sp20Name = new("--name");

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
        _sp20Sets.Description = Loc.S("CliOptSp20Sets");
        _sp20Mode.Description = Loc.S("CliOptSp20Mode");
        _sp20Mode.AcceptOnlyFromAmong("fundamental", "accidental", "both");
        _sp20Group.Description = Loc.S("CliOptSp20Group");
        _sp20Group.AcceptOnlyFromAmong(ForceSetAuthoring.Sp20Groups);
        _sp20Envelope.Description = Loc.S("CliOptSp20Envelope");
        _sp20Cases.Description = Loc.S("CliOptSp20Cases");
        _sp20Name.Description = Loc.S("CliOptSp20Name");
    }

    Command CombineSp20Command() => EditCommand("sp20", "CliCmdCombineSp20", CombineSp20Async,
        _sp20Sets, _sp20Mode, _sp20Group, _sp20Envelope, _sp20Cases, _sp20Name);

    /// <summary>
    /// Сочетания СП 20 по наборам проекта — как диалог «Сочетания СП20»: огибающая и/или список комбинаций основного
    /// (группа C/CL/N/NL) и/или особого сочетания; без --envelope и --cases — оба. Наборы — по имени или Id через «;».
    /// </summary>
    Task<int> CombineSp20Async(CliCall call)
    {
        var p = call.Parse;
        var refs = p.GetValue(_sp20Sets)!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (refs.Length == 0) throw new CliException(CliExitCode.Usage, Loc.S("Sp20ErrNoSets"));
        bool envelope = p.GetValue(_sp20Envelope), cases = p.GetValue(_sp20Cases);
        if (!envelope && !cases) envelope = cases = true;
        var mode = p.GetValue(_sp20Mode) switch
        {
            "fundamental" => ForceSetAuthoring.Sp20Mode.Fundamental,
            "accidental" => ForceSetAuthoring.Sp20Mode.Accidental,
            _ => ForceSetAuthoring.Sp20Mode.Both
        };
        return Edit(call, session =>
        {
            var ctx = session.Context;
            var sets = refs.Select(r => CliRefs.Resolve(ctx.Db.ForceSets, r, f => f.Id, f => f.Tag, "CliKindForceSet"))
                           .Distinct().ToList();
            var request = new ForceSetAuthoring.Sp20Request(sets, mode, p.GetValue(_sp20Group)!, envelope, cases,
                p.GetValue(_sp20Name), ctx.CalcSettings.ToSp20LoadValueKind(), ctx.CalcSettings.ToSp20GammaDefaults());
            var (created, warnings, errors) = ForceSetAuthoring.CombineSp20(ctx.Db, request);
            if (errors.Count > 0) throw new CliException(CliExitCode.Usage, string.Join(Environment.NewLine, errors));
            foreach (string w in warnings) call.Log.Warning(w);
            if (call.Json)
                CliOutput.WriteJson(call.Out, new { Sets = created.Select(s => new { s.Id, s.Num, s.Tag, Rows = SP20RowCount(s) }).ToList() });
            else
                foreach (var s in created)
                    call.Out.WriteLine(string.Format(Loc.S("CliImportedSet"), s.Id, s.Tag, SP20RowCount(s)));
        });
    }

    static int SP20RowCount(ForceSet s) => s.Kind == "shell" ? s.ShellItems.Count : s.Items.Count;

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
