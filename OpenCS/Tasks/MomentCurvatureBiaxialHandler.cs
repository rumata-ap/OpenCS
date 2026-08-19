using System;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик задачи «Кривизна-момент (двухплоскостной изгиб)» — составная 4-участковая
/// диаграмма по п. 8.2.23-8.2.32 СП 63.13330 для произвольного направления изгиба.
/// </summary>
public sealed class MomentCurvatureBiaxialHandler : ITaskHandler
{
    /// <inheritdoc/>
    public string Kind => "moment_curvature_biaxial";

    /// <inheritdoc/>
    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
        CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx?.Database?.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram);

            var parameters = MomentCurvatureBiaxialTaskParams.Parse(task.ParamsJson);
            var nMode = parameters.NMode == "proportional"
                ? CurvatureNMode.Proportional
                : CurvatureNMode.Constant;

            // Задача не ограничена 2-й ГПС (N/NL) — трещинообразование, текучесть и предел
            // несущей способности можно строить и на диаграммах 1-й ГПС (C/CL), поэтому
            // выбранный пользователем CalcType передаётся решателю напрямую, без подмены.
            var calcCrc = task.CalcType;
            var solver = new BiaxialCurvatureCurveSolver(section,
                calcCrc: calcCrc, calcService: calcCrc,
                solverTol: settings.NewtonTolerance,
                solverMaxIter: settings.NewtonMaxIter,
                solverH: settings.NewtonDeltaH,
                centralJacobian: settings.NewtonJacobian == "central",
                cancellationToken: ctx?.CancellationToken ?? default);

            var result = solver.Compute(item.N, item.Mx, item.My, nMode, parameters.UsePsi);

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = result.Status,
                DataJson = JsonSerializer.Serialize(MomentCurvatureBiaxialJson.Build(result))
            };
        }
        catch (OperationCanceledException)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
                Status = "error", DataJson = JsonSerializer.Serialize(new { error = "Отменено" })
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
                Status = "error", DataJson = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }
}

/// <summary>JSON-контракт результата задачи moment_curvature_biaxial.</summary>
internal static class MomentCurvatureBiaxialJson
{
    public static object Build(BiaxialCurvatureCurveResult result) => new
    {
        n_mode = result.NMode == CurvatureNMode.Proportional ? "proportional" : "constant",
        use_psi = result.UsePsi,
        has_mx = result.HasMx,
        has_my = result.HasMy,
        ea0 = Math.Round(result.Ea0, 2),
        b0x = Math.Round(result.B0x, 4),
        b0y = Math.Round(result.B0y, 4),
        status = result.Status,
        cracking = PointJson(result.Cracking),
        crack_transition = PointJson(result.CrackTransitionPoint),
        yield_point = PointJson(result.Yield),
        ultimate = PointJson(result.Ultimate),
        ultimate_reference = PointJson(result.UltimateReference),
        points = result.Points.Select(PointJson)
    };

    static object? PointJson(BiaxialCurveScanPoint? p) => p == null ? null : new
    {
        n = Math.Round(p.N, 4),
        mx = Math.Round(p.Mx, 4),
        my = Math.Round(p.My, 4),
        e0 = Math.Round(p.E0, 10),
        ky = Math.Round(p.Ky, 8),
        kz = Math.Round(p.Kz, 8),
        segment = p.Segment,
        converged = p.Converged,
        psi_active = p.PsiActive,
        non_physical = p.NonPhysical
    };
}
