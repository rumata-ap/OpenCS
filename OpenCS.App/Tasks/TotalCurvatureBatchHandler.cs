using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик задачи «Полная кривизна (весь набор)» по п. 8.2.23–8.2.32 СП 63.13330.
/// </summary>
public sealed class TotalCurvatureBatchHandler : ITaskHandler
{
    /// <inheritdoc/>
    public string Kind => "total_curvature_batch";

    /// <inheritdoc/>
    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
        CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException(Loc.S("TotalCurvature_BatchNeedDatabase"));

            var forceSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException(string.Format(
                    Loc.S("TotalCurvature_ForceSetNotFound"), task.ForceSetId));
            if (forceSet.Items.Count == 0)
                throw new InvalidOperationException(Loc.S("TotalCurvature_ForceSetEmpty"));

            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx.Database.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram, ekbEtaMin: settings.EkbDescEtaMin);

            var parameters = TotalCurvatureTaskParams.Parse(task.ParamsJson);
            var calcCrc = task.CalcType is CalcType.N or CalcType.NL
                ? task.CalcType
                : CalcType.N;
            TotalCurvatureSolver Solver(CrossSection s) => new(s, calcCrc: calcCrc,
                solverTol: settings.NewtonTolerance,
                solverMaxIter: settings.NewtonMaxIter,
                solverH: settings.NewtonDeltaH,
                centralJacobian: settings.NewtonJacobian == "central");
            var shared = Solver(section);

            var items = forceSet.Items;
            var rows = new object[items.Count];
            var convergedArr = new bool[items.Count];
            BatchProgress.ForEachRow(items.Count, settings, ctx, i =>
            {
                var forceItem = items[i];
                // Расчёт меняет состояние сечения (фибры) — в параллели у каждой строки свой клон.
                var solver = settings.BatchParallel ? Solver(section.CloneForCalc()) : shared;
                double nTotal = forceItem.N;
                double mxTotal = forceItem.Mx;
                double myTotal = forceItem.My;
                var (mxLong, myLong) = TotalCurvatureHandler.ResolveLongMoments(
                    parameters, mxTotal, myTotal);
                var result = solver.Compute(nTotal, mxLong, myLong, mxTotal, myTotal);
                convergedArr[i] = result.AllConverged;

                rows[i] = new
                {
                    label = forceItem.Label,
                    num = forceItem.Num,
                    N = Math.Round(nTotal, 4),
                    Mx_long = Math.Round(mxLong, 4),
                    Mx_total = Math.Round(mxTotal, 4),
                    My_long = Math.Round(myLong, 4),
                    My_total = Math.Round(myTotal, 4),
                    cracked = result.Cracked,
                    Mcrc = Math.Round(result.Mcrc, 4),
                    ky_full = Math.Round(result.KyFull, 8),
                    kz_full = Math.Round(result.KzFull, 8),
                    k_full = Math.Round(result.KFull, 8),
                    converged = result.AllConverged
                };
            });

            int convergedCount = convergedArr.Count(c => c);
            bool allConverged = convergedCount == forceSet.Items.Count;
            var data = new
            {
                all_converged = allConverged,
                converged_count = convergedCount,
                total = forceSet.Items.Count,
                rows
            };

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = allConverged ? "ok" : "error",
                DataJson = JsonSerializer.Serialize(data)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }
}
