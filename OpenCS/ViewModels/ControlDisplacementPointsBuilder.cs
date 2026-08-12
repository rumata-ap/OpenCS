using OpenCS.OpenSees.Structural;

namespace OpenCS.ViewModels;

/// <summary>Строит точки графика λ–перемещение контрольного/мониторингового узла из
/// истории шагов нелинейного расчёта. Чистая функция без побочных эффектов — вынесена
/// отдельно от FemAnalysisResultVM, чтобы не требовать конструирования VM (DatabaseService,
/// FemSchema) для тестирования этой алгоритмической логики.</summary>
public static class ControlDisplacementPointsBuilder
{
    public readonly record struct Point(double X, double Y, bool Converged, int SegmentId);

    /// <summary>Ключ «источника» точки — то, что делает два соседних шага частью одной
    /// физической кривой. НЕ только (NodeTag, Dof): две разные прямые стадии могут
    /// случайно использовать один и тот же узел/DOF (например, DisplacementControl-стадия
    /// и следующая за ней ArcLength-стадия, обе мониторящие узел 4 DOF 3) — без StageIndex/
    /// Mode в ключе они ошибочно слились бы в один сегмент.</summary>
    readonly record struct Source(int StageIndex, FemPathControlMode Mode, int NodeTag, int Dof);

    /// <summary>Строит список ТОЙ ЖЕ длины и в том же порядке, что steps — позиционный
    /// индекс в результате остаётся позиционным индексом в steps (важно для
    /// FemAnalysisResultVM.SelectedStepIndex/StepClicked). X=NaN — узел/DOF для этого шага
    /// не определён (LoadControl-фаза без continuation, либо StagePathControls пуст —
    /// backward-compat со старыми результатами, сериализованными до появления этой фичи).</summary>
    public static IReadOnlyList<Point> Build(
        IReadOnlyList<FemNonlinearStepResult> steps,
        IReadOnlyList<FemPathControlSettings?> stagePathControls,
        IReadOnlyList<FemPathControlSwitch> switches)
    {
        var result = new List<Point>(steps.Count);
        Source? prevSource = null;
        int nextSegmentId = 0;
        int currentSegmentId = -1;

        foreach (var step in steps)
        {
            FemPathControlSettings? stage = step.StageIndex >= 0 && step.StageIndex < stagePathControls.Count
                ? stagePathControls[step.StageIndex] : null;

            Source? source = stage == null ? null : stage.Mode switch
            {
                FemPathControlMode.DisplacementControl => new Source(step.StageIndex, FemPathControlMode.DisplacementControl,
                    stage.DisplacementControl!.ControlNodeTag, stage.DisplacementControl.ControlDof),
                FemPathControlMode.ArcLength => new Source(step.StageIndex, FemPathControlMode.ArcLength,
                    stage.ArcLength!.MonitorNodeTag, stage.ArcLength.MonitorDof),
                FemPathControlMode.LoadControl => ResolveContinuationSource(stage, step, switches),
                _ => null
            };

            if (source is not { } s)
            {
                // Разрыв (узел/DOF не определён — LoadControl-фаза без continuation).
                // GroupBySegment (FemLoadFactorCanvas) в любом случае прерывает полилинию на
                // каждом NaN независимо от SegmentId, поэтому сброс prevSource здесь
                // безопасен: следующая конечная точка начнёт новый сегмент, что визуально
                // совпадает со "сравнением с предыдущей конечной точкой".
                result.Add(new Point(double.NaN, step.LoadFactor, step.Converged, -1));
                prevSource = null;
                continue;
            }

            if (prevSource != s)
            {
                currentSegmentId = nextSegmentId++;
                prevSource = s;
            }

            // Несошедшийся или неполный шаг не содержит записи перемещения искомого узла
            // (FemNonlinearStepResult.Displacements пуст при Converged=false) — FemNodeDisplacement
            // это record (ссылочный тип), FirstOrDefault на отсутствии совпадения вернёт null,
            // а не нулевую структуру. Явная проверка на null вместо слепого обращения к
            // disp.Ux — иначе NullReferenceException, либо ложная точка X=0, которую canvas
            // ошибочно соединил бы линией с соседними точками.
            var disp = step.Displacements.FirstOrDefault(d => d.NodeTag == s.NodeTag);
            double x = disp is null ? double.NaN : ComponentValue(disp, s.Dof);
            result.Add(new Point(x, step.LoadFactor, step.Converged, currentSegmentId));
        }

        return result;
    }

    static Source? ResolveContinuationSource(
        FemPathControlSettings stage, FemNonlinearStepResult step, IReadOnlyList<FemPathControlSwitch> switches)
    {
        var sw = switches.FirstOrDefault(x => x.StageIndex == step.StageIndex && x.AtStepIndex <= step.StepIndex);
        if (sw is null) return null;
        return stage.ContinueWithMode switch
        {
            FemPathControlMode.DisplacementControl => new Source(step.StageIndex, FemPathControlMode.DisplacementControl,
                stage.ContinueWithDisplacementControl!.ControlNodeTag, stage.ContinueWithDisplacementControl.ControlDof),
            FemPathControlMode.ArcLength => new Source(step.StageIndex, FemPathControlMode.ArcLength,
                stage.ContinueWithArcLength!.MonitorNodeTag, stage.ContinueWithArcLength.MonitorDof),
            _ => null
        };
    }

    static double ComponentValue(FemNodeDisplacement disp, int dof) => dof switch
    {
        1 => disp.Ux, 2 => disp.Uy, 3 => disp.Uz, 4 => disp.Rx, 5 => disp.Ry, 6 => disp.Rz,
        _ => double.NaN
    };
}
