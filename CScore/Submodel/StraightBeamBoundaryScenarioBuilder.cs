using CScore.Fem;
using CScore.Fem.Combinations;

namespace CScore.Submodel;

/// <summary>Вход сборщика граничного сценария: извлечение, родительская схема и её линейный результат.</summary>
public sealed record BoundaryScenarioInput(
    SubmodelExtraction Extraction,
    string ParentSourceType,
    IReadOnlyList<FemNode> ParentNodes,
    IReadOnlyList<FemMember> ParentMembers,
    IReadOnlyList<FemMeshNode> ParentMeshNodes,
    IReadOnlyList<FemElement> ParentMeshElements,
    IReadOnlyList<FemLoadCase> ParentLoadCases,
    IReadOnlyList<FemNodeLoad> ParentNodeLoads,
    IReadOnlyList<FemMemberLoad> ParentMemberLoads,
    IReadOnlyList<FemKinematicLoad> ParentKinematicLoads,
    IParentLinearResult ParentResult,
    IReadOnlyList<DofOverride> Overrides);

/// <summary>Результат сборки: сценарий есть всегда, при блокирующих ошибках — со статусом Blocked.</summary>
public sealed record BoundaryScenarioBuildResult(
    BoundaryScenario Scenario,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Строит граничный сценарий извлечённой прямой цепочки: нагрузки по снимку выражения загружения
/// (λ = 1), граничные векторы концов, режимы DOF и контроль. Исключения — только на null-аргументы;
/// любые проблемы данных оформляются диагностиками.
/// </summary>
public static class StraightBeamBoundaryScenarioBuilder
{
    public static BoundaryScenarioBuildResult Build(BoundaryScenarioInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Extraction);
        ArgumentNullException.ThrowIfNull(input.ParentResult);
        var diagnostics = new List<FemValidationDiagnostic>();

        if (!input.ParentResult.Capabilities.IsLinear)
        {
            diagnostics.Add(new(BoundaryScenarioDiagnostics.ParentResultNotLinear,
                "Граничный сценарий строится только по линейному результату родителя.", true, []));
            return Blocked(input, diagnostics);
        }

        var chain = SubmodelChainTopology.Build(input.Extraction, input.ParentMeshElements, diagnostics);
        if (chain is null) return Blocked(input, diagnostics);

        FemResolvedLoads loads;
        try
        {
            loads = FemLoadExpressionResolver.Resolve(
                FemLoadExpression.Parse(input.Extraction.LoadExpressionJson), input.ParentLoadCases,
                input.ParentNodeLoads, input.ParentMemberLoads, input.ParentKinematicLoads);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or FormatException
                                       or System.Text.Json.JsonException)
        {
            diagnostics.Add(new(BoundaryScenarioDiagnostics.LoadUnresolved,
                $"Выражение загружения извлечения не разрешено: {ex.Message}", true, []));
            return Blocked(input, diagnostics);
        }

        var classification = SubmodelLoadClassifier.Classify(chain, input.ParentSourceType, input.ParentNodes,
            input.ParentMembers, input.ParentMeshNodes, input.ParentMeshElements, loads);
        diagnostics.AddRange(classification.Diagnostics);

        var ends = new List<ScenarioEnd>(2);
        foreach (bool atStart in new[] { true, false })
        {
            var actions = BoundaryActionProviders.Collect(atStart, chain, input.ParentMembers, input.ParentMeshNodes,
                input.ParentMeshElements, classification.BoundaryNodal, input.ParentResult, diagnostics);
            int mask = ParentDofMask(actions.ParentNodeTag, input.ParentMeshNodes, input.ParentNodes);
            var dofs = BoundaryDofModeSelector.Select(actions, mask, classification.InterfaceKinematic,
                input.Overrides ?? [], diagnostics);
            var control = BoundaryControlCheck.Compute(actions, mask, diagnostics);
            ends.Add(new ScenarioEnd(atStart, chain.ChildNodeByParent[actions.ParentNodeTag], actions.ParentNodeTag,
                dofs, actions.BoundaryVector, actions.Contributions, actions.Reaction, actions.Displacement, control));
        }

        if (ends.SelectMany(e => e.Dofs).All(d => d.Mode == DofMode.Force))
            diagnostics.Add(new(BoundaryScenarioDiagnostics.GaugeRequired,
                "Все DOF обоих концов в силовом режиме: запуск возможен только после фиксации жёсткотельных мод (срез 4).",
                false, []));

        var status = diagnostics.Any(d => d.IsError) ? ScenarioStatus.Blocked
            : classification.Completeness == LoadCompleteness.Unknown ? ScenarioStatus.Incomplete
            : ScenarioStatus.Complete;
        var scenario = new BoundaryScenario(input.Extraction.Id, input.Extraction.ReferenceScale, status,
            classification.Completeness, classification.RetainedDistributed, classification.RetainedPoints,
            classification.RetainedNodal, classification.RetainedKinematic, classification.Accounting, ends, diagnostics);
        return new BoundaryScenarioBuildResult(scenario, diagnostics);
    }

    /// <summary>Сценарий без граничных данных для случая, когда сборка невозможна.</summary>
    public static BoundaryScenarioBuildResult Blocked(SubmodelExtraction extraction, string parentSourceType,
        IReadOnlyList<FemValidationDiagnostic> diagnostics)
    {
        var completeness = parentSourceType is "internal" or "opensees" ? LoadCompleteness.Known : LoadCompleteness.Unknown;
        var scenario = new BoundaryScenario(extraction.Id, extraction.ReferenceScale, ScenarioStatus.Blocked, completeness,
            [], [], [], [], new LoadAccounting(0, 0, 0), [], diagnostics);
        return new BoundaryScenarioBuildResult(scenario, diagnostics);
    }

    static BoundaryScenarioBuildResult Blocked(BoundaryScenarioInput input, List<FemValidationDiagnostic> diagnostics) =>
        Blocked(input.Extraction, input.ParentSourceType, diagnostics);

    /// <summary>DofMask конструктивного узла, на который ссылается mesh-узел конца (нет — 0).</summary>
    static int ParentDofMask(string parentMeshNodeTag, IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemNode> nodes)
    {
        var meshNode = meshNodes.FirstOrDefault(n => FemMeshTopology.CanonicalNodeTag(n.NodeTag) == parentMeshNodeTag);
        if (meshNode?.SourceNodeTag is not string sourceTag) return 0;
        return nodes.FirstOrDefault(n => n.NodeTag == sourceTag)?.DofMask ?? 0;
    }
}
