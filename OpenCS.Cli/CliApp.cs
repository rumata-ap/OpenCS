using System.CommandLine;
using OpenCS.ExternalTools;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>
/// Консоль opencs: разбор аргументов и команды. Вся логика — здесь, <c>Program</c> только передаёт
/// стандартные потоки (тесты вызывают <see cref="RunAsync"/> в процессе со своими писателями).
/// Описания команд берутся из словарей строк, поэтому язык задаётся до построения дерева команд.
/// </summary>
sealed partial class CliApp
{
    readonly TextWriter _out;
    readonly TextWriter _err;

    readonly Option<string> _lang = new("--lang") { DefaultValueFactory = _ => "ru", Recursive = true };
    readonly Option<bool> _json = new("--json") { Recursive = true };
    readonly Option<bool> _quiet = new("--quiet", "-q") { Recursive = true };
    readonly Argument<string> _project = new("project");
    readonly Argument<int[]> _ids = new("ids") { Arity = ArgumentArity.ZeroOrMore };
    readonly Option<bool> _all = new("--all");
    readonly Option<string?> _outFile = new("--out");
    readonly Option<bool> _force = new("--force");
    readonly Option<bool> _inPlace = new("--in-place");
    readonly Option<bool> _yes = new("--yes", "-y");
    readonly Option<bool> _withData = new("--with-data");
    readonly Option<int?> _task = new("--task");
    readonly Option<int?> _check = new("--check");
    readonly Option<int?> _analysis = new("--analysis");
    readonly Argument<int?> _resultId = new("result-id") { Arity = ArgumentArity.ZeroOrOne };
    readonly Option<string?> _output = new("--output", "-o");
    readonly Option<string?> _artifacts = new("--artifacts");

    CliApp(TextWriter stdout, TextWriter stderr)
    {
        _out = stdout;
        _err = stderr;
    }

