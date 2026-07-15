using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Обработчик задачи «Момент трещинообразования (весь набор)».</summary>
public sealed class CrackingBatchHandler : ITaskHandler
{
    public string Kind => "cracking_batch";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException("Для cracking_batch требуется контекст с DatabaseService.");

            var forceSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException($"Набор усилий id={task.ForceSetId} не найден.");

            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx.Database.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram);

            var items = forceSet.Items;
            int total = items.Count;
            var rowResults = new object[total];
            var convergedArr = new bool[total];
            var calcCrc = task.CalcType is CalcType.N or CalcType.NL ? task.CalcType : CalcType.N;

            void Solve(int i)
            {
                var fi = items[i];
                var clone = settings.BatchParallel ? section.CloneForCalc() : section;
                double mag = Math.Sqrt(fi.Mx * fi.Mx + fi.My * fi.My);
                double dmx = mag > 1e-12 ? fi.Mx / mag : 1.0;
                double dmy = mag > 1e-12 ? fi.My / mag : 0.0;

                var solver = new CrackingSolver(clone, calcCrc);
                var res = solver.CrackingMoment(fi.N, dmx, dmy);
                double mcrc = Math.Sqrt(res.Mx * res.Mx + res.My * res.My);
                convergedArr[i] = res.Converged;

                rowResults[i] = new
                {
                    label = fi.Label,
                    num = fi.Num,
                    N = fi.N,
                    Mx_crc = Math.Round(res.Mx, 4),
                    My_crc = Math.Round(res.My, 4),
                    Mcrc = Math.Round(mcrc, 4),
                    converged = res.Converged,
                    status = res.Converged ? "ok" : "not_converged"
                };
            }

            if (settings.BatchParallel && total > 1)
                Parallel.For(0, total, Solve);
            else
                for (int i = 0; i < total; i++) Solve(i);

            int convergedCount = convergedArr.Count(c => c);
            bool allConverged = convergedCount == total;

            var data = new
            {
                all_converged = allConverged,
                converged_count = convergedCount,
                total,
                rows = rowResults
            };

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = allConverged ? "ok" : "partial",
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
