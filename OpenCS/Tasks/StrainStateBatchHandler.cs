using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик задачи «Состояние деформаций (весь набор)»: находит плоскость деформаций
/// для каждой строки ForceSet методом Ньютона-Рафсона. Несходимость строки не прерывает пакет.
/// </summary>
public sealed class StrainStateBatchHandler : ITaskHandler
{
    public string Kind => "strain_state_batch";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException(
                    "Для strain_state_batch требуется контекст с DatabaseService.");

            var forceSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException(
                    $"Набор усилий id={task.ForceSetId} не найден.");

            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx.Database.Diagrams);

            var rows = new List<object>();
            int convergedCount = 0;

            foreach (var fi in forceSet.Items)
            {
                var solver = new StrainSolver(section, task.CalcType,
                    tol:     settings.NewtonTolerance,
                    maxIter: settings.NewtonMaxIter,
                    h:       settings.NewtonDeltaH);
                var k = solver.Solve(fi.N, fi.Mx, fi.My);

                if (solver.Converged) convergedCount++;

                rows.Add(new
                {
                    label      = fi.Label,
                    N          = fi.N,
                    Mx         = fi.Mx,
                    My         = fi.My,
                    e0         = Math.Round(k.e0, 8),
                    ky         = Math.Round(k.ky, 8),
                    kz         = Math.Round(k.kz, 8),
                    iterations = solver.Iterations,
                    residual   = Math.Round(solver.Residual, 6),
                    status     = solver.Converged ? "ok" : "not_converged"
                });
            }

            int total = rows.Count;
            bool allConverged = convergedCount == total;

            var data = new
            {
                all_converged   = allConverged,
                converged_count = convergedCount,
                total,
                rows
            };

            return new CalcResult
            {
                TaskId   = task.Id,
                TaskKind = task.Kind,
                TaskTag  = task.Tag,
                Created  = created,
                Status   = allConverged ? "ok" : "partial",
                DataJson = JsonSerializer.Serialize(data)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId   = task.Id,
                TaskKind = task.Kind,
                TaskTag  = task.Tag,
                Created  = created,
                Status   = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }
}
