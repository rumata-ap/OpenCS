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
    /// <summary>Выполняет расчёт постановки analysis и возвращает CalcResult
    /// (kind=fem_linear|fem_nonlinear).</summary>
    public static async Task<CalcResult> RunAsync(AppViewModel app, FemSchema schema, FemAnalysis analysis, CancellationToken ct)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var db = app.db;

        var meshNodes = db.GetFemMeshNodes(schema.Id);
        var meshElems = db.GetFemMeshElements(schema.Id);
        var sourceNodes = db.GetFemNodes(schema.Id);
        var sourceMembers = db.GetFemMembers(schema.Id);
        var loadCases = db.GetFemLoadCases(schema.Id);
        var allLoads = db.GetFemNodeLoads(schema.Id);

        string taskKind = analysis.Kind == "nonlinear" ? "fem_nonlinear" : "fem_linear";

        IReadOnlyList<FemNodeLoad> resolvedLoads;
        try
        {
            resolvedLoads = FemLoadExpressionResolver.Resolve(analysis.GetLoadExpression(), loadCases, allLoads);
        }
        catch (NotSupportedException ex)
        {
            return Error(analysis, created, ex.Message, taskKind);
        }

        var parameters = FemAnalysisParams.Parse(analysis.ParamsJson);
        var executable = new OpenSeesExecutableResolver(Path.Combine(AppContext.BaseDirectory, "OpenSees.exe"))
            .Resolve(parameters.ExecutablePath ?? ResolveFromOpenSeesHome());
        var runRequest = new OpenSeesRunRequest
        {
            ExecutablePath = executable.Path,
            WorkingDirectory = Path.GetTempPath(),
            Timeout = TimeSpan.FromSeconds(parameters.TimeoutSeconds)
        };

        if (analysis.Kind == "nonlinear")
            return await RunNonlinearAsync(app, analysis, created, meshNodes, meshElems, sourceNodes, sourceMembers,
                resolvedLoads, parameters, runRequest, ct);

        return await RunLinearAsync(app, analysis, created, meshNodes, meshElems, sourceNodes, sourceMembers,
            resolvedLoads, runRequest, ct);
    }

    static async Task<CalcResult> RunLinearAsync(AppViewModel app, FemAnalysis analysis, string created,
        List<FemMeshNode> meshNodes, List<FemElement> meshElems, List<FemNode> sourceNodes, List<FemMember> sourceMembers,
        IReadOnlyList<FemNodeLoad> resolvedLoads, OpenSeesRunRequest runRequest, CancellationToken ct)
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

        var input = new FemLinearWorkflowInput(meshNodes, meshElems, sourceNodes, sourceMembers, resolvedLoads, sectionProps);

        var service = new FemLinearAnalysisService(
            new FemLinearTclGenerator(),
            new OpenSeesProcessRunner(),
            new OpenSeesArtifactStore(Path.Combine(AppContext.BaseDirectory, "OpenSeesArtifacts")),
            new FemLinearResultParser());
        var workflow = new FemLinearAnalysisWorkflow(service);

        var output = await workflow.RunAsync(input, runRequest, ct);

        return new CalcResult
        {
            TaskId = 0, TaskKind = "fem_linear", TaskTag = analysis.Tag, Created = created,
            Status = output.Status, DataJson = output.DataJson
        };
    }

    static async Task<CalcResult> RunNonlinearAsync(AppViewModel app, FemAnalysis analysis, string created,
        List<FemMeshNode> meshNodes, List<FemElement> meshElems, List<FemNode> sourceNodes, List<FemMember> sourceMembers,
        IReadOnlyList<FemNodeLoad> resolvedLoads, FemAnalysisParams parameters, OpenSeesRunRequest runRequest, CancellationToken ct)
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
            parameters.GeomTransfKind, parameters.LoadSteps, parameters.Tolerance,
            parameters.MaxIterations, parameters.IntegrationPoints, parameters.ConvergenceTest);

        var input = new FemNonlinearWorkflowInput(
            meshNodes, meshElems, sourceNodes, sourceMembers, resolvedLoads,
            sections, materials, app.Diagrams, calcType, options);

        var service = new FemNonlinearAnalysisService(
            new FemNonlinearTclGenerator(),
            new OpenSeesProcessRunner(),
            new OpenSeesArtifactStore(Path.Combine(AppContext.BaseDirectory, "OpenSeesArtifacts")),
            new FemNonlinearResultParser());
        var workflow = new FemNonlinearAnalysisWorkflow(service);

        var output = await workflow.RunAsync(input, runRequest, ct);

        return new CalcResult
        {
            TaskId = 0, TaskKind = "fem_nonlinear", TaskTag = analysis.Tag, Created = created,
            Status = output.Status, DataJson = output.DataJson
        };
    }

    /// <summary>Путь к OpenSees.exe из %OPENSEES_HOME%\bin, если он существует; иначе null.</summary>
    static string? ResolveFromOpenSeesHome()
    {
        var home = Environment.GetEnvironmentVariable("OPENSEES_HOME");
        if (string.IsNullOrWhiteSpace(home)) return null;
        var candidate = Path.Combine(home, "bin", "OpenSees.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    static CalcResult Error(FemAnalysis analysis, string created, string message, string taskKind) => new()
    {
        TaskId = 0, TaskKind = taskKind, TaskTag = analysis.Tag, Created = created,
        Status = "error",
        DataJson = JsonSerializer.Serialize(new { error = message, errors = new[] { message } })
    };
}
