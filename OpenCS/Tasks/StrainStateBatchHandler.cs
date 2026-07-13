using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик задачи «Состояние деформаций (весь набор)»: находит плоскость деформаций
/// для каждой строки ForceSet методом Ньютона-Рафсона. Несходимость строки не прерывает пакет.
/// При BatchParallel=true каждый поток работает с клоном сечения.
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
                pool: ctx.Database.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram);

            var items = forceSet.Items;
            int total = items.Count;
            var rowResults  = new object[total];
            var convergedArr = new bool[total];
            int done = 0;

            if (settings.BatchParallel && total > 1)
            {
                Parallel.For(0, total, (i, state) =>
                {
                    if (ctx?.CancellationToken.IsCancellationRequested == true) { state.Stop(); return; }
                    var fi    = items[i];
                    var clone = section.CloneForCalc();
                    var solver = new StrainSolver(clone, task.CalcType,
                        tol:     settings.NewtonTolerance,
                        maxIter: settings.NewtonMaxIter,
                        h:       settings.NewtonDeltaH,
                        centralJacobian: settings.NewtonJacobian == "central");
                    var k = solver.Solve(fi.N, fi.Mx, fi.My);
                    convergedArr[i] = solver.Converged;
                    rowResults[i]   = BuildRow(fi, k, solver);
                    BatchProgress.Report(ctx, ref done, total);
                });
                ctx?.CancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                for (int i = 0; i < total; i++)
                {
                    var fi     = items[i];
                    var solver = new StrainSolver(section, task.CalcType,
                        tol:     settings.NewtonTolerance,
                        maxIter: settings.NewtonMaxIter,
                        h:       settings.NewtonDeltaH,
                        centralJacobian: settings.NewtonJacobian == "central");
                    var k = solver.Solve(fi.N, fi.Mx, fi.My);
                    convergedArr[i] = solver.Converged;
                    rowResults[i]   = BuildRow(fi, k, solver);
                    BatchProgress.Report(ctx, ref done, total);
                }
            }

            int convergedCount = convergedArr.Count(c => c);
            bool allConverged  = convergedCount == total;

            var data = new
            {
                all_converged   = allConverged,
                converged_count = convergedCount,
                total,
                rows = rowResults
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

    static object BuildRow(LoadItem fi, Kurvature k, StrainSolver solver) => new
    {
        label      = fi.Label,
        num        = fi.Num,
        N          = fi.N,
        Mx         = fi.Mx,
        My         = fi.My,
        e0         = Math.Round(k.e0, 8),
        ky         = Math.Round(k.ky, 8),
        kz         = Math.Round(k.kz, 8),
        iterations = solver.Iterations,
        residual   = Math.Round(solver.Residual, 6),
        status     = solver.Converged ? "ok" : "not_converged"
    };
}
