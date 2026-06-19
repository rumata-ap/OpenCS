using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Пакетная задача поиска плоскости деформаций пластины по строкам ForceSet.ShellItems.
/// Режим определяется настройками CalcSettings:
///   BatchParallel=true  → параллельный Parallel.For, каждый поток на клоне сечения, без тёплого старта;
///   ShellWarmStart=true → последовательный SolveMany, результат строки N → начало строки N+1;
///   (оба false, по умолчанию) → последовательный, каждая строка стартует от упругого приближения.
/// </summary>
public sealed class ShellStrainBatchHandler : ITaskHandler
{
    public string Kind => "shell_strain_state_batch";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException("Требуется контекст с DatabaseService.");

            var plate = ctx.Database.PlateSections.FirstOrDefault(s => s.Id == task.SectionId)
                ?? throw new InvalidOperationException($"Плитное сечение id={task.SectionId} не найдено.");
            var forceSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException($"Набор усилий id={task.ForceSetId} не найден.");
            if (forceSet.ShellItems.Count == 0)
                throw new InvalidOperationException($"Набор усилий «{forceSet.Tag}» не содержит строк для пластин.");

            var (cDiag, rDiag, layerDiags, _) =
                PlateMaterialResolver.Resolve(plate, ctx.Database.Materials, task.CalcType);
            var p       = ShellStrainParams.Parse(task.ParamsJson);
            bool central = settings.NewtonJacobian == "central";
            double hDiff = settings.NewtonDeltaH;

            var items = forceSet.ShellItems;
            int total = items.Count;
            var rows = new object[total];
            var converged = new bool[total];

            if (settings.BatchParallel && total > 1)
            {
                // Параллельный режим всегда без тёплого старта (независимые клоны)
                Parallel.For(0, total, i =>
                {
                    var clone = plate.CloneForCalc();
                    var si = items[i];
                    double[] tgt = { si.Nx, si.Ny, si.Nxy, si.Mx, si.My, si.Mxy };
                    var r = new ShellStrainSolver(clone, cDiag, rDiag, layerDiags,
                        tolRes: p.TolRes, maxIter: p.MaxIter,
                        hDiff: hDiff, centralJacobian: central).Solve(tgt);
                    converged[i] = r.Converged;
                    rows[i] = BuildRow(si.Num, si.Label, r);
                });
            }
            else if (settings.ShellWarmStart)
            {
                // Последовательный режим с тёплым стартом: результат строки N → начало строки N+1
                var solver = new ShellStrainSolver(plate, cDiag, rDiag, layerDiags,
                    tolRes: p.TolRes, maxIter: p.MaxIter,
                    hDiff: hDiff, centralJacobian: central);
                var targets = items.Select(si =>
                    new[] { si.Nx, si.Ny, si.Nxy, si.Mx, si.My, si.Mxy }).ToList();
                var results = solver.SolveMany(targets);
                for (int i = 0; i < total; i++)
                {
                    converged[i] = results[i].Converged;
                    rows[i] = BuildRow(items[i].Num, items[i].Label, results[i]);
                }
            }
            else
            {
                // Последовательный режим без тёплого старта (по умолчанию): каждая строка
                // стартует независимо от упругого приближения
                var solver = new ShellStrainSolver(plate, cDiag, rDiag, layerDiags,
                    tolRes: p.TolRes, maxIter: p.MaxIter,
                    hDiff: hDiff, centralJacobian: central);
                for (int i = 0; i < total; i++)
                {
                    var si = items[i];
                    double[] tgt = { si.Nx, si.Ny, si.Nxy, si.Mx, si.My, si.Mxy };
                    var r = solver.Solve(tgt);
                    converged[i] = r.Converged;
                    rows[i] = BuildRow(si.Num, si.Label, r);
                }
            }

            int convergedCount = converged.Count(c => c);
            bool allOk = convergedCount == total;

            var data = new
            {
                all_converged = allOk,
                converged_count = convergedCount,
                total,
                rows,
            };

            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = allOk ? "ok" : "partial",
                DataJson = JsonSerializer.Serialize(data),
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message }),
            };
        }
    }

    static object BuildRow(int num, string label, ShellStrainSolverResult r) => new
    {
        num,
        label,
        converged = r.Converged,
        iterations = r.Iterations,
        residual = Math.Round(r.Residual, 6),
        status = r.Converged ? "ok" : "not_converged",
    };
}
