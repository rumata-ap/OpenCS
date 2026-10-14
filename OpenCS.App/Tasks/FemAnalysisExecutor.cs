using System.IO;
using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Fem.Combinations;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.Tasks;

/// <summary>Загружает слои схемы из БД, запускает линейный или нелинейный OpenSees-расчёт
/// (по FemAnalysis.Kind) и формирует CalcResult.</summary>
public static class FemAnalysisExecutor
{
    /// <summary>
    /// Полный прогон постановки с занятостью и отменой: статус «running» → расчёт → результат и статус в БД → итог
    /// и диагностика в журнал. Возвращает сохранённый результат; null — занято, схема не найдена, отмена или ошибка
    /// (статус постановки «cancelled»/«error» сохранён).
    /// </summary>
    public static async Task<CalcResult?> RunAndSaveAsync(Services.IProjectContext app, FemAnalysis analysis)
    {
        var busy = app.Busy;
        if (busy.IsBusy) return null;
        var schema = app.FemSchemas.FirstOrDefault(s => s.Id == analysis.SchemaId);
        if (schema == null) return null;

        var db = app.Db;
        var log = app.LogService;
        var cts = busy.BeginBusyWithCancellation(
            string.Format(Utilites.Loc.S("FemAnalysisRunning"), analysis.Tag), indeterminate: true);
        analysis.Status = "running";
        db.SaveFemAnalysis(analysis);
        try
        {
            var result = await RunAsync(app, schema, analysis, cts.Token);
            db.SaveCalcResult(result);
            analysis.ResultId = result.Id;
            analysis.Status   = result.Status;
            db.SaveFemAnalysis(analysis);

            if (!LogCsfeaResult(log, result))
                LogOpenSeesResult(log, analysis, result);
            busy.EndBusy(string.Format(Utilites.Loc.S("FemAnalysisDone"), analysis.Tag));
            return result;
        }
        catch (OperationCanceledException)
        {
            analysis.Status = "cancelled";
            db.SaveFemAnalysis(analysis);
            busy.EndBusy(Utilites.Loc.S("CalcTaskCancelled"));
        }
        catch (Exception ex)
        {
            analysis.Status = "error";
            db.SaveFemAnalysis(analysis);
            busy.EndBusy();
            log.Error(ex.Message);
        }
        return null;
    }

    /// <summary>Итог OpenSees-расчёта в журнал: статус, диагностика прогона, каталог артефактов.</summary>
    static void LogOpenSeesResult(Services.ILogService log, FemAnalysis analysis, CalcResult result)
    {
        var statusKey = result.Status switch
        {
            "ok" => "CalcResultOk",
            "not_converged" => "CalcResultNotConverged",
            "partial" => "CalcResultPartial",
            "not_passed" => "CalcResultNotPassed",
            _ => "CalcResultError"
        };
        string done = string.Format(Utilites.Loc.S(statusKey), analysis.Tag);
        if (result.Status == "ok")
            log.Info(done);
        else if (result.Status == "not_converged")
            log.Warning(done);
        else
            log.Error(done);

        var (diagnostics, artifactDirectory) = Services.FemAnalysisResultDiagnostics.Read(result.DataJson);
        foreach (var diag in diagnostics)
            log.Warning($"[{analysis.Tag}] {diag}");
        if (!string.IsNullOrEmpty(artifactDirectory))
            log.Info($"[{analysis.Tag}] {Utilites.Loc.S("FemResultArtifacts")} {artifactDirectory}");
    }

    /// <summary>Результат CSfea: итог и отчёт — в журнал; true — это он.</summary>
    static bool LogCsfeaResult(Services.ILogService log, CalcResult result)
    {
        if (result.TaskKind != Services.FemCsfeaRunner.TaskKind) return false;
        string tag = result.TaskTag;
        var statusKey = result.Status switch
        {
            "ok" => "CalcResultOk",
            "not_converged" => "CalcResultNotConverged",
            _ => "CalcResultError"
        };
        string done = string.Format(Utilites.Loc.S(statusKey), tag);
        if (result.Status == "ok") log.Info(done);
        else if (result.Status == "not_converged") log.Warning(done);
        else log.Error(done);
        if (Services.FemCsfeaResultSummary.Parse(result.DataJson) is { } summary)
        {
            foreach (var line in summary.Report)
                if (line.StartsWith("Ошибка: ", StringComparison.Ordinal)) log.Error($"[{tag}] {line}");
                else log.Warning($"[{tag}] {line}");
            foreach (var line in summary.Describe().Where(l => !l.StartsWith("Ошибка: ", StringComparison.Ordinal)))
                log.Info($"[{tag}] {line}");
        }
        return true;
    }

