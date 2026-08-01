using System;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Детальный двухстадийный расчёт: этап 1 решается под своим усилием → κ1 фиксируется,
/// затем составное сечение решается под усилием этапа 2 с учётом κ1.
/// </summary>
public sealed class TwoStageStrainHandler : ITaskHandler
{
   public string Kind => "two_stage_strain";

   public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                         CalcSettings settings, TaskRunContext? ctx = null)
   {
      var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
      try
      {
         if (ctx?.Database is null)
            throw new InvalidOperationException("Для two_stage_strain требуется контекст с DatabaseService.");
         if (section is not TwoStageSection tss)
            throw new InvalidOperationException("Сечение задачи не является двухстадийным.");

         var p = TwoStageParams.Parse(task.ParamsJson);
         var f1 = TwoStageForceResolver.Resolve(p.Stage1, ctx.Database.ForceSets).Single();
         var f2 = TwoStageForceResolver.Resolve(p.Stage2, ctx.Database.ForceSets).Single();

         tss.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin, pool: ctx.Database.Diagrams,
            rebarDifferentialDiagram: settings.RebarDifferentialDiagram);
         tss.Stage1.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin, pool: ctx.Database.Diagrams,
            rebarDifferentialDiagram: settings.RebarDifferentialDiagram);
         bool ten = settings.ResolveConcreteTension(task.CalcType);

         // Этап 1: решаем сечение этапа 1 как обычное CrossSection → κ1
         var s1Solver = new StrainSolver(tss.Stage1, task.CalcType, ten: ten,
            tol: settings.NewtonTolerance, maxIter: settings.NewtonMaxIter, h: settings.NewtonDeltaH);
         var k1 = s1Solver.Solve(f1.N, f1.Mx, f1.My);
         tss.Stage1Kurvature = k1;

         // Этап 2: составное сечение под полным усилием этапа 2
         var s2Solver = new StrainSolver(tss, task.CalcType, ten: ten,
            tol: settings.NewtonTolerance, maxIter: settings.NewtonMaxIter, h: settings.NewtonDeltaH);
         var k = s2Solver.Solve(f2.N, f2.Mx, f2.My);
         var res    = tss.Integral(k, task.CalcType, ten);
         var resS1  = tss.Stage1.Integral(k1, task.CalcType, ten);

         var data = new
         {
            converged  = s2Solver.Converged,
            iterations = s2Solver.Iterations,
            residual   = Math.Round(s2Solver.Residual, 6),
            e0 = Math.Round(k.e0, 8), ky = Math.Round(k.ky, 8), kz = Math.Round(k.kz, 8),
            N_target = f2.N, Mx_target = f2.Mx, My_target = f2.My,
            N_result = Math.Round(res.N, 4), Mx_result = Math.Round(res.Mx, 4), My_result = Math.Round(res.My, 4),
            stage1_converged  = s1Solver.Converged,
            stage1_iterations = s1Solver.Iterations,
            stage1_residual   = Math.Round(s1Solver.Residual, 6),
            stage1_e0 = Math.Round(k1.e0, 8), stage1_ky = Math.Round(k1.ky, 8), stage1_kz = Math.Round(k1.kz, 8),
            stage1_N_target  = f1.N, stage1_Mx_target = f1.Mx, stage1_My_target = f1.My,
            stage1_N_result  = Math.Round(resS1.N, 4),
            stage1_Mx_result = Math.Round(resS1.Mx, 4),
            stage1_My_result = Math.Round(resS1.My, 4)
         };

          return new CalcResult
          {
             TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
             Status = s2Solver.Converged && s1Solver.Converged ? "ok" : "not_converged",
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
