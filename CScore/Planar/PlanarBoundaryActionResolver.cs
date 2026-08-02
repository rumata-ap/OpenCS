using CScore.Fem;

namespace CScore.Planar;

/// <summary>Строго объединяет parent/template actions без скрытого сложения DOF.</summary>
public sealed class PlanarBoundaryActionResolver
{
    /// <summary>Получает и валидирует actions в явно выбранном source mode.</summary>
    public PlanarBoundaryActionProviderResult Resolve(
        PlanarBoundaryActionRequest request,
        IPlanarBoundaryActionProvider? parentProvider,
        IPlanarBoundaryActionProvider? templateProvider)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = new List<FemValidationDiagnostic>();
        diagnostics.AddRange(request.Interface.Validate());
        try { request.TargetFrame.Validate(); }
        catch (InvalidOperationException ex) { diagnostics.Add(new("planar_boundary_target_frame_invalid", ex.Message)); }
        ValidateParentScenario(request, diagnostics);

        var results = new List<PlanarBoundaryActionProviderResult>();
        if (request.SourceMode is PlanarBoundaryActionSourceMode.Parent or PlanarBoundaryActionSourceMode.Combined)
            results.Add(ResolveProvider(parentProvider, request, PlanarBoundaryActionSourceMode.Parent, diagnostics));
        if (request.SourceMode is PlanarBoundaryActionSourceMode.Template or PlanarBoundaryActionSourceMode.Combined)
            results.Add(ResolveProvider(templateProvider, request, PlanarBoundaryActionSourceMode.Template, diagnostics));

        var forceActions = new List<PlanarBoundaryForceAction>();
        var kinematicActions = new List<PlanarBoundaryKinematicAction>();
        var sourceReferences = new List<PlanarBoundarySourceReference>();
        PlanarDofMask covered = PlanarDofMask.None;
        foreach (var result in results)
        {
            diagnostics.AddRange(result.Diagnostics);
            forceActions.AddRange(result.ForceActions);
            kinematicActions.AddRange(result.KinematicActions);
            sourceReferences.AddRange(result.SourceReferences);
            PlanarDofMask overlap = covered & result.CoveredDofs;
            if (overlap != PlanarDofMask.None)
                diagnostics.Add(new("planar_boundary_source_dof_conflict", $"Источники boundary action пересекаются по DOF {overlap}."));
            covered |= result.CoveredDofs;
        }

        PlanarDofMask missing = request.RequiredDofs & ~covered;
        if (missing != PlanarDofMask.None)
            diagnostics.Add(new("planar_boundary_required_dof_missing", $"Boundary action не покрывает требуемые DOF {missing}."));

        return new PlanarBoundaryActionProviderResult
        {
            SourceMode = request.SourceMode,
            ForceActions = forceActions,
            KinematicActions = kinematicActions,
            SourceReferences = sourceReferences,
            Diagnostics = diagnostics
        };
    }

    static PlanarBoundaryActionProviderResult ResolveProvider(
        IPlanarBoundaryActionProvider? provider,
        PlanarBoundaryActionRequest request,
        PlanarBoundaryActionSourceMode expectedMode,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        if (provider is null)
        {
            diagnostics.Add(new("planar_boundary_provider_missing", $"Для source mode {expectedMode} не задан provider."));
            return new() { SourceMode = expectedMode };
        }

        try
        {
            var result = provider.Resolve(request);
            if (result.SourceMode != expectedMode)
                diagnostics.Add(new("planar_boundary_provider_mode_mismatch", $"Provider вернул mode {result.SourceMode}, ожидался {expectedMode}."));
            return result;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            diagnostics.Add(new("planar_boundary_provider_failed", ex.Message));
            return new() { SourceMode = expectedMode };
        }
    }

    static void ValidateParentScenario(PlanarBoundaryActionRequest request, ICollection<FemValidationDiagnostic> diagnostics)
    {
        if (request.SourceMode is not (PlanarBoundaryActionSourceMode.Parent or PlanarBoundaryActionSourceMode.Combined))
            return;
        if (request.Scenario is not { ResultAvailable: true } scenario)
        {
            diagnostics.Add(new("planar_boundary_parent_result_missing", "Для parent source отсутствует доступный result scenario."));
            return;
        }
        if (!scenario.Converged)
            diagnostics.Add(new("planar_boundary_parent_step_not_converged", "Выбранный parent result step не сошёлся."));
    }
}