    /// <summary>Выполняет расчёт постановки analysis и возвращает CalcResult
    /// (kind=fem_linear|fem_nonlinear).</summary>
    public static async Task<CalcResult> RunAsync(Services.IProjectContext app, FemSchema schema, FemAnalysis analysis, CancellationToken ct)
    {
        if (Services.FemCsfeaRunner.IsCsfeaKind(analysis.Kind))
            return await RunCsfeaAsync(app, schema, analysis, ct);

        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var db = app.Db;

        var meshNodes = db.GetFemMeshNodes(schema.Id);
        var meshElems = db.GetFemMeshElements(schema.Id);
        // Пластины (своих областей или импорта) расчёт OpenSees схемы не берёт: только стержни и их узлы.
        int shellCount = meshElems.Count(e => e.ElemType == "shell");
        if (shellCount > 0)
        {
            meshElems = meshElems.Where(e => e.ElemType != "shell").ToList();
            var used = meshElems.SelectMany(e => JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? []).ToHashSet();
            meshNodes = meshNodes.Where(n => int.TryParse(n.NodeTag, out var tag) && used.Contains(tag)).ToList();
            app.LogService.Warning(string.Format(Utilites.Loc.S("FemAnalysisShellsIgnored"), analysis.Tag, shellCount));
        }
        var sourceNodes = db.GetFemNodes(schema.Id);
        var sourceMembers = db.GetFemMembers(schema.Id);
        var loadCases = db.GetFemLoadCases(schema.Id);
        var allLoads = db.GetFemNodeLoads(schema.Id);
        var allMemberLoads = db.GetFemMemberLoads(schema.Id);
        var allKinematicLoads = db.GetFemKinematicLoads(schema.Id);

        string taskKind = analysis.Kind == "nonlinear" ? "fem_nonlinear" : "fem_linear";

        var parameters = FemAnalysisParams.Parse(analysis.ParamsJson);

        FemResolvedLoads resolved = new([], [], []);
        List<FemNonlinearStageInput> nonlinearStages = [];
        try
        {
            if (analysis.Kind == "nonlinear")
            {
                nonlinearStages = BuildNonlinearStages(parameters, analysis, loadCases, allLoads, allMemberLoads, allKinematicLoads);
            }
            else
            {
                resolved = FemLoadExpressionResolver.Resolve(
                    analysis.GetLoadExpression(), loadCases, allLoads, allMemberLoads, allKinematicLoads);
            }
        }
        catch (NotSupportedException ex)
        {
            return Error(analysis, created, ex.Message, taskKind);
        }

        var calcSettings = app.CalcSettings;
        var executable = new OpenSeesExecutableResolver()
            .Resolve(calcSettings.OpenSeesExecutablePath);
        var runRequest = new OpenSeesRunRequest
        {
            ExecutablePath = executable.Path,
            WorkingDirectory = Path.GetTempPath(),
            Timeout = TimeSpan.FromSeconds(calcSettings.OpenSeesTimeoutSeconds),
            OnOutputLine = line => app.LogService.Info($"[{analysis.Tag}] {line}")
        };

        if (analysis.Kind == "nonlinear")
            return await RunNonlinearAsync(app, analysis, created, meshNodes, meshElems, sourceNodes, sourceMembers,
                nonlinearStages, parameters, calcSettings, runRequest, ct);

        return await RunLinearAsync(app, analysis, created, meshNodes, meshElems, sourceNodes, sourceMembers,
            resolved, runRequest, ct);
    }

    /// <summary>
    /// Секущий или линейный расчёт CSfea (<see cref="Services.FemCsfeaRunner"/>); прогресс секущего решателя — в полосу
    /// занятости приложения (вызывать из UI-потока: <see cref="Progress{T}"/> возвращает отчёты в него).
    /// </summary>
    static Task<CalcResult> RunCsfeaAsync(Services.IProjectContext app, FemSchema schema, FemAnalysis analysis, CancellationToken ct)
    {
        var progress = new Progress<CSfea.Core.SecantProgress>(p => app.Busy.ReportBusyProgress(p.Fraction, string.Format(
            Utilites.Loc.S("FemCsfeaProgress"), analysis.Tag, p.StageName, p.Step, p.StepCount, p.LoadFactor, p.Iteration, p.Cracked)));
        var ctx = new Services.FemCsfeaRunContext(app.Db, app.GmshSettings, app.LogService,
            app.CalcSettings.ResolveCsfeaMaxDegreeOfParallelism());
        return Services.FemCsfeaRunner.RunAsync(ctx, schema, analysis, progress, ct);
    }

