using System.CommandLine;
using System.Text;
using CScore.Fem;
using CScore.Import;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>
/// Импорт расчётных схем ЛИРЫ и SCAD — те же службы, что у меню «Импорт» (<see cref="FemSchemaImport"/>). Без COM и
/// DLL (везде): текст ЛИРЫ, .lir (разборщик <see cref="LiraFileParser"/>), CSV ЛИРЫ, текст SCAD. Только Windows: схема,
/// открытая в запущенной ЛИРЕ (<c>--lira-api</c>), и проект SCAD .SPR (SCADAPIX.dll); в других ОС — понятный отказ.
/// </summary>
sealed partial class CliApp
{
    readonly Argument<string?> _schemaFile = new("file") { Arity = ArgumentArity.ZeroOrOne };
    readonly Option<string> _schemaFormat = new("--format") { DefaultValueFactory = _ => "auto" };
    readonly Option<bool> _liraApi = new("--lira-api");
    readonly Option<bool> _designLoads = new("--design-loads");
    readonly Option<string?> _csvElements = new("--elements-file");
    readonly Option<string?> _csvBarStiffness = new("--bar-stiffness");
    readonly Option<string?> _csvPlateStiffness = new("--plate-stiffness");
    readonly Option<string?> _scadDll = new("--scad-dll");
    readonly Option<bool> _noOutputAxes = new("--no-output-axes");
    readonly Option<bool> _noConcreteGroups = new("--no-concrete-groups");
    readonly Option<bool> _noSteelGroups = new("--no-steel-groups");

    /// <summary>Проверка ОС для отказа COM-форматов; тесты подменяют, чтобы проверить отказ «не Windows».</summary>
    internal static Func<bool> ComAvailable { get; set; } = OperatingSystem.IsWindows;

    static readonly string[] SchemaFormats = ["auto", "lira-text", "lir", "lira-csv", "scad-txt", "scad-spr"];

    void DescribeImportOptions()
    {
        _schemaFile.Description = Loc.S("CliArgSchemaFile");
        _schemaFormat.Description = Loc.S("CliOptSchemaFormat");
        _schemaFormat.AcceptOnlyFromAmong(SchemaFormats);
        _liraApi.Description = Loc.S("CliOptLiraApi");
        _designLoads.Description = Loc.S("CliOptDesignLoads");
        _csvElements.Description = Loc.S("CliOptCsvElements");
        _csvBarStiffness.Description = Loc.S("CliOptCsvBarStiffness");
        _csvPlateStiffness.Description = Loc.S("CliOptCsvPlateStiffness");
        _scadDll.Description = Loc.S("CliOptScadDll");
        _noOutputAxes.Description = Loc.S("CliOptNoOutputAxes");
        _noConcreteGroups.Description = Loc.S("CliOptNoConcreteGroups");
        _noSteelGroups.Description = Loc.S("CliOptNoSteelGroups");
    }

    Command ImportSchemaCommand() => EditCommand("schema", "CliCmdImportSchema", ImportSchemaAsync,
        _schemaFile, _tag, _schemaFormat, _liraApi, _csvElements, _csvBarStiffness, _csvPlateStiffness, _scadDll,
        _noOutputAxes, _noConcreteGroups, _noSteelGroups, _designLoads);

    /// <summary>Итог импорта для вывода: схема, ГУ, ΣF, предупреждение о нормативных нагрузках.</summary>
    sealed record SchemaImportOutcome(FemSchema Schema, string Done, FemBoundarySummary? Boundary, FemLoadTransferOutcome? Loads);