    /// <summary>Выполняет команду; возвращает код выхода (<see cref="CliExitCode"/>).</summary>
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        bool english = LanguageFromArgs(args) == "en";
        Loc.SetLanguage(english ? Loc.English : Loc.Russian);
        // Справка и ошибки разбора System.CommandLine — на языке UI-культуры (вне этого вызова не утекает).
        System.Globalization.CultureInfo.CurrentUICulture =
            System.Globalization.CultureInfo.GetCultureInfo(english ? "en-US" : "ru-RU");
        // gmsh и OpenSees в консоли — только Python-пакеты (одинаково на Windows и Linux); на время вызова, в его
        // потоке выполнения. OPENCS_EXTERNAL_TOOLS=auto оставляет поиск как в OpenCS (exe, PATH).
        using var tools = string.Equals(Environment.GetEnvironmentVariable(ExternalToolPolicy.EnvironmentVariable), "auto",
            StringComparison.OrdinalIgnoreCase) ? null : ExternalToolPolicy.Use(ExternalToolSource.PythonPackages);
        var app = new CliApp(stdout, stderr);
        var parse = app.BuildRoot().Parse(args);
        if (parse.Errors.Count > 0)
        {
            foreach (var error in parse.Errors) stderr.WriteLine(error.Message);
            return CliExitCode.Usage;
        }
        return await parse.InvokeAsync(new InvocationConfiguration
        {
            Output = stdout,
            Error = stderr,
            // Ctrl+C отменяет токен команды; расчёт успевает остановиться и закрыть БД.
            ProcessTerminationTimeout = TimeSpan.FromSeconds(30)
        });
    }

    /// <summary>Значение --lang до разбора: от него зависят описания команд в справке.</summary>
    static string? LanguageFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--lang" && i + 1 < args.Length) return args[i + 1];
            if (args[i].StartsWith("--lang=", StringComparison.Ordinal) || args[i].StartsWith("--lang:", StringComparison.Ordinal))
                return args[i][7..];
        }
        return null;
    }

    RootCommand BuildRoot()
    {
        _lang.Description = Loc.S("CliOptLang");
        _lang.AcceptOnlyFromAmong("ru", "en");
        _json.Description = Loc.S("CliOptJson");
        _quiet.Description = Loc.S("CliOptQuiet");
        _project.Description = Loc.S("CliArgProject");
        _ids.Description = Loc.S("CliArgIds");
        _all.Description = Loc.S("CliOptAll");
        _outFile.Description = Loc.S("CliOptOut");
        _force.Description = Loc.S("CliOptForce");
        _inPlace.Description = Loc.S("CliOptInPlace");
        _yes.Description = Loc.S("CliOptYes");
        _withData.Description = Loc.S("CliOptWithData");
        _task.Description = Loc.S("CliOptTask");
        _check.Description = Loc.S("CliOptCheck");
        _analysis.Description = Loc.S("CliOptAnalysis");
        _resultId.Description = Loc.S("CliArgResultId");
        _output.Description = Loc.S("CliOptOutput");
        _artifacts.Description = Loc.S("CliOptArtifacts");

        var info = new Command("info", Loc.S("CliCmdInfo")) { _project };
        info.SetAction((p, ct) => Handle(p, ct, InfoAsync));

        var listTasks = new Command("tasks", Loc.S("CliCmdListTasks")) { _project };
        listTasks.SetAction((p, ct) => Handle(p, ct, ListTasksAsync));
        var listChecks = new Command("checks", Loc.S("CliCmdListChecks")) { _project };
        listChecks.SetAction((p, ct) => Handle(p, ct, ListChecksAsync));
        var listAnalyses = new Command("analyses", Loc.S("CliCmdListAnalyses")) { _project };
        listAnalyses.SetAction((p, ct) => Handle(p, ct, ListAnalysesAsync));
        var listResults = new Command("results", Loc.S("CliCmdListResults")) { _project, _task };
        listResults.SetAction((p, ct) => Handle(p, ct, ListResultsAsync));
        var list = new Command("list", Loc.S("CliCmdList")) { listTasks, listChecks, listAnalyses, listResults };

        var run = new Command("run", Loc.S("CliCmdRun"))
        {
            RunCommand("task", "CliCmdRunTask", RunTasksAsync),
            RunCommand("check", "CliCmdRunCheck", RunChecksAsync),
            RunCommand("analysis", "CliCmdRunAnalysis", RunAnalysesAsync)
        };

        var exportResult = new Command("result", Loc.S("CliCmdExportResult"))
        {
            _project, _resultId, _task, _check, _analysis, _output
        };
        exportResult.SetAction((p, ct) => Handle(p, ct, ExportResultAsync));
        var export = new Command("export", Loc.S("CliCmdExport")) { exportResult };

        var tools = new Command("tools", Loc.S("CliCmdTools"));
        tools.SetAction((p, ct) => Handle(p, ct, ToolsAsync));

        return new RootCommand(Loc.S("CliRootDescription")) { _lang, _json, _quiet, info, list, run, export, tools };
    }

    Command RunCommand(string name, string descriptionKey, Func<CliCall, Task<int>> body)
    {
        var command = new Command(name, Loc.S(descriptionKey))
        {
            _project, _ids, _all, _outFile, _force, _inPlace, _yes, _withData, _artifacts
        };
        command.SetAction((p, ct) => Handle(p, ct, body));
        return command;
    }

    async Task<int> Handle(ParseResult parse, CancellationToken token, Func<CliCall, Task<int>> body)
    {
        var call = new CliCall(this, parse, token);
        try
        {
            return await body(call);
        }
        catch (CliException ex)
        {
            _err.WriteLine(ex.Message);
            return ex.ExitCode;
        }
        catch (Exception ex)
        {
            _err.WriteLine(string.Format(Loc.S("CliErrUnexpected"), ex.Message));
            return CliExitCode.Failed;
        }
    }

    /// <summary>Один вызов команды: разобранные аргументы, журнал, открытие проекта.</summary>
    sealed class CliCall(CliApp app, ParseResult parse, CancellationToken token)
    {
        public ParseResult Parse { get; } = parse;
        public CancellationToken Token { get; } = token;
        public TextWriter Out => app._out;
        public TextWriter Err => app._err;
        public bool Json => Parse.GetValue(app._json);
        public bool Quiet => Parse.GetValue(app._quiet);
        public ConsoleLog Log { get; } = new(app._err);

        public ProjectSession OpenProject(ProjectSessionOptions? options = null, bool yes = false) =>
            ProjectSession.Open(Parse.GetValue(app._project)!, options ?? new ProjectSessionOptions(),
                Log, new ConsoleNotifier(Log, yes), new ConsoleBusyHost(app._err, Quiet, Token));
    }
}
