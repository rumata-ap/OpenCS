using System.IO;
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

/// <summary>Загружает слои схемы из БД, запускает линейный OpenSees-расчёт и формирует CalcResult.</summary>
public static class FemAnalysisExecutor
{
    /// <summary>Выполняет линейный расчёт постановки analysis и возвращает CalcResult (kind=fem_linear).</summary>
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

        IReadOnlyList<FemNodeLoad> resolvedLoads;
        try
        {
            resolvedLoads = FemLoadExpressionResolver.Resolve(analysis.GetLoadExpression(), loadCases, allLoads);
        }
        catch (NotSupportedException ex)
        {
            return Error(analysis, created, ex.Message);
        }

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

        var parameters = FemAnalysisParams.Parse(analysis.ParamsJson);
        var executable = new OpenSeesExecutableResolver(Path.Combine(AppContext.BaseDirectory, "OpenSees.exe"))
            .Resolve(parameters.ExecutablePath);

        var service = new FemLinearAnalysisService(
            new FemLinearTclGenerator(),
            new OpenSeesProcessRunner(),
            new OpenSeesArtifactStore(Path.Combine(AppContext.BaseDirectory, "OpenSeesArtifacts")),
            new FemLinearResultParser());
        var workflow = new FemLinearAnalysisWorkflow(service);

        var output = await workflow.RunAsync(input, new OpenSeesRunRequest
        {
            ExecutablePath = executable.Path,
            WorkingDirectory = Path.GetTempPath(),
            Timeout = TimeSpan.FromSeconds(parameters.TimeoutSeconds)
        }, ct);

        return new CalcResult
        {
            TaskId = 0,
            TaskKind = "fem_linear",
            TaskTag = analysis.Tag,
            Created = created,
            Status = output.Status,
            DataJson = output.DataJson
        };
    }

    static CalcResult Error(FemAnalysis analysis, string created, string message) => new()
    {
        TaskId = 0, TaskKind = "fem_linear", TaskTag = analysis.Tag, Created = created,
        Status = "error",
        DataJson = System.Text.Json.JsonSerializer.Serialize(new { error = message, errors = new[] { message } })
    };
}