    Task<int> ImportSchemaAsync(CliCall call)
    {
        var p = call.Parse;
        bool api = p.GetValue(_liraApi);
        string? file = p.GetValue(_schemaFile);
        if (api == (file != null))
            throw new CliException(CliExitCode.Usage, Loc.S(api ? "CliErrSchemaFileWithApi" : "CliErrSchemaFileRequired"));
        string? path = file == null ? null : Path.GetFullPath(file);
        if (path != null && !File.Exists(path))
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrFileNotFound"), file));
        string format = api ? "lira-api" : ResolveSchemaFormat(p.GetValue(_schemaFormat)!, path!);
        if (format is "lira-api" or "scad-spr" && !ComAvailable())
            throw new CliException(CliExitCode.Usage, Loc.S(format == "lira-api" ? "CliErrLiraApiWindowsOnly" : "CliErrScadSprWindowsOnly"));
        string? tag = p.GetValue(_tag);

        return Edit(call, session =>
        {
            var ctx = session.Context;
            var import = new FemSchemaImport(ctx.Db, call.Log, ctx.Db.LoadLiraImportSettings() ?? LiraImportSettings.Default,
                ctx.CalcSettings);
            var outcome = format switch
            {
                "lira-text" => ImportLiraText(import, path!, tag),
                "lir" => ImportLir(import, path!, tag, call),
                "lira-csv" => MeshOnly(import, ReadLiraCsv(p, path!), path!, tag),
                "scad-txt" => ImportScadText(import, path!, tag, call),
                "scad-spr" when OperatingSystem.IsWindows() => ImportScadSpr(import, ctx.Db, path!, call),
                "lira-api" when OperatingSystem.IsWindows() => ImportLiraApi(import),
                _ => throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrImportFormat"), Path.GetFileName(path ?? "")))
            };
            if (tag != null && outcome.Schema.Tag != tag && format is "scad-spr" or "lira-api")
            {
                outcome.Schema.Tag = tag;
                ctx.Db.SaveFemSchema(outcome.Schema);
            }
            bool switched = SwitchToDesignLoads(call, ctx, outcome.Loads);
            ReportSchemaImport(call, ctx.Db, outcome, switched);
        });
    }

    /// <summary>
    /// <c>--design-loads</c>: нагрузки источника расчётные, а в настройках нормативные — настройка переключается на
    /// «расчётные» (как ответ «Да» в GUI), «авто»-слагаемые комбинаций схем пересчитываются.
    /// </summary>
    bool SwitchToDesignLoads(CliCall call, HeadlessProjectContext ctx, FemLoadTransferOutcome? loads)
    {
        if (loads?.AskDesignLoadValues != true || !call.Parse.GetValue(_designLoads)) return false;
        ctx.CalcSettings.Sp20LoadValueKind = "design";
        ctx.Db.SaveCalcSettings(ctx.CalcSettings);
        call.Log.Info(Loc.S("ImportDesignLoadValuesSwitched"));
        Model.SettingsKind.RecomputeAutoCombinations(ctx);
        return true;
    }