    /// <summary>Резолвит нагрузки и способ управления траекторией по стадиям нелинейной
    /// постановки. Не обращается к БД — все аргументы уже загружены вызывающей стороной.
    /// Бросает NotSupportedException (тег стадии в сообщении) на некорректное выражение
    /// нагрузки или на неполный/неизвестный DTO path control (FemAnalysisPathControlMapper)
    /// — вызывающая сторона (RunAsync) перехватывает её в одном общем try/catch.</summary>
    public static List<FemNonlinearStageInput> BuildNonlinearStages(
        FemAnalysisParams parameters, FemAnalysis analysis,
        List<FemLoadCase> loadCases, List<FemNodeLoad> allLoads,
        List<FemMemberLoad> allMemberLoads, List<FemKinematicLoad> allKinematicLoads)
    {
        var nonlinearStages = new List<FemNonlinearStageInput>();
        foreach (var stage in parameters.ResolveStages(analysis))
        {
            var stageResolved = FemLoadExpressionResolver.Resolve(
                FemLoadExpression.Parse(stage.LoadExpressionJson), loadCases, allLoads, allMemberLoads, allKinematicLoads);
            var pathControl = FemAnalysisPathControlMapper.Resolve(stage.PathControl, stage.ContinueWith, stage.Tag);
            nonlinearStages.Add(new FemNonlinearStageInput(
                stage.Tag, stageResolved.NodeLoads,
                stage.LoadFactorStep ?? 0.1, stage.MaxLoadFactor ?? 10.0)
            {
                MemberLoads = stageResolved.MemberLoads, KinematicLoads = stageResolved.KinematicLoads,
                PathControl = pathControl
            });
        }
        return nonlinearStages;
    }

    static async Task<CalcResult> RunLinearAsync(Services.IProjectContext app, FemAnalysis analysis, string created,
        List<FemMeshNode> meshNodes, List<FemElement> meshElems, List<FemNode> sourceNodes, List<FemMember> sourceMembers,
        FemResolvedLoads resolved, OpenSeesRunRequest runRequest, CancellationToken ct)
    {
        // GeoProps по каждому используемому сечению — из готовых (с фибрами) сечений проекта
        var sectionProps = new Dictionary<int, GeoProps>();
        foreach (var csId in meshElems
                     .Select(e => sourceMembers.FirstOrDefault(m => m.ElemTag == e.SourceMemberTag)?.CrossSectionId)
                     .Where(id => id is not null).Select(id => id!.Value).Distinct())
        {
            var section = app.CrossSections.FirstOrDefault(s => s.Id == csId);
            if (section is null) continue;               // резолвер сообщит «сечение не готово»
            sectionProps[csId] = new GeoProps(section);
        }

        var input = new FemLinearWorkflowInput(meshNodes, meshElems, sourceNodes, sourceMembers,
            resolved.NodeLoads, sectionProps)
        { ResolvedMemberLoads = resolved.MemberLoads, ResolvedKinematicLoads = resolved.KinematicLoads };

        var service = new FemLinearAnalysisService(
            new FemLinearTclGenerator(),
            new OpenSeesProcessRunner(),
            new OpenSeesArtifactStore(app.CalcSettings.ResolveOpenSeesArtifactsPath()),
            new FemLinearResultParser());
        var workflow = new FemLinearAnalysisWorkflow(service);

        var output = await workflow.RunAsync(input, runRequest, ct);
        LogOpenSeesDiagnostics(app, output.Status, output.Result?.Diagnostics);

        return new CalcResult
        {
            TaskId = 0, TaskKind = "fem_linear", TaskTag = analysis.Tag, Created = created,
            Status = output.Status, DataJson = output.DataJson
        };
    }

