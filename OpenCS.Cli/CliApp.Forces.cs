using System.CommandLine;
using CScore;
using CScore.Import;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>Наборы усилий стержней: импорт таблицы (CSV/XLSX/DOCX, HTML ЛИРА-САПР) и выделение строк по номерам КЭ.</summary>
sealed partial class CliApp
{
    readonly Argument<string?> _forceFile = new("file") { Arity = ArgumentArity.ZeroOrOne };
    readonly Option<string?> _forceSchema = new("--schema");
    readonly Option<string?> _forceTarget = new("--target");
    readonly Option<string?> _scadXls = new("--scad");
    readonly Option<string?> _forceApi = new("--api");
    readonly Option<string?> _apiKind = new("--kind");
    readonly Option<string?> _rsuGroups = new("--rsu-groups");
    readonly Option<string?> _spr = new("--spr");
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
        _forceSchema.Description = Loc.S("CliOptForceSchema");
        _forceTarget.Description = Loc.S("CliOptForceTarget");
        _scadXls.Description = Loc.S("CliOptScadXls");
        _scadXls.AcceptOnlyFromAmong("loadcases", "rsu", "combinations");
        _forceApi.Description = Loc.S("CliOptForceApi");
        _forceApi.AcceptOnlyFromAmong("lira", "scad");
        _apiKind.Description = Loc.S("CliOptApiKind");
        _apiKind.AcceptOnlyFromAmong("loadcases", "combinations", "rsn", "rsu");
        _rsuGroups.Description = Loc.S("CliOptRsuGroups");
        _spr.Description = Loc.S("CliOptSpr");
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
        _forceFile, _tag, _axes, _units, _elements, _sheet, _liraMode, _forceSchema, _forceTarget, _scadXls, _forceApi, _apiKind,
        _rsuGroups, _spr, _scadDll);

    Command SplitForcesCommand() => EditCommand("forces", "CliCmdSplitForces", SplitForcesAsync, _set, _elements, _tag);

    static readonly string[] HtmlExtensions = [".htm", ".html"];

    Task<int> ImportForcesAsync(CliCall call)
    {
        var p = call.Parse;
        string? api = p.GetValue(_forceApi);
        string? fileArg = p.GetValue(_forceFile);
        if (p.GetValue(_forceTarget) != null && p.GetValue(_forceSchema) == null)
            throw new CliException(CliExitCode.Usage, Loc.S("CliErrTargetNeedsSchema"));
        if (api != null) return ImportApiForces(call, api, fileArg);
        if (fileArg == null) throw new CliException(CliExitCode.Usage, Loc.S("CliErrForceFileRequired"));
        if (p.GetValue(_scadXls) is { } scadMode) return ImportScadXlsForces(call, fileArg, scadMode);
        string file = fileArg;
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
            var (_, target) = ResolveForceTarget(call, db);
            FemForceImport.Save(db, sets, target);
            ReportImport(call, sets, axesName, columns, warnings);
        });
    }

    /// <summary>Схема (<c>--schema</c>, Id или имя) и цель в ней (<c>--target</c>: имя группы КЭ/КонЭ или КонЭ).</summary>
    (CScore.Fem.FemSchema? Schema, CScore.Fem.IFemCheckable? Target) ResolveForceTarget(CliCall call, DatabaseService db)
    {
        if (call.Parse.GetValue(_forceSchema) is not { } schemaRef) return (null, null);
        var schema = CliRefs.Resolve(db.FemSchemas, schemaRef, s => s.Id, s => s.Tag, "CliKindSchema");
        if (call.Parse.GetValue(_forceTarget) is not { } targetRef) return (schema, null);
        var groups = schema.MemberGroups.Where(g => g.Tag == targetRef).ToList();
        var members = db.GetFemMembers(schema.Id).Where(m => m.ElemTag == targetRef).ToList();
        return (groups.Count + members.Count) switch
        {
            1 => (schema, groups.Count == 1 ? groups[0] : members[0]),
            0 => throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrTargetNotFound"), targetRef, schema.Tag)),
            _ => throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrTargetAmbiguous"), targetRef, schema.Tag)),
        };
    }

    LiraImportSettings LiraSettings(DatabaseService db) => db.LoadLiraImportSettings() ?? LiraImportSettings.Default;

    /// <summary>
    /// Усилия SCAD из XLS (<c>--scad loadcases|rsu|combinations</c>): КЭ — <c>--elements</c>, иначе КЭ цели, иначе все;
    /// наборы привязываются к цели <c>--target</c>.
    /// </summary>
    Task<int> ImportScadXlsForces(CliCall call, string file, string mode)
    {
        string path = Path.GetFullPath(file);
        if (!File.Exists(path)) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrFileNotFound"), file));
        var elements = call.Parse.GetValue(_elements) is { } list ? ElementList.Parse(list) : null;
        return Edit(call, session =>
        {
            var db = session.Context.Db;
            var (schema, target) = ResolveForceTarget(call, db);
            var ids = elements ?? (target != null ? FemForceImport.TargetElements(db, target).ToHashSet() : null);
            var options = FemForceImport.ScadOptions(LiraSettings(db));
            options.ElementIds = ids ?? new HashSet<int>();
            options.ImportAllElements = ids == null;
            var import = ScadXlsForceImporter.ImportFile(path, mode switch
            {
                "rsu" => ScadXlsImportMode.Rsu,
                "combinations" => ScadXlsImportMode.Combinations,
                _ => ScadXlsImportMode.LoadCases,
            }, options);
            if (!import.Success)
                throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrImport"),
                    import.Error ?? import.Warning ?? Loc.S("ImportScadForcesNoRows")));
            if (!string.IsNullOrEmpty(import.Warning)) call.Log.Warning(import.Warning);
            if (schema != null) foreach (var s in import.ForceSets) s.SourceSchemaId ??= schema.Id;
            FemForceImport.Save(db, import.ForceSets, target);
            ReportImport(call, import.ForceSets, "scad", [], []);
        });
    }

    /// <summary>
    /// Усилия через API (<c>--api lira|scad</c>, только Windows) на цель схемы: ЛИРА — открытая в ней схема, КЭ цели;
    /// SCAD — проект .SPR схемы (<c>--spr</c> или путь, запомненный импортом), КЭ цели или все.
    /// </summary>
    Task<int> ImportApiForces(CliCall call, string api, string? file)
    {
        if (file != null) throw new CliException(CliExitCode.Usage, Loc.S("CliErrApiNoFile"));
        if (call.Parse.GetValue(_forceSchema) == null) throw new CliException(CliExitCode.Usage, Loc.S("CliErrApiNeedsSchema"));
        if (!ComAvailable())
            throw new CliException(CliExitCode.Usage, Loc.S(api == "lira" ? "CliErrLiraApiWindowsOnly" : "CliErrScadSprWindowsOnly"));
        string kind = call.Parse.GetValue(_apiKind) ?? "loadcases";
        if (api == "lira" && kind == "combinations" || api == "scad" && kind == "rsn")
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrApiKind"), kind, api));
        return Edit(call, session =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var db = session.Context.Db;
            var (schema, target) = ResolveForceTarget(call, db);
            var sets = api == "lira" ? ReadLiraApiForces(db, schema!, target, kind) : ReadScadApiForces(call, db, schema!, target, kind);
            FemForceImport.Save(db, sets, target);
            ReportImport(call, sets, api, [], []);
        });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    List<ForceSet> ReadLiraApiForces(DatabaseService db, CScore.Fem.FemSchema schema, CScore.Fem.IFemCheckable? target, string kind)
    {
        if (target == null) throw new CliException(CliExitCode.Usage, Loc.S("CliErrLiraApiNeedsTarget"));
        var ids = FemForceImport.TargetElements(db, target);
        if (ids.Length == 0) throw new CliException(CliExitCode.Usage, Loc.S("ImportLiraForcesNoElements"));
        var settings = LiraSettings(db);
        try
        {
            return RunSta(() => kind switch
            {
                "rsn" => LiraApiForceImporter.ReadLoadCombinationForces(schema, ids, settings, target.Tag),
                "rsu" => LiraApiForceImporter.ReadDesignCombinationForces(schema, ids, settings, target.Tag),
                _ => LiraApiForceImporter.ReadLoadCaseForces(schema, ids, settings, target.Tag),
            });
        }
        catch (Exception ex) when (ex is not CliException)
        {
            throw new CliException(CliExitCode.Failed, string.Format(Loc.S("CliErrLiraApi"), ex.Message.Split('\n')[0].TrimEnd('.', ' ', '\r')));
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    List<ForceSet> ReadScadApiForces(CliCall call, DatabaseService db, CScore.Fem.FemSchema schema, CScore.Fem.IFemCheckable? target,
        string kind)
    {
        if (schema.SourceType != "scad")
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("ScadForcesNotScadSchema"), schema.Tag));
        string? spr = call.Parse.GetValue(_spr) is { } given ? Path.GetFullPath(given) : schema.SourcePath;
        if (spr == null || !File.Exists(spr))
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrSprNotFound"), schema.Tag));
        if (!string.Equals(spr, schema.SourcePath, StringComparison.OrdinalIgnoreCase)) db.UpdateFemSchemaSourcePath(schema, spr);
        var (targets, notInMesh) = FemForceImport.ScadApiTargets(db, schema, target);
        if (targets.Count == 0) throw new CliException(CliExitCode.Usage, Loc.S("ScadForcesNoElements"));
        var settings = db.LoadScadApiSettings();
        string dll = ResolveScadDll(call, settings);
        string? work = !string.IsNullOrWhiteSpace(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory)
            ? settings.WorkDirectory : Services.Scad.ScadInstallLocator.FindWorkDirectory();
        var readKind = kind switch
        {
            "combinations" => Services.Scad.ScadForceReadKind.Combinations,
            "rsu" => Services.Scad.ScadForceReadKind.Rsu,
            _ => Services.Scad.ScadForceReadKind.LoadCases,
        };
        HashSet<int>? rsu = readKind == Services.Scad.ScadForceReadKind.Rsu ? ParseRsuGroups(call, settings) : null;
        Services.Scad.ScadApiForceReadResult read;
        Services.Scad.ScadApiNative.Gate.Wait(call.Token);
        try
        {
            using var session = new Services.Scad.ScadApiSession(Services.Scad.ScadApiNative.Load(dll));
            session.Open(spr);
            read = Services.Scad.ScadApiForceReader.Read(session, work, targets, readKind, null, call.Token, rsu);
        }
        catch (Services.Scad.ScadApiException ex) { throw new CliException(CliExitCode.Failed, ex.Format(Loc.S)); }
        finally { Services.Scad.ScadApiNative.Gate.Release(); }
        FemForceImport.ReportScadApiRead(call.Log, read, notInMesh);
        return FemForceImport.BuildScadApiSets(read, readKind, schema.Id, target?.Tag ?? schema.Tag,
            FemForceImport.ScadOptions(LiraSettings(db)));
    }

    /// <summary>Группы РСУ SCAD: <c>--rsu-groups C,CL,N,NL</c>, иначе — запомненные окном.</summary>
    HashSet<int> ParseRsuGroups(CliCall call, ScadApiSettings settings)
    {
        if (call.Parse.GetValue(_rsuGroups) is not { } text) return [.. settings.RsuGroups];
        string[] names = ["C", "CL", "N", "NL"];
        var set = new HashSet<int>();
        foreach (var part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int i = Array.FindIndex(names, n => string.Equals(n, part, StringComparison.OrdinalIgnoreCase));
            if (i < 0) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrRsuGroup"), part));
            set.Add(i);
        }
        if (set.Count == 0) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrRsuGroup"), text));
        return set;
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
                call.Out.WriteLine(string.Format(Loc.S("CliImportedSet"), set.Id, set.Tag, SP20RowCount(set)));
            if (columns.Count > 0)
                call.Out.WriteLine(string.Format(Loc.S("CliImportColumns"), axesName, string.Join("; ", columns)));
        }
    }

    static object SetSummary(ForceSet set) => new
    {
        set.Id, set.Num, set.Tag, Rows = SP20RowCount(set), set.Kind,
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
