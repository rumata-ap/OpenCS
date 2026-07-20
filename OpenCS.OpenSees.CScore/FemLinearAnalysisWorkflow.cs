using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Входные слои для линейного расчёта FEM-схемы.</summary>
public sealed record FemLinearWorkflowInput(
    IReadOnlyList<FemMeshNode> MeshNodes,
    IReadOnlyList<FemElement> MeshElements,
    IReadOnlyList<FemNode> SourceNodes,
    IReadOnlyList<FemMember> SourceMembers,
    IReadOnlyList<FemNodeLoad> ResolvedLoads,
    IReadOnlyDictionary<int, GeoProps> SectionProps);

/// <summary>Итог workflow: статус, типизированный результат, ошибки, сериализованный DataJson.</summary>
public sealed record FemLinearWorkflowOutput(string Status, FemLinearResult? Result, IReadOnlyList<string> Errors, string DataJson);

/// <summary>Связывает резолвер и сервис: валидация → сборка → запуск → сериализация без БД/WPF.</summary>
public sealed class FemLinearAnalysisWorkflow
{
    private readonly FemLinearAnalysisService _service;
    public FemLinearAnalysisWorkflow(FemLinearAnalysisService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <summary>Резолвит модель и, при отсутствии ошибок, запускает расчёт.</summary>
    public async Task<FemLinearWorkflowOutput> RunAsync(FemLinearWorkflowInput input, OpenSeesRunRequest processRequest, CancellationToken ct)
    {
        var resolve = new FemLinearModelResolver().Resolve(
            input.MeshNodes, input.MeshElements, input.SourceNodes, input.SourceMembers, input.ResolvedLoads, input.SectionProps);

        if (!resolve.Ok)
        {
            string errJson = JsonSerializer.Serialize(new { error = "Модель не прошла валидацию.", errors = resolve.Errors });
            return new FemLinearWorkflowOutput("error", null, resolve.Errors, errJson);
        }

        var result = await _service.RunAsync(resolve.Model!, processRequest, ct);
        string dataJson = JsonSerializer.Serialize(result);
        return new FemLinearWorkflowOutput(result.Status, result, [], dataJson);
    }
}
