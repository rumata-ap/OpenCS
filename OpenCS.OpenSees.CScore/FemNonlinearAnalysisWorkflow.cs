using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Входные слои для нелинейного расчёта FEM-схемы.</summary>
public sealed record FemNonlinearWorkflowInput(
    IReadOnlyList<FemMeshNode> MeshNodes,
    IReadOnlyList<FemElement> MeshElements,
    IReadOnlyList<FemNode> SourceNodes,
    IReadOnlyList<FemMember> SourceMembers,
    IReadOnlyList<FemNodeLoad> ResolvedLoads,
    IReadOnlyDictionary<int, CrossSection> Sections,
    IReadOnlyDictionary<int, Material> Materials,
    IReadOnlyList<Diagramm>? CustomDiagramPool,
    CalcType CalcType,
    FemNonlinearAnalysisOptions Options)
{
    /// <summary>Распределённые нагрузки конструктивных стержней после разрешения выражения.</summary>
    public IReadOnlyList<FemMemberLoad> ResolvedMemberLoads { get; init; } = [];
    /// <summary>Заданные перемещения и повороты узлов после разрешения выражения.</summary>
    public IReadOnlyList<FemKinematicLoad> ResolvedKinematicLoads { get; init; } = [];
}

/// <summary>Итог workflow: статус, типизированный результат, ошибки, сериализованный DataJson.</summary>
public sealed record FemNonlinearWorkflowOutput(string Status, FemNonlinearResult? Result, IReadOnlyList<string> Errors, string DataJson);

/// <summary>Связывает резолвер и сервис: валидация → сборка → запуск → сериализация без БД/WPF.</summary>
public sealed class FemNonlinearAnalysisWorkflow
{
    private readonly FemNonlinearAnalysisService _service;
    public FemNonlinearAnalysisWorkflow(FemNonlinearAnalysisService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <summary>Резолвит модель и, при отсутствии ошибок, запускает расчёт.</summary>
    public async Task<FemNonlinearWorkflowOutput> RunAsync(FemNonlinearWorkflowInput input, OpenSeesRunRequest processRequest, CancellationToken ct)
    {
        var resolve = new FemNonlinearModelResolver().Resolve(
            input.MeshNodes, input.MeshElements, input.SourceNodes, input.SourceMembers, input.ResolvedLoads,
            input.Sections, input.Materials, input.CustomDiagramPool, input.CalcType, input.Options,
            input.ResolvedMemberLoads, input.ResolvedKinematicLoads);

        if (!resolve.Ok)
        {
            string errJson = JsonSerializer.Serialize(new { error = "Модель не прошла валидацию.", errors = resolve.Errors });
            return new FemNonlinearWorkflowOutput("error", null, resolve.Errors, errJson);
        }

        var result = await _service.RunAsync(resolve.Model!, processRequest, ct);
        string dataJson = JsonSerializer.Serialize(result);
        return new FemNonlinearWorkflowOutput(result.Status, result, [], dataJson);
    }
}
