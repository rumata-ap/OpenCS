using CScore.Fem;

namespace CScore.Submodel;

/// <summary>
/// Выбор режима каждого из 6 DOF конца: опора родителя → Fixed; заданное перемещение в узле →
/// Kinematic; определённый силовой вклад → Force; иначе перемещение родителя → Kinematic;
/// иначе — блокирующая диагностика. Ручные переопределения проверяются на наличие данных.
/// </summary>
public static class BoundaryDofModeSelector
{
    static readonly string[] DofNames = ["Ux", "Uy", "Uz", "Rx", "Ry", "Rz"];

    public static IReadOnlyList<DofAssignment> Select(
        EndActions actions,
        int parentDofMask,
        IReadOnlyList<InterfaceKinematicLoad> interfaceKinematic,
        IReadOnlyList<DofOverride> overrides,
        List<FemValidationDiagnostic> diagnostics)
    {
        var result = new DofAssignment[6];
        for (int dof = 0; dof < 6; dof++)
        {
            var kinematicLoad = interfaceKinematic.LastOrDefault(k => k.ParentNodeTag == actions.ParentNodeTag && k.Dof == dof);
            var manual = overrides.LastOrDefault(o => o.AtStart == actions.AtStart && o.Dof == dof);
            result[dof] = manual is not null
                ? ApplyOverride(actions, dof, manual.Mode, kinematicLoad, diagnostics)
                : Auto(actions, parentDofMask, dof, kinematicLoad, diagnostics);
        }
        diagnostics.Add(new(BoundaryScenarioDiagnostics.Info,
            $"Режимы DOF конца {actions.ParentNodeTag}: " +
            string.Join(", ", result.Select((a, i) => $"{DofNames[i]}={a.Mode}{(a.Source == DofSource.Override ? "*" : "")}")) + ".",
            false, [actions.ParentNodeTag]));
        return result;
    }

    static DofAssignment Auto(EndActions actions, int mask, int dof, InterfaceKinematicLoad? kinematicLoad,
        List<FemValidationDiagnostic> diagnostics)
    {
        if ((mask & (1 << dof)) != 0) return new DofAssignment(DofMode.Fixed, null, DofSource.Auto);
        if (kinematicLoad is not null) return new DofAssignment(DofMode.Kinematic, kinematicLoad.Value, DofSource.Auto);
        if (actions.BoundaryVector is Dof6 force) return new DofAssignment(DofMode.Force, force[dof], DofSource.Auto);
        if (actions.Displacement is Dof6 u) return new DofAssignment(DofMode.Kinematic, u[dof], DofSource.Auto);
        diagnostics.Add(new(BoundaryScenarioDiagnostics.DofUndetermined,
            $"Конец {actions.ParentNodeTag}, {DofNames[dof]}: нет ни опоры, ни силового вклада, ни перемещения родителя.", true,
            [actions.ParentNodeTag]));
        return new DofAssignment(DofMode.Force, null, DofSource.Auto);
    }

    static DofAssignment ApplyOverride(EndActions actions, int dof, DofMode mode, InterfaceKinematicLoad? kinematicLoad,
        List<FemValidationDiagnostic> diagnostics)
    {
        switch (mode)
        {
            case DofMode.Fixed:
                return new DofAssignment(DofMode.Fixed, null, DofSource.Override);
            case DofMode.Force when actions.BoundaryVector is Dof6 force:
                return new DofAssignment(DofMode.Force, force[dof], DofSource.Override);
            case DofMode.Force:
                string reason = actions.UnsupportedJunctionTags.Count > 0
                    ? $"к концу примыкают неподдержанные объекты ({string.Join(", ", actions.UnsupportedJunctionTags)})"
                    : "силовой вклад не определён";
                diagnostics.Add(new(
                    actions.UnsupportedJunctionTags.Count > 0 ? BoundaryScenarioDiagnostics.UnsupportedJunction : BoundaryScenarioDiagnostics.OverrideInvalid,
                    $"Конец {actions.ParentNodeTag}, {DofNames[dof]}: силовой режим невозможен — {reason}.", true, [actions.ParentNodeTag]));
                return new DofAssignment(DofMode.Force, null, DofSource.Override);
            case DofMode.Kinematic when kinematicLoad is not null:
                return new DofAssignment(DofMode.Kinematic, kinematicLoad.Value, DofSource.Override);
            case DofMode.Kinematic when actions.Displacement is Dof6 u:
                return new DofAssignment(DofMode.Kinematic, u[dof], DofSource.Override);
            default:
                diagnostics.Add(new(BoundaryScenarioDiagnostics.OverrideInvalid,
                    $"Конец {actions.ParentNodeTag}, {DofNames[dof]}: кинематический режим невозможен — нет перемещений родителя.", true,
                    [actions.ParentNodeTag]));
                return new DofAssignment(DofMode.Kinematic, null, DofSource.Override);
        }
    }
}
