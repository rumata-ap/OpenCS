using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Пакетный двухстадийный расчёт. Этап 2 — набор; этап 1 — набор (попарно по строкам,
/// число строк должно совпадать) или одно усилие (κ1 считается один раз).
/// </summary>
public sealed class TwoStageStrainBatchHandler : ITaskHandler
{
   public string Kind => "two_stage_strain_batch";

   public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                         CalcSettings settings, TaskRunContext? ctx = null)
   {
      var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
      try
      {
         if (ctx?.Database is null)
            throw new InvalidOperationException("Для two_stage_strain_batch требуется контекст с DatabaseService.");
         if (section is not TwoStageSection tss)
            throw new InvalidOperationException("Сечение задачи не является двухстадийным.");

         var p = TwoStageParams.Parse(task.ParamsJson);
         var s1Items = TwoStageForceResolver.Resolve(p.Stage1, ctx.Database.ForceSets);
         var s2Items = TwoStageForceResolver.Resolve(p.Stage2, ctx.Database.ForceSets);

         bool stage1Fixed = s1Items.Count == 1;
         if (!stage1Fixed && s1Items.Count != s2Items.Count)
            throw new InvalidOperationException(
               $"Число строк наборов этапов не совпадает: этап 1 = {s1Items.Count}, этап 2 = {s2Items.Count}.");

         tss.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin, pool: ctx.Database.Diagrams);
         tss.Stage1.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin, pool: ctx.Database.Diagrams);

         int total = s2Items.Count;
         var rows  = new object[total];
         var conv  = new bool[total];

         // κ1 один раз, если этап 1 — одно усилие
         Kurvature? sharedK1 = null;
         if (stage1Fixed)
         {
            var s1Solver = new StrainSolver(tss.Stage1, task.CalcType,
               tol: settings.NewtonTolerance, maxIter: settings.NewtonMaxIter, h: settings.NewtonDeltaH);
            sharedK1 = s1Solver.Solve(s1Items[0].N, s1Items[0].Mx, s1Items[0].My);
         }

         void Solve(int i)
         {
            var clone = (TwoStageSection)tss.CloneForCalc();
            clone.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin, pool: ctx.Database.Diagrams);
            clone.Stage1.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin, pool: ctx.Database.Diagrams);

            Kurvature k1;
            if (sharedK1.HasValue)
            {
               k1 = sharedK1.Value;
            }
            else
            {
               var f1 = s1Items[i];
               var s1Solver = new StrainSolver(clone.Stage1, task.CalcType,
                  tol: settings.NewtonTolerance, maxIter: settings.NewtonMaxIter, h: settings.NewtonDeltaH);
               k1 = s1Solver.Solve(f1.N, f1.Mx, f1.My);
            }
            clone.Stage1Kurvature = k1;

            var f2 = s2Items[i];
            var s2Solver = new StrainSolver(clone, task.CalcType,
               tol: settings.NewtonTolerance, maxIter: settings.NewtonMaxIter, h: settings.NewtonDeltaH);
            var k = s2Solver.Solve(f2.N, f2.Mx, f2.My);

            conv[i] = s2Solver.Converged;
            rows[i] = new
            {
               label = f2.Label,
               N = f2.N, Mx = f2.Mx, My = f2.My,
               e0 = Math.Round(k.e0, 8), ky = Math.Round(k.ky, 8), kz = Math.Round(k.kz, 8),
               iterations = s2Solver.Iterations,
               residual = Math.Round(s2Solver.Residual, 6),
               status = s2Solver.Converged ? "ok" : "not_converged",
               stage1_e0 = Math.Round(k1.e0, 8), stage1_ky = Math.Round(k1.ky, 8), stage1_kz = Math.Round(k1.kz, 8)
            };
         }

         if (settings.BatchParallel && total > 1)
            Parallel.For(0, total, Solve);
         else
            for (int i = 0; i < total; i++) Solve(i);

         int convergedCount = conv.Count(c => c);
         bool allConverged  = convergedCount == total;

         var data = new
         {
            all_converged = allConverged,
            converged_count = convergedCount,
            total,
            rows
         };

         return new CalcResult
         {
            TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
            Status = allConverged ? "ok" : "partial",
            DataJson = JsonSerializer.Serialize(data)
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
