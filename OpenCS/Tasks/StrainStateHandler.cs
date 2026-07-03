using System;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks
{
   /// <summary>
   /// Обработчик задачи «Состояние деформаций»: методом Ньютона находит
   /// плоскость деформаций (e0, ky, kz) при заданных N/Mx/My из LoadItem.
   /// </summary>
   public class StrainStateHandler : ITaskHandler
   {
      public string Kind => "strain_state";

      public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings)
         => Run(task, section, item, settings, null);

      public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx)
      {
         var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
         try
         {
            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx?.Database?.Diagrams);

            double nTarget  = item.N;
            double mxTarget = item.Mx; // LoadItem.Mx → Load.Mx (∫σ·y·dA, момент относительно X)
            double myTarget = item.My; // LoadItem.My → Load.My (∫σ·x·dA, момент относительно Y)

            var solver = new StrainSolver(section, task.CalcType,
                tol: settings.NewtonTolerance,
                maxIter: settings.NewtonMaxIter,
                h: settings.NewtonDeltaH,
                centralJacobian: settings.NewtonJacobian == "central");
            var k      = solver.Solve(nTarget, mxTarget, myTarget);

            var result = section.Integral(k, task.CalcType);

            var data = new
            {
               converged  = solver.Converged,
               iterations = solver.Iterations,
               residual   = Math.Round(solver.Residual, 6),
               e0         = Math.Round(k.e0, 8),
               ky         = Math.Round(k.ky, 8),
               kz         = Math.Round(k.kz, 8),
               N_target   = nTarget,
               Mx_target  = mxTarget,
               My_target  = myTarget,
               N_result   = Math.Round(result.N,  4),
               Mx_result  = Math.Round(result.Mx, 4),
               My_result  = Math.Round(result.My, 4)
            };

            return new CalcResult
            {
               TaskId   = task.Id,
               TaskKind = task.Kind,
               TaskTag  = task.Tag,
               Created  = created,
               Status   = solver.Converged ? "ok" : "not_converged",
               DataJson = JsonSerializer.Serialize(data)
            };
         }
         catch (Exception ex)
         {
            var errData = new { error = ex.Message };
            return new CalcResult
            {
               TaskId   = task.Id,
               TaskKind = task.Kind,
               TaskTag  = task.Tag,
               Created  = created,
               Status   = "error",
               DataJson = JsonSerializer.Serialize(errData)
            };
         }
      }
   }
}
