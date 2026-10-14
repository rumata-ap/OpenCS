using CScore.Submodel;
using OpenCS.OpenSees.CScore;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Строит и сохраняет граничный сценарий извлечённой прямой цепочки: читает извлечение, родительскую
/// схему и её сохранённый линейный результат из <see cref="DatabaseService"/>, адаптирует результат OpenSees
/// и вызывает доменный <see cref="StraightBeamBoundaryScenarioBuilder"/>. Непригодный результат родителя
/// оформляется сохранённым сценарием со статусом Blocked, а не исключением.
/// </summary>
public sealed class StraightBeamBoundaryScenarioService
{
    readonly DatabaseService _database;

    public StraightBeamBoundaryScenarioService(DatabaseService database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    public SubmodelBoundaryScenario BuildAndSave(int submodelSchemaId, IReadOnlyList<DofOverride>? overrides = null)
    {
        var extraction = _database.GetSubmodelExtractionBySubmodelSchema(submodelSchemaId)
            ?? throw new InvalidOperationException($"Схема #{submodelSchemaId} не является извлечённой субмоделью.");
        var parent = _database.FemSchemas.FirstOrDefault(s => s.Id == extraction.ParentSchemaId)
            ?? throw new InvalidOperationException($"Родительская схема #{extraction.ParentSchemaId} не найдена.");

        var calcResult = _database.GetCalcResultById(extraction.ParentResultId);
        var adapted = calcResult is null
            ? new ParentResultAdapterOutcome(null, new CScore.Fem.FemValidationDiagnostic(
                BoundaryScenarioDiagnostics.ParentResultInvalid,
                $"Результат родителя #{extraction.ParentResultId} не найден.", true, []))
            : FemLinearResultParentAdapter.FromCalcResult(calcResult);

        var build = adapted.Result is null
            ? StraightBeamBoundaryScenarioBuilder.Blocked(extraction, parent.SourceType, [adapted.Error!])
            : StraightBeamBoundaryScenarioBuilder.Build(new BoundaryScenarioInput(
                extraction, parent.SourceType,
                _database.GetFemNodes(parent.Id), _database.GetFemMembers(parent.Id),
                _database.GetFemMeshNodes(parent.Id), _database.GetFemMeshElements(parent.Id),
                _database.GetFemLoadCases(parent.Id), _database.GetFemNodeLoads(parent.Id),
                _database.GetFemMemberLoads(parent.Id), _database.GetFemKinematicLoads(parent.Id),
                adapted.Result, overrides ?? []));

        return _database.SaveSubmodelBoundaryScenario(extraction.Id, build.Scenario);
    }
}
