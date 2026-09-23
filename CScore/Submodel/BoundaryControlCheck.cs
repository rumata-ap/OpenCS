using CScore.Fem;

namespace CScore.Submodel;

/// <summary>
/// Сверка граничного вектора с контрольным остатком выбранной части. Из равновесия узла родителя
/// <c>Σ_e p_e = P + R</c> следует <c>p_boundary + R = p_control</c>, где <c>p_control = +Σ p_r</c>
/// выбранных КЭ, а <c>R</c> — реакция родителя (ноль на незакреплённых DOF).
/// </summary>
public static class BoundaryControlCheck
{
    const double RelativeTolerance = 1e-6;
    const double AbsoluteForceN = 1e-6;
    const double AbsoluteMomentNm = 1e-6;

    /// <param name="parentDofMask">Закреплённые DOF родителя в узле конца (биты 0–5).</param>
    public static ControlCheck Compute(EndActions actions, int parentDofMask, List<FemValidationDiagnostic> diagnostics)
    {
        if (actions.BoundaryVector is not Dof6 boundary || actions.RetainedResistance is not Dof6 control)
        {
            diagnostics.Add(new(BoundaryScenarioDiagnostics.Info,
                $"Контроль конца {actions.ParentNodeTag} недоступен: нет граничного вектора или усилий выбранных КЭ.", false,
                [actions.ParentNodeTag]));
            return new ControlCheck(actions.RetainedResistance, 0, 0, Available: false, Passed: false);
        }

        double forceMismatch = 0, momentMismatch = 0, forceScale = 0, momentScale = 0;
        var skipped = new List<int>();
        for (int dof = 0; dof < 6; dof++)
        {
            bool supported = (parentDofMask & (1 << dof)) != 0;
            double reaction = 0;
            if (supported)
            {
                if (actions.Reaction is not Dof6 r) { skipped.Add(dof); continue; }
                reaction = r[dof];
            }
            double mismatch = Math.Abs(boundary[dof] + reaction - control[dof]);
            double scale = Math.Max(Math.Abs(boundary[dof] + reaction), Math.Abs(control[dof]));
            if (dof < 3) { forceMismatch = Math.Max(forceMismatch, mismatch); forceScale = Math.Max(forceScale, scale); }
            else { momentMismatch = Math.Max(momentMismatch, mismatch); momentScale = Math.Max(momentScale, scale); }
        }
        if (skipped.Count > 0)
            diagnostics.Add(new(BoundaryScenarioDiagnostics.Info,
                $"Контроль конца {actions.ParentNodeTag}: закреплённые DOF {string.Join(", ", skipped)} без реакции родителя исключены.", false,
                [actions.ParentNodeTag]));

        double forceTolerance = Math.Max(RelativeTolerance * forceScale, AbsoluteForceN);
        double momentTolerance = Math.Max(RelativeTolerance * momentScale, AbsoluteMomentNm);
        bool passed = forceMismatch <= forceTolerance && momentMismatch <= momentTolerance;
        if (!passed)
            diagnostics.Add(new(BoundaryScenarioDiagnostics.ControlMismatch,
                $"Конец {actions.ParentNodeTag}: граничный вектор расходится с остатком выбранной части — " +
                $"по силам {forceMismatch:G6} Н (допуск {forceTolerance:G3}), по моментам {momentMismatch:G6} Н·м (допуск {momentTolerance:G3}). " +
                $"p_boundary = {Format(boundary)}, p_control = {Format(control)}.", false, [actions.ParentNodeTag]));
        return new ControlCheck(control, forceMismatch, momentMismatch, Available: true, Passed: passed);
    }

    static string Format(Dof6 v) => $"({v.X:G6}; {v.Y:G6}; {v.Z:G6}; {v.Rx:G6}; {v.Ry:G6}; {v.Rz:G6})";
}
