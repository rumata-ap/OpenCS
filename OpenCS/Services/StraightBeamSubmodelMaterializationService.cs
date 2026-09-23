using CScore.Fem;
using CScore.Submodel;
using OpenCS.OpenSees.CScore;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Итог материализации: сохранённая запись (null — не материализовано) и диагностика.</summary>
public sealed record SubmodelMaterializationOutcome(
    SubmodelMaterialization? Materialization,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Итог линейной сверки: отчёт (null — сверка невозможна) и диагностика.</summary>
public sealed record SubmodelVerificationOutcome(
    SubmodelVerificationReport? Report,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Материализует граничный сценарий извлечённой цепочки в конструктивный слой дочерней схемы и сверяет
/// её линейный расчёт с родителем. Читает всё из <see cref="DatabaseService"/>, логику делегирует чистым
/// <see cref="SubmodelMaterializationPlanner"/> и <see cref="SubmodelLinearVerification"/>. Устаревший
/// mesh-снимок (пересоздана сетка) блокирует и материализацию, и сверку. Без UI.
/// </summary>
public sealed class StraightBeamSubmodelMaterializationService
{
    readonly DatabaseService _database;

    public StraightBeamSubmodelMaterializationService(DatabaseService database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    public SubmodelMaterializationOutcome Materialize(int submodelSchemaId)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        if (!TryLoad(submodelSchemaId, diagnostics, out var extraction, out var scenario))
            return new(null, diagnostics);

        var meshNodes = _database.GetFemMeshNodes(submodelSchemaId);
        var meshElements = _database.GetFemMeshElements(submodelSchemaId);
        diagnostics.AddRange(SubmodelMeshIntegrity.Check(extraction, meshNodes, meshElements, checkIds: true));
        if (diagnostics.Any(d => d.IsError)) return new(null, diagnostics);

        var build = SubmodelMaterializationPlanner.Plan(new(extraction, scenario.Scenario, meshNodes, meshElements,
            _database.GetFemNodes(extraction.ParentSchemaId)));
        diagnostics.AddRange(build.Diagnostics);
        if (!build.IsSuccess) return new(null, diagnostics);

        try
        {
            return new(_database.ApplySubmodelMaterialization(submodelSchemaId, scenario.Id, build.Plan!), diagnostics);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("submodel_", StringComparison.Ordinal))
        {
            diagnostics.Add(new(ex.Message, Explain(ex.Message), true, []));
            return new(null, diagnostics);
        }
    }

    public SubmodelVerificationOutcome Verify(int submodelSchemaId)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        if (!TryLoad(submodelSchemaId, diagnostics, out var extraction, out var scenario))
            return new(null, diagnostics);

        diagnostics.AddRange(SubmodelMeshIntegrity.Check(extraction,
            _database.GetFemMeshNodes(submodelSchemaId), _database.GetFemMeshElements(submodelSchemaId), checkIds: true));
        if (diagnostics.Any(d => d.IsError)) return new(null, diagnostics);

        var materialization = _database.GetSubmodelMaterialization(scenario.Id);
        if (materialization is null)
        {
            diagnostics.Add(new(SubmodelMaterializationDiagnostics.Stale,
                "Текущий граничный сценарий не материализован — выполните материализацию.", true, []));
            return new(null, diagnostics);
        }

        var analysis = _database.GetFemAnalyses(submodelSchemaId)
            .FirstOrDefault(a => a.Tag == SubmodelMaterializationPlanner.AnalysisTag);
        var childCalc = analysis?.ResultId is { } childResultId ? _database.GetCalcResultById(childResultId) : null;
        if (childCalc is null)
        {
            diagnostics.Add(new(SubmodelMaterializationDiagnostics.Info,
                $"Нет результата постановки «{SubmodelMaterializationPlanner.AnalysisTag}» — выполните её расчёт.", false, []));
            return new(null, diagnostics);
        }
        var parentCalc = _database.GetCalcResultById(extraction.ParentResultId);
        if (parentCalc is null)
        {
            diagnostics.Add(new(BoundaryScenarioDiagnostics.ParentResultInvalid,
                $"Результат родителя #{extraction.ParentResultId} не найден.", true, []));
            return new(null, diagnostics);
        }

        var parent = FemLinearResultParentAdapter.FromCalcResult(parentCalc);
        var child = FemLinearResultParentAdapter.FromCalcResult(childCalc);
        foreach (var error in new[] { parent.Error, child.Error })
            if (error is not null) diagnostics.Add(error);
        if (parent.Result is null || child.Result is null) return new(null, diagnostics);

        var report = SubmodelLinearVerification.Compare(extraction, scenario.Scenario, materialization.Summary,
            parent.Result, child.Result);
        diagnostics.AddRange(report.Diagnostics);
        return new(report, diagnostics);
    }

    bool TryLoad(int submodelSchemaId, List<FemValidationDiagnostic> diagnostics,
        out SubmodelExtraction extraction, out SubmodelBoundaryScenario scenario)
    {
        extraction = _database.GetSubmodelExtractionBySubmodelSchema(submodelSchemaId)!;
        scenario = null!;
        if (extraction is null)
        {
            diagnostics.Add(new(SubmodelMaterializationDiagnostics.ScenarioBlocked,
                $"Схема #{submodelSchemaId} не является извлечённой субмоделью.", true, []));
            return false;
        }
        var stored = _database.GetSubmodelBoundaryScenarios(extraction.Id).FirstOrDefault(s => s.Ordinal == 0);
        if (stored is null)
        {
            diagnostics.Add(new(SubmodelMaterializationDiagnostics.ScenarioBlocked,
                "У субмодели нет граничного сценария — сначала постройте его.", true, []));
            return false;
        }
        scenario = stored;
        return true;
    }

    static string Explain(string code) => code switch
    {
        SubmodelMaterializationDiagnostics.Stale => "Результат родителя изменился после извлечения — субмодель устарела.",
        SubmodelMaterializationDiagnostics.MeshStale => "Сетка дочерней схемы изменилась после извлечения.",
        "submodel_materialization_child_is_parent" =>
            "Дочерняя схема сама является родителем другой субмодели — замена её слоя сломала бы ту субмодель.",
        _ => $"Материализация отклонена: {code}."
    };
}
