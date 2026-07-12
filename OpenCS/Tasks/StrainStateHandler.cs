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
                pool: ctx?.Database?.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram);

            double nTarget    = item.N;
            double mxOriginal = item.Mx; // LoadItem.Mx → Load.Mx (∫σ·y·dA, момент относительно X)
            double myOriginal = item.My; // LoadItem.My → Load.My (∫σ·x·dA, момент относительно Y)

            bool ten = settings.ResolveConcreteTension(task.CalcType);
            var solver = new StrainSolver(section, task.CalcType,
                ten: ten,
                tol: settings.NewtonTolerance,
                maxIter: settings.NewtonMaxIter,
                h: settings.NewtonDeltaH,
                centralJacobian: settings.NewtonJacobian == "central");

            double mxTarget = mxOriginal;
            double myTarget = myOriginal;
            object? etaData = null;

            var etaParams = LimitForceParams.Parse(task.ParamsJson);
            if (etaParams.EtaEnabled)
            {
               var wiring = CScore.Sp63.RodEtaWiring.Apply(
                   section, nTarget, mxOriginal, myOriginal,
                   etaParams.EtaL0x ?? 0, etaParams.EtaL0y ?? 0,
                   etaParams.EtaM1lx ?? Math.Abs(mxOriginal), etaParams.EtaM1ly ?? Math.Abs(myOriginal),
                   etaParams.EtaIterative,
                   (mx, my) => solver.Solve(nTarget, mx, my));

               mxTarget = wiring.MxEff;
               myTarget = wiring.MyEff;
               etaData = new
               {
                  mode       = etaParams.EtaIterative ? "iterative" : "formula",
                  mxOriginal,
                  myOriginal,
                  l0x              = Math.Round(wiring.X.L0, 4),
                  hx               = Math.Round(wiring.X.H,  4),
                  slendernessX     = wiring.X.H > 1e-9 ? Math.Round(wiring.X.L0 / wiring.X.H, 2) : (double?)null,
                  dX               = double.IsFinite(wiring.X.D) ? Math.Round(wiring.X.D, 2) : (double?)null,
                  etaX             = Math.Round(wiring.X.Eta, 6),
                  ncrX             = double.IsFinite(wiring.X.Ncr) ? Math.Round(wiring.X.Ncr, 4) : (double?)null,
                  slenderX         = wiring.X.Slender,
                  stableX          = wiring.X.Stable,
                  extrapolationFailedX = wiring.X.ExtrapolationFailed,
                  l0y              = Math.Round(wiring.Y.L0, 4),
                  hy               = Math.Round(wiring.Y.H,  4),
                  slendernessY     = wiring.Y.H > 1e-9 ? Math.Round(wiring.Y.L0 / wiring.Y.H, 2) : (double?)null,
                  dY               = double.IsFinite(wiring.Y.D) ? Math.Round(wiring.Y.D, 2) : (double?)null,
                  etaY             = Math.Round(wiring.Y.Eta, 6),
                  ncrY             = double.IsFinite(wiring.Y.Ncr) ? Math.Round(wiring.Y.Ncr, 4) : (double?)null,
                  slenderY         = wiring.Y.Slender,
                  stableY          = wiring.Y.Stable,
                  extrapolationFailedY = wiring.Y.ExtrapolationFailed,
               };
            }

            var k      = solver.Solve(nTarget, mxTarget, myTarget);
            var result = section.Integral(k, task.CalcType, ten);

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
               My_result  = Math.Round(result.My, 4),
               eta        = etaData
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