    static async Task<CalcResult> RunNonlinearAsync(Services.IProjectContext app, FemAnalysis analysis, string created,
        List<FemMeshNode> meshNodes, List<FemElement> meshElems, List<FemNode> sourceNodes, List<FemMember> sourceMembers,
        List<FemNonlinearStageInput> stages, FemAnalysisParams parameters, Utilites.CalcSettings calcSettings,
        OpenSeesRunRequest runRequest, CancellationToken ct)
    {
        if (parameters.CalcType is not { } calcType)
            return Error(analysis, created, "Не выбран тип расчёта (CalcType) для нелинейной постановки.", "fem_nonlinear");

        // Проектные CrossSection (с фибрами) по каждому используемому сечению
        var sections = new Dictionary<int, CrossSection>();
        foreach (var csId in meshElems
                     .Select(e => sourceMembers.FirstOrDefault(m => m.ElemTag == e.SourceMemberTag)?.CrossSectionId)
                     .Where(id => id is not null).Select(id => id!.Value).Distinct())
        {
            var section = app.CrossSections.FirstOrDefault(s => s.Id == csId);
            if (section is null) continue;               // резолвер сообщит «сечение не готово»
            sections[csId] = section;
        }
        var materials = app.Materials.Where(m => m.Id != 0).ToDictionary(m => m.Id);
        var options = new FemNonlinearAnalysisOptions(
            GeomTransfKind: calcSettings.OpenSeesGeomTransfKind,
            RefinementDivisions: calcSettings.OpenSeesRefinementDivisions,
            Tolerance: calcSettings.OpenSeesTolerance,
            MaxIterations: calcSettings.OpenSeesMaxIterations,
            IntegrationPoints: calcSettings.OpenSeesIntegrationPoints,
            ConvergenceTest: calcSettings.OpenSeesConvergenceTest,
            ConsiderConcreteTension: parameters.ConsiderConcreteTension,
            MaterialSource: Enum.Parse<MaterialSource>(parameters.MaterialSource),
            MainMaterialModel: Enum.Parse<MainMaterialModelKind>(parameters.MainMaterialModel),
            SteelModel: Enum.Parse<SteelModelKind>(parameters.SteelModel),
            SteelHardeningRatioOverride: parameters.SteelHardeningRatioOverride,
            MaxRefinementDepth: calcSettings.OpenSeesMaxRefinementDepth,
            ElementFormulation: parameters.ElementFormulation,
            Algorithm: calcSettings.OpenSeesAlgorithm,
            RecordFiberStates: calcSettings.OpenSeesRecordFiberStates,
            FiberStatesIntegrationPoints: calcSettings.ResolveOpenSeesFiberStatesIntegrationPoints(),
            ConsiderPhysicalNonlinearity: parameters.ConsiderPhysicalNonlinearity,
            Sp63EtaMin: calcSettings.Sp63DescEtaMin,
            SteelHardeningModulusMpa: parameters.SteelHardeningModulusMpa,
            EkbEtaMin: calcSettings.EkbDescEtaMin);

        var input = new FemNonlinearWorkflowInput(
            meshNodes, meshElems, sourceNodes, sourceMembers, stages,
            sections, materials, app.Diagrams, calcType, options);

        var service = new FemNonlinearAnalysisService(
            new FemNonlinearTclGenerator(),
            new OpenSeesProcessRunner(),
            new OpenSeesArtifactStore(calcSettings.ResolveOpenSeesArtifactsPath()),
            new FemNonlinearResultParser());
        var workflow = new FemNonlinearAnalysisWorkflow(service);

        var output = await workflow.RunAsync(input, runRequest, ct);
        LogOpenSeesDiagnostics(app, output.Status, output.Result?.Diagnostics);

        return new CalcResult
        {
            TaskId = 0, TaskKind = "fem_nonlinear", TaskTag = analysis.Tag, Created = created,
            Status = output.Status, DataJson = output.DataJson
        };
    }

    /// <summary>Пишет сырую диагностику OpenSees-прогона (stdout/stderr) в собственный лог OpenCS
    /// одной записью — без построчной фильтрации, чтобы не потерять ничего и не усложнять код.</summary>
    static void LogOpenSeesDiagnostics(Services.IProjectContext app, string status, IReadOnlyList<string>? diagnostics)
    {
        if (diagnostics == null || diagnostics.Count == 0) return;
        string text = string.Join(Environment.NewLine, diagnostics);
        if (status == "ok") app.LogService.Info(text);
        else app.LogService.Warning(text);
    }

    static CalcResult Error(FemAnalysis analysis, string created, string message, string taskKind) => new()
    {
        TaskId = 0, TaskKind = taskKind, TaskTag = analysis.Tag, Created = created,
        Status = "error",
        DataJson = JsonSerializer.Serialize(new { error = message, errors = new[] { message } })
    };
}
