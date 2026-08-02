namespace CScore.Planar;

/// <summary>Provider явного boundary template без зависимости от parent model.</summary>
public sealed class PlanarBoundaryTemplateProvider : IPlanarBoundaryActionProvider
{
    readonly PlanarBoundaryActionSet _template;

    /// <summary>Создаёт provider из заранее заданного normalized template.</summary>
    public PlanarBoundaryTemplateProvider(PlanarBoundaryActionSet template)
    {
        _template = template ?? throw new ArgumentNullException(nameof(template));
    }

    /// <inheritdoc />
    public PlanarBoundaryActionProviderResult Resolve(PlanarBoundaryActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = new List<CScore.Fem.FemValidationDiagnostic>(_template.Diagnostics);
        var forces = _template.ForceActions
            .Select(action => Normalize(action, request.TargetFrame, diagnostics))
            .ToArray();
        var kinematics = _template.KinematicActions
            .Select(action => Normalize(action, request.TargetFrame, diagnostics))
            .ToArray();
        foreach (var action in forces) diagnostics.AddRange(action.Validate());
        foreach (var action in kinematics) diagnostics.AddRange(action.Validate());
        if (forces.Any(action => action.InterfaceId != request.Interface.Id) ||
            kinematics.Any(action => action.InterfaceId != request.Interface.Id))
            diagnostics.Add(new("planar_boundary_template_interface_mismatch", "Template ссылается на другой cut interface."));

        return new PlanarBoundaryActionProviderResult
        {
            SourceMode = PlanarBoundaryActionSourceMode.Template,
            ForceActions = forces,
            KinematicActions = kinematics,
            SourceReferences = _template.SourceReferences,
            Diagnostics = diagnostics
        };
    }

    static PlanarBoundaryForceAction Normalize(
        PlanarBoundaryForceAction action,
        Frame3D target,
        ICollection<CScore.Fem.FemValidationDiagnostic> diagnostics)
    {
        try
        {
            var sourceReferenceGlobal = PlanarBoundaryFrameConverter.ToGlobalPoint(action.Frame, action.ReferencePoint);
            var referencePoint = PlanarBoundaryFrameConverter.ToLocalPoint(target, sourceReferenceGlobal);
            return new PlanarBoundaryForceAction
            {
                InterfaceId = action.InterfaceId,
                DofMask = action.DofMask,
                Frame = target,
                UnitSystem = action.UnitSystem,
                Interpolation = action.Interpolation,
                ReferencePoint = referencePoint,
                Samples = action.Samples.Select(sample => new PlanarBoundaryForceSample(
                    sample.S,
                    PlanarBoundaryFrameConverter.ToLocalVector(target,
                        PlanarBoundaryFrameConverter.ToGlobalVector(action.Frame, sample.ForcePerLength)),
                    PlanarBoundaryFrameConverter.ToLocalVector(target,
                        PlanarBoundaryFrameConverter.ToGlobalVector(action.Frame, sample.MomentPerLength)))).ToArray(),
                SourceReferences = action.SourceReferences
            };
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(new("planar_boundary_template_frame_invalid", ex.Message));
            return action;
        }
    }

    static PlanarBoundaryKinematicAction Normalize(
        PlanarBoundaryKinematicAction action,
        Frame3D target,
        ICollection<CScore.Fem.FemValidationDiagnostic> diagnostics)
    {
        try
        {
            return new PlanarBoundaryKinematicAction
            {
                InterfaceId = action.InterfaceId,
                DofMask = action.DofMask,
                Frame = target,
                UnitSystem = action.UnitSystem,
                Interpolation = action.Interpolation,
                Samples = action.Samples.Select(sample => new PlanarBoundaryKinematicSample(
                    sample.S,
                    PlanarBoundaryFrameConverter.ToLocalVector(target,
                        PlanarBoundaryFrameConverter.ToGlobalVector(action.Frame, sample.Displacement)),
                    PlanarBoundaryFrameConverter.ToLocalVector(target,
                        PlanarBoundaryFrameConverter.ToGlobalVector(action.Frame, sample.Rotation)))).ToArray(),
                SourceReferences = action.SourceReferences
            };
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(new("planar_boundary_template_frame_invalid", ex.Message));
            return action;
        }
    }
}
