using System;
using System.Linq;

namespace CScore.Planar.Fragments
{
    /// <summary>Чистое масштабирование величины template-набора boundary actions —
    /// используется VerticalPlanarFragmentRunner для FragmentStage.CutInterfaceScale.</summary>
    public static class PlanarBoundaryActionSetScaling
    {
        public static PlanarBoundaryActionSet Scale(PlanarBoundaryActionSet source, double factor)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new PlanarBoundaryActionSet
            {
                SourceMode = source.SourceMode,
                ForceActions = source.ForceActions.Select(a => new PlanarBoundaryForceAction
                {
                    InterfaceId = a.InterfaceId,
                    DofMask = a.DofMask,
                    Frame = a.Frame,
                    UnitSystem = a.UnitSystem,
                    Interpolation = a.Interpolation,
                    ReferencePoint = a.ReferencePoint,
                    Samples = a.Samples.Select(s => new PlanarBoundaryForceSample(
                        s.S, s.ForcePerLength * factor, s.MomentPerLength * factor)).ToArray(),
                    SourceReferences = a.SourceReferences
                }).ToArray(),
                KinematicActions = source.KinematicActions.Select(a => new PlanarBoundaryKinematicAction
                {
                    InterfaceId = a.InterfaceId,
                    DofMask = a.DofMask,
                    Frame = a.Frame,
                    UnitSystem = a.UnitSystem,
                    Interpolation = a.Interpolation,
                    Samples = a.Samples.Select(s => new PlanarBoundaryKinematicSample(
                        s.S, s.Displacement * factor, s.Rotation * factor)).ToArray(),
                    SourceReferences = a.SourceReferences
                }).ToArray(),
                SourceReferences = source.SourceReferences,
                Diagnostics = source.Diagnostics
            };
        }
    }
}