    /// <summary>Формат по расширению; <c>.txt</c> — по содержимому (текст SCAD начинается с «(0;Version=»).</summary>
    static string ResolveSchemaFormat(string format, string path)
    {
        if (format != "auto") return format;
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".lir" => "lir",
            ".spr" => "scad-spr",
            ".csv" => "lira-csv",
            ".txt" => LooksLikeScadText(path) ? "scad-txt" : "lira-text",
            _ => throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrSchemaFormatUnknown"), Path.GetFileName(path),
                string.Join(", ", SchemaFormats.Skip(1))))
        };
    }

    static bool LooksLikeScadText(string path)
    {
        var buffer = new char[4096];
        using var reader = new StreamReader(path, Encoding.Latin1);
        int n = reader.Read(buffer, 0, buffer.Length);
        string head = new string(buffer, 0, n).Replace(" ", "");
        return head.Contains("(0;Version=", StringComparison.Ordinal);
    }

    static SchemaImportOutcome ImportLiraText(FemSchemaImport import, string path, string? tag)
    {
        var txt = import.ReadLiraTextAsync(path, InlineImportWork.Instance).GetAwaiter().GetResult()
            ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrImport"), Path.GetFileName(path)));
        try
        {
            var (schema, done, boundary, loads) = import.ImportLiraText(txt, path, tag, InlineImportWork.Instance)
                .GetAwaiter().GetResult();
            return new(schema, done, boundary, loads);
        }
        catch (InvalidDataException ex) { throw new CliException(CliExitCode.Usage, ex.Message); }
    }

    static SchemaImportOutcome MeshOnly(FemSchemaImport import, LiraSchemaData raw, string path, string? tag)
    {
        var (schema, done) = import.ImportLiraMeshOnly(raw, tag ?? Path.GetFileNameWithoutExtension(path), null);
        return new(schema, done, null, null);
    }

    /// <summary>
    /// .lir разборщиком OpenCS (без ЛИРЫ, в любой ОС). Формат разобран не полностью — в журнал прямо пишется, что
    /// прочитано и что нет (пропущенные КЭ, закрепления, нагрузки), и чем восполнить: текстовым файлом ЛИРЫ.
    /// </summary>
    static SchemaImportOutcome ImportLir(FemSchemaImport import, string path, string? tag, CliCall call)
    {
        LiraSchemaData raw;
        try { raw = LiraFileParser.Parse(path, LiraBinaryExtrasMode.Stiffness); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or EndOfStreamException or ArgumentException)
        {
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrLirRead"), Path.GetFileName(path), ex.Message));
        }
        foreach (var note in raw.BinaryNotes) call.Log.Warning(note);
        int skipped = raw.Elements.Count(e => e.NodeIds.Length is not (2 or 3 or 4));
        if (skipped > 0) call.Log.Warning(string.Format(Loc.S("CliLirSkippedElements"), skipped));
        bool supports = raw.Nodes.Any(n => (n.DofMask & FemBoundaryDofs.All) != 0);
        var outcome = MeshOnly(import, raw, path, tag);
        call.Log.Warning(string.Format(Loc.S(supports ? "CliLirPartial" : "CliLirPartialNoSupports"),
            Path.GetFileNameWithoutExtension(path)));
        return outcome;
    }

    LiraSchemaData ReadLiraCsv(ParseResult p, string nodesPath)
    {
        string elements = p.GetValue(_csvElements) ?? throw new CliException(CliExitCode.Usage, Loc.S("CliErrCsvElementsRequired"));
        string? Existing(string? f) => f == null ? null
            : File.Exists(f) ? Path.GetFullPath(f) : throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrFileNotFound"), f));
        return LiraCsvSchemaParser.Parse(nodesPath, Existing(elements)!, Existing(p.GetValue(_csvBarStiffness)),
            Existing(p.GetValue(_csvPlateStiffness)));
    }

    static SchemaImportOutcome ImportScadText(FemSchemaImport import, string path, string? tag, CliCall call)
    {
        var parsed = ScadTextParser.Parse(path);
        if (!parsed.Success)
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrImport"), parsed.Error ?? Loc.S("ImportScadFailed")));
        foreach (var w in parsed.Warnings) call.Log.Warning(w);
        var (schema, done) = import.ImportScadText(parsed.Data!, tag ?? Path.GetFileNameWithoutExtension(path), null);
        return new(schema, done, null, null);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    SchemaImportOutcome ImportScadSpr(FemSchemaImport import, DatabaseService db, string spr, CliCall call)
    {
        string dll = ResolveScadDll(call, db.LoadScadApiSettings());
        var options = new Services.Scad.ScadReadOptions(!call.Parse.GetValue(_noOutputAxes), ConcreteGroups: true);
        Services.Scad.ScadApiReadResult read;
        Services.Scad.ScadApiNative.Gate.Wait(call.Token);
        try
        {
            var native = Services.Scad.ScadApiNative.Load(dll);
            using var session = new Services.Scad.ScadApiSession(native);
            session.Open(spr);
            read = Services.Scad.ScadApiReader.Read(session, options, null, call.Token);
        }
        catch (Services.Scad.ScadApiException ex) { throw new CliException(CliExitCode.Failed, ex.Format(Loc.S)); }
        finally { Services.Scad.ScadApiNative.Gate.Release(); }

        foreach (var (type, count) in read.SkippedByType.OrderBy(kv => kv.Key))
            call.Log.Warning(string.Format(Loc.S("ScadApiSkippedType"), type, count));
        if (read.BarStiffnessesWithoutShape.Count > 0)
            call.Log.Warning(string.Format(Loc.S("ScadApiBarsWithoutShape"), string.Join(", ", read.BarStiffnessesWithoutShape)));
        if (read.DegenerateAxisElements > 0)
            call.Log.Warning(string.Format(Loc.S("ScadApiDegenerateAxes"), read.DegenerateAxisElements));
        var (schema, done, boundary, loads) = import.ImportScadApi(read.Data, spr, new FemSchemaImport.ScadApiImportOptions(
            !call.Parse.GetValue(_noConcreteGroups), !call.Parse.GetValue(_noSteelGroups), dll));
        return new(schema, done, boundary, loads);
    }

    /// <summary>Каталог SCADAPIX.dll: <c>--scad-dll</c>, иначе из настроек проекта, иначе поиском установки SCAD.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    string ResolveScadDll(CliCall call, ScadApiSettings settings)
    {
        string? dll = call.Parse.GetValue(_scadDll)
            ?? (Services.Scad.ScadInstallLocator.ContainsDll(settings.DllDirectory) ? settings.DllDirectory
                : Services.Scad.ScadInstallLocator.FindDllDirectory());
        return dll != null && Services.Scad.ScadInstallLocator.ContainsDll(dll)
            ? dll : throw new CliException(CliExitCode.Usage, Loc.S("CliErrScadDllNotFound"));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static SchemaImportOutcome ImportLiraApi(FemSchemaImport import)
    {
        int? version = null;
        string? title = null, lirPath = null;
        double tonToKn = import.Lira.TonToKnFactor;
        LiraSchemaData raw;
        try
        {
            raw = RunSta(() =>
            {
                var data = LiraApiSchemaReader.Read(out version, out title, tonToKn);
                try { lirPath = LiraApiSchemaReader.ReadDocumentInfo().PathName; }
                catch (Exception) { }  // путь .lir — только для проверки свежести текстового файла
                return data;
            });
        }
        catch (Exception ex) when (ex is not CliException)
        {
            throw new CliException(CliExitCode.Failed, string.Format(Loc.S("CliErrLiraApi"), ex.Message.Split('\n')[0].TrimEnd('.', ' ', '\r')));
        }
        var (schema, done, boundary, loads) = import.ImportLiraApi(raw, title, version, lirPath, InlineImportWork.Instance)
            .GetAwaiter().GetResult();
        return new(schema, done, boundary, loads);
    }

    /// <summary>COM ЛИРЫ — в потоке STA (как у окна).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static T RunSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
        return result;
    }

    static void ReportSchemaImport(CliCall call, DatabaseService db, SchemaImportOutcome o, bool designLoadsSwitched = false)
    {
        var schema = o.Schema;
        var elements = db.GetFemMeshElements(schema.Id);
        int nodes = db.GetFemMeshNodes(schema.Id).Count;
        int bars = elements.Count(e => e.ElemType == "beam"), shells = elements.Count(e => e.ElemType == "shell");
        bool normative = o.Loads?.AskDesignLoadValues == true && !designLoadsSwitched;
        if (normative) call.Log.Warning(Loc.S("CliImportLoadValuesNormative"));
        if (call.Json)
        {
            CliOutput.WriteJson(call.Out, new
            {
                Schema = new { schema.Id, schema.Tag, schema.SourceType },
                Nodes = nodes, Bars = bars, Shells = shells, Groups = schema.MemberGroups.Count,
                Boundary = o.Boundary == null ? null : new
                {
                    Rows = o.Boundary.Rows.Select(r => new { r.Kind, r.Unit, r.Transferred, r.NotTransferred, r.Note }),
                    o.Boundary.Messages
                },
                LoadCases = o.Loads?.Totals.Select(t => new { t.Case, t.SourceNum, t.FxKn, t.FyKn, t.FzKn }),
                NormativeLoadValues = normative,
                DesignLoadsSwitched = designLoadsSwitched,
            });
            return;
        }
        call.Out.WriteLine(string.Format(Loc.S("CliImportedSchema"), schema.Id, schema.Tag, nodes, bars, shells,
            schema.MemberGroups.Count));
        if (o.Boundary != null)
        {
            call.Out.WriteLine(Loc.S("CliImportBoundaryHeader"));
            foreach (var r in o.Boundary.Rows)
                call.Out.WriteLine(string.Format(Loc.S("CliImportBoundaryRow"), r.Kind, r.Transferred, r.NotTransferred, r.Unit,
                    string.IsNullOrWhiteSpace(r.Note) ? "" : " — " + r.Note));
        }
        if (o.Loads is { Totals.Count: > 0 } loads)
        {
            call.Out.WriteLine(Loc.S("CliImportLoadsHeader"));
            foreach (var t in loads.Totals)
                call.Out.WriteLine(string.Format(System.Globalization.CultureInfo.CurrentCulture, Loc.S("CliImportLoadRow"),
                    t.Case, t.FxKn, t.FyKn, t.FzKn));
        }
    }
}
