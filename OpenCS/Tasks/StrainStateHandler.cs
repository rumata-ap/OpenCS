using System;
using System.Linq;
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
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram, ekbEtaMin: settings.EkbDescEtaMin);

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
               double slendernessThreshold = etaParams.EtaSlendernessThreshold
                   ?? CScore.Sp63.EccentricityAmplifier.SlendernessThreshold;

               var wiring = CScore.Sp63.RodEtaWiring.Apply(
                   section, nTarget, mxOriginal, myOriginal,
                   etaParams.EtaL0x, etaParams.EtaL0y,
                   etaParams.EtaPsiX ?? 1.0, etaParams.EtaPsiY ?? 1.0,
                   etaParams.EtaIterative,
                   (mx, my) => solver.Solve(nTarget, mx, my),
                   slendernessThreshold);

               mxTarget = wiring.MxEff;
               myTarget = wiring.MyEff;
               etaData = new
               {
                  mode       = etaParams.EtaIterative ? "iterative" : "formula",
                  slendernessThreshold,
                  mxOriginal,
                  myOriginal,
                  l0x              = StrainStateJsonHelper.FiniteRounded(wiring.X.L0, 4),
                  hx               = StrainStateJsonHelper.FiniteRounded(wiring.X.H,  4),
                  slendernessX     = wiring.X.H > 1e-9
                     ? StrainStateJsonHelper.FiniteRounded(wiring.X.L0 / wiring.X.H, 2)
                     : (double?)null,
                  dX               = double.IsFinite(wiring.X.D) ? Math.Round(wiring.X.D, 2) : (double?)null,
                  etaX             = StrainStateJsonHelper.FiniteRounded(wiring.X.Eta, 6),
                  ncrX             = double.IsFinite(wiring.X.Ncr) ? Math.Round(wiring.X.Ncr, 4) : (double?)null,
                  slenderX         = wiring.X.Slender,
                  stableX          = wiring.X.Stable,
                  extrapolationFailedX = wiring.X.ExtrapolationFailed,
                  etaHistoryX      = StrainStateJsonHelper.FiniteRoundedArray(wiring.X.EtaHistory, 6),
                  l0y              = StrainStateJsonHelper.FiniteRounded(wiring.Y.L0, 4),
                  hy               = StrainStateJsonHelper.FiniteRounded(wiring.Y.H,  4),
                  slendernessY     = wiring.Y.H > 1e-9
                     ? StrainStateJsonHelper.FiniteRounded(wiring.Y.L0 / wiring.Y.H, 2)
                     : (double?)null,
                  dY               = double.IsFinite(wiring.Y.D) ? Math.Round(wiring.Y.D, 2) : (double?)null,
                  etaY             = StrainStateJsonHelper.FiniteRounded(wiring.Y.Eta, 6),
                  ncrY             = double.IsFinite(wiring.Y.Ncr) ? Math.Round(wiring.Y.Ncr, 4) : (double?)null,
                  slenderY         = wiring.Y.Slender,
                  stableY          = wiring.Y.Stable,
                  extrapolationFailedY = wiring.Y.ExtrapolationFailed,
                  etaHistoryY      = StrainStateJsonHelper.FiniteRoundedArray(wiring.Y.EtaHistory, 6),
               };
            }

            var k      = solver.Solve(nTarget, mxTarget, myTarget);
            var result = section.Integral(k, task.CalcType, ten);
            var prestress = section.PrestressActions(null, task.CalcType, ten);
            var stiffness = section.CalculateSecantStiffness(k, task.CalcType, ten);
            var jacobian = solver.EvaluateJacobian(k);
            section.SetEps(k, task.CalcType, ten);
            var extrema = CalculateExtrema(section);
            var rebar = section.EnumerateAreas(k)
               .SelectMany(pair => pair.area.Fibers
                  .Where(fiber => fiber.TypeFiber == FiberType.point)
                  .Select(fiber => new
                  {
                     group = pair.area.Tag,
                     material = pair.area.Material?.Tag ?? "",
                     x_mm = SafeRound(fiber.X * 1000.0, 6),
                     y_mm = SafeRound(fiber.Y * 1000.0, 6),
                     diameter_mm = SafeRound(fiber.Diameter * 1000.0, 6),
                     area_mm2 = SafeRound(fiber.Area * 1e6, 6),
                     eps = SafeRound(fiber.Eps, 12),
                     sigma_mpa = SafeRound(fiber.Sig / 1000.0, 6),
                     e_sec_mpa = SafeRound(fiber.E / 1000.0, 6)
                  }))
               .Select((fiber, index) => new
               {
                  num = index + 1,
                  fiber.group,
                  fiber.material,
                  fiber.x_mm,
                  fiber.y_mm,
                  fiber.diameter_mm,
                  fiber.area_mm2,
                  fiber.eps,
                  fiber.sigma_mpa,
                  fiber.e_sec_mpa
               })
               .ToArray();

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
               formula_version = "SP63.13330.2021/8.1",
               stiffness = new
               {
                  source = stiffness.Source,
                  d11 = SafeRound(stiffness.Matrix.D11),
                  d12 = SafeRound(stiffness.Matrix.D12),
                  d13 = SafeRound(stiffness.Matrix.D13),
                  d21 = SafeRound(stiffness.Matrix.D21),
                  d22 = SafeRound(stiffness.Matrix.D22),
                  d23 = SafeRound(stiffness.Matrix.D23),
                  d31 = SafeRound(stiffness.Matrix.D31),
                  d32 = SafeRound(stiffness.Matrix.D32),
                  d33 = SafeRound(stiffness.Matrix.D33)
               },
               jacobian = new
               {
                  rows = jacobian.Rows,
                  columns = jacobian.Columns,
                  scheme = jacobian.Scheme,
                  h = SafeRound(jacobian.Step, 12),
                  values = jacobian.Values
                     .Select(row => row.Select(value => SafeRound(value, 12)).ToArray())
                     .ToArray()
               },
               equilibrium = new
               {
                  n = SafeRound(result.N),
                  mx = SafeRound(result.Mx),
                  my = SafeRound(result.My)
               },
               extrema = new
               {
                  eps_b_min = SafeRound(extrema.ConcreteMin, 12),
                  eps_b_max = SafeRound(extrema.ConcreteMax, 12),
                  eps_s_min = SafeRound(extrema.SteelMin, 12),
                  eps_s_max = SafeRound(extrema.SteelMax, 12)
               },
               section = new
               {
                  id = section.Id,
                  num = section.Num,
                  tag = section.Tag,
                  description = section.Description ?? ""
               },
               rebar,
               prestress  = PrestressActionsJsonModel.From(prestress),
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

      static (double ConcreteMin, double ConcreteMax, double SteelMin, double SteelMax)
         CalculateExtrema(CrossSection section)
      {
         var concrete = new List<double>();
         var steel = new List<double>();
         foreach (var area in section.Areas)
         {
            if (!MaterialArea.IsCalcActive(area)) continue;
            var values = area.Fibers.Select(f => f.Eps + f.Eps_p);
            if (area.Hull != null)
               values = values.Concat(area.Hull.Points.Select(p => p.Eps + p.Eps_p));

            var target = area.Material?.Type == MatType.Concrete ? concrete : steel;
            target.AddRange(values.Where(double.IsFinite));
         }

         return (
            concrete.Count > 0 ? concrete.Min() : 0.0,
            concrete.Count > 0 ? concrete.Max() : 0.0,
            steel.Count > 0 ? steel.Min() : 0.0,
            steel.Count > 0 ? steel.Max() : 0.0);
      }

      static double SafeRound(double value, int digits = 8)
         => double.IsFinite(value) ? Math.Round(value, digits) : 0.0;
   }
}
