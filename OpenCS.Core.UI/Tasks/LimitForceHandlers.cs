using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Режим масштабирования предельного нагружения.</summary>
enum LimitForceScaleMode
{
   /// <summary>k·(N, Mx, My).</summary>
   All,
   /// <summary>N фикс., k·(Mx, My).</summary>
   Moment,
   /// <summary>k·N, Mx/My фикс.</summary>
   Axial
}

/// <summary>Общая логика задач предельного нагружения.</summary>
static class LimitForceTaskHelper
{
   public static CalcResult RunSingle(
      CalcTask task, CrossSection section, LoadItem item,
      CalcSettings settings, LimitForceScaleMode mode)
   {
      var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
      try
      {
         section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
            rebarDifferentialDiagram: settings.RebarDifferentialDiagram);
         var solver = LimitForceSolvers.Create(section, task.CalcType,
            LimitForceParams.Parse(task.ParamsJson),
            newtonTol: settings.NewtonTolerance,
            newtonMaxIter: settings.NewtonMaxIter,
            ten: settings.ResolveConcreteTension(task.CalcType));

         var res = mode switch
         {
            LimitForceScaleMode.Moment => solver.MomentFactor(item.N, item.Mx, item.My),
            LimitForceScaleMode.Axial  => solver.AxialFactor(item.N, item.Mx, item.My),
            _                          => solver.AllFactor(item.N, item.Mx, item.My),
         };

         return MakeResult(task, created, res, item, section, settings);
      }
      catch (Exception ex)
      {
         return ErrorResult(task, created, ex.Message);
      }
   }

   public static CalcResult RunBatch(
      CalcTask task, CrossSection section, CalcSettings settings, TaskRunContext? ctx,
      LimitForceScaleMode mode)
   {
      var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
      try
      {
         if (ctx?.Database is null)
            throw new InvalidOperationException(
               "Для пакетных задач предельного нагружения требуется контекст с DatabaseService.");

         var forceSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
            ?? throw new InvalidOperationException(
               $"Набор усилий id={task.ForceSetId} не найден.");

         section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
            rebarDifferentialDiagram: settings.RebarDifferentialDiagram);
         var parameters = LimitForceParams.Parse(task.ParamsJson);
         var items = forceSet.Items;
         int total = items.Count;
         var rowResults = new object[total];
         var convergedArr = new bool[total];
         int done = 0;

         if (settings.BatchParallel && total > 1)
         {
            Parallel.For(0, total, (i, state) =>
            {
               if (ctx?.CancellationToken.IsCancellationRequested == true) { state.Stop(); return; }
               var fi = items[i];
               var clone = section.CloneForCalc();
               var solver = LimitForceSolvers.Create(clone, task.CalcType, parameters,
                  newtonTol: settings.NewtonTolerance,
                  newtonMaxIter: settings.NewtonMaxIter,
                  ten: settings.ResolveConcreteTension(task.CalcType));
               var res = Solve(solver, fi, mode);
               convergedArr[i] = res.Converged;
               rowResults[i] = BuildRow(fi, res, parameters);
               BatchProgress.Report(ctx, ref done, total);
            });
            ctx?.CancellationToken.ThrowIfCancellationRequested();
         }
         else
         {
            var solver = LimitForceSolvers.Create(section, task.CalcType, parameters,
               newtonTol: settings.NewtonTolerance,
               newtonMaxIter: settings.NewtonMaxIter,
               ten: settings.ResolveConcreteTension(task.CalcType));
            for (int i = 0; i < total; i++)
            {
               var fi = items[i];
               var res = Solve(solver, fi, mode);
               convergedArr[i] = res.Converged;
               rowResults[i] = BuildRow(fi, res, parameters);
               BatchProgress.Report(ctx, ref done, total);
            }
         }

         int convergedCount = convergedArr.Count(c => c);
         bool allConverged = convergedCount == total;

         return new CalcResult
         {
            TaskId   = task.Id,
            TaskKind = task.Kind,
            TaskTag  = task.Tag,
            Created  = created,
            Status   = allConverged ? "ok" : "partial",
            DataJson = JsonSerializer.Serialize(new
            {
               all_converged   = allConverged,
               converged_count = convergedCount,
               total,
               solver          = parameters.Solver,
               rows            = rowResults
            })
         };
      }
      catch (Exception ex)
      {
         return ErrorResult(task, created, ex.Message);
      }
   }

   static LimitForceResult Solve(ILimitForceSolver solver, LoadItem fi, LimitForceScaleMode mode)
      => mode switch
      {
         LimitForceScaleMode.Moment => solver.MomentFactor(fi.N, fi.Mx, fi.My),
         LimitForceScaleMode.Axial  => solver.AxialFactor(fi.N, fi.Mx, fi.My),
         _                          => solver.AllFactor(fi.N, fi.Mx, fi.My),
      };

   static CalcResult MakeResult(
      CalcTask task, string created, LimitForceResult res, LoadItem item,
      CrossSection section, CalcSettings settings)
   {
      var k = res.StrainPlane ?? new Kurvature();
      double nRes = res.NLimit, mxRes = res.MxLimit, myRes = res.MyLimit;
      if (res.StrainPlane.HasValue)
      {
         var integral = section.Integral(k, task.CalcType);
         nRes  = integral.N;
         mxRes = integral.Mx;
         myRes = integral.My;
      }

      var parameters = LimitForceParams.Parse(task.ParamsJson);
      var data = BuildData(res, item, parameters, k, nRes, mxRes, myRes);

      return new CalcResult
      {
         TaskId   = task.Id,
         TaskKind = task.Kind,
         TaskTag  = task.Tag,
         Created  = created,
         Status   = res.Converged ? "ok" : "not_converged",
         DataJson = JsonSerializer.Serialize(data)
      };
   }

   static object BuildData(
      LimitForceResult res, LoadItem item, LimitForceParams parameters, Kurvature k,
      double nRes, double mxRes, double myRes)
   {
      return new
      {
         solver_method     = parameters.Solver,
         converged         = res.Converged,
         iterations        = res.Iterations,
         newton_iterations = res.NewtonIterations,
         factor            = Math.Round(res.Factor, 6),
         utilization       = Math.Round(res.Utilization, 6),
         governing         = res.Governing,
         N_target          = item.N,
         Mx_target         = item.Mx,
         My_target         = item.My,
         N_limit           = Math.Round(res.NLimit, 4),
         Mx_limit          = Math.Round(res.MxLimit, 4),
         My_limit          = Math.Round(res.MyLimit, 4),
         e0                = Math.Round(k.e0, 8),
         ky                = Math.Round(k.ky, 8),
         kz                = Math.Round(k.kz, 8),
         eps_contour_min   = Math.Round(res.EpsContourMin, 8),
         eps_cu            = Math.Round(res.EpsCu, 8),
         eps_rebar_max     = res.EpsRebarMax.HasValue ? Math.Round(res.EpsRebarMax.Value, 8) : (double?)null,
         eps_su            = res.EpsSu.HasValue ? Math.Round(res.EpsSu.Value, 8) : (double?)null,
         N_result          = Math.Round(nRes, 4),
         Mx_result         = Math.Round(mxRes, 4),
         My_result         = Math.Round(myRes, 4),
         eta               = BuildEtaJson(res.Eta, parameters.EtaIterative,
            parameters.EtaSlendernessThreshold ?? CScore.Sp63.EccentricityAmplifier.SlendernessThreshold,
            res.MxLimit, res.MyLimit),
      };
   }

   /// <summary>
   /// Диагностика η (п. 8.1.15) для найденной предельной точки — см.
   /// LimitForceResult.Eta. Схема полей совпадает с StrainStateHandler.etaData,
   /// чтобы результат читал тот же StrainSummaryVM/StrainSummaryBody (см.
   /// LimitForceSummaryView.xaml: local:StrainSummaryBody DataContext=StrainPart).
   /// mxOriginal/myOriginal — найденный предельный момент (Mx_limit/My_limit,
   /// т.е. ДО усиления η — усиленное значение использовалось только для
   /// проверки вместимости сечения на каждом пробном k).
   /// </summary>
   static object? BuildEtaJson(
      CScore.Sp63.RodEtaWiring.Result? etaOpt, bool iterative, double threshold,
      double mxOriginal, double myOriginal)
   {
      if (etaOpt is not { } eta) return null;
      return new
      {
         mode = iterative ? "iterative" : "formula",
         slendernessThreshold = threshold,
         mxOriginal,
         myOriginal,
         l0x = Math.Round(eta.X.L0, 4),
         hx  = Math.Round(eta.X.H,  4),
         slendernessX = eta.X.H > 1e-9 ? Math.Round(eta.X.L0 / eta.X.H, 2) : (double?)null,
         dX = double.IsFinite(eta.X.D) ? Math.Round(eta.X.D, 2) : (double?)null,
         etaX = Math.Round(eta.X.Eta, 6),
         ncrX = double.IsFinite(eta.X.Ncr) ? Math.Round(eta.X.Ncr, 4) : (double?)null,
         slenderX = eta.X.Slender,
         stableX  = eta.X.Stable,
         extrapolationFailedX = eta.X.ExtrapolationFailed,
         etaHistoryX = eta.X.EtaHistory.Select(e => Math.Round(e, 6)).ToArray(),
         l0y = Math.Round(eta.Y.L0, 4),
         hy  = Math.Round(eta.Y.H,  4),
         slendernessY = eta.Y.H > 1e-9 ? Math.Round(eta.Y.L0 / eta.Y.H, 2) : (double?)null,
         dY = double.IsFinite(eta.Y.D) ? Math.Round(eta.Y.D, 2) : (double?)null,
         etaY = Math.Round(eta.Y.Eta, 6),
         ncrY = double.IsFinite(eta.Y.Ncr) ? Math.Round(eta.Y.Ncr, 4) : (double?)null,
         slenderY = eta.Y.Slender,
         stableY  = eta.Y.Stable,
         extrapolationFailedY = eta.Y.ExtrapolationFailed,
         etaHistoryY = eta.Y.EtaHistory.Select(e => Math.Round(e, 6)).ToArray(),
      };
   }

   static object BuildRow(LoadItem fi, LimitForceResult res, LimitForceParams parameters)
   {
      var k = res.StrainPlane ?? new Kurvature();
      return new
      {
         label             = fi.Label,
         num               = fi.Num,
         N                 = fi.N,
         Mx                = fi.Mx,
         My                = fi.My,
         factor            = Math.Round(res.Factor, 6),
         utilization       = Math.Round(res.Utilization, 6),
         governing         = res.Governing,
         N_limit           = Math.Round(res.NLimit, 4),
         Mx_limit          = Math.Round(res.MxLimit, 4),
         My_limit          = Math.Round(res.MyLimit, 4),
         e0                = Math.Round(k.e0, 8),
         ky                = Math.Round(k.ky, 8),
         kz                = Math.Round(k.kz, 8),
         iterations        = res.Iterations,
         newton_iterations = res.NewtonIterations,
         status            = res.Converged ? "ok" : "not_converged",
         eta               = BuildEtaJson(res.Eta, parameters.EtaIterative,
            parameters.EtaSlendernessThreshold ?? CScore.Sp63.EccentricityAmplifier.SlendernessThreshold,
            res.MxLimit, res.MyLimit),
      };
   }

   static CalcResult ErrorResult(CalcTask task, string created, string message)
      => new()
      {
         TaskId   = task.Id,
         TaskKind = task.Kind,
         TaskTag  = task.Tag,
         Created  = created,
         Status   = "error",
         DataJson = JsonSerializer.Serialize(new { error = message })
      };
}

/// <summary>Предельное пропорциональное нагружение k·(N, Mx, My).</summary>
public sealed class LimitForceHandler : ITaskHandler
{
   public string Kind => "limit_force";
   public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
      CalcSettings settings, TaskRunContext? ctx = null)
      => LimitForceTaskHelper.RunSingle(task, section, item, settings, LimitForceScaleMode.All);
}

/// <summary>Предельный момент при фиксированном N.</summary>
public sealed class LimitMomentHandler : ITaskHandler
{
   public string Kind => "limit_moment";
   public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
      CalcSettings settings, TaskRunContext? ctx = null)
      => LimitForceTaskHelper.RunSingle(task, section, item, settings, LimitForceScaleMode.Moment);
}

/// <summary>Предельная продольная сила при фиксированных моментах.</summary>
public sealed class LimitAxialHandler : ITaskHandler
{
   public string Kind => "limit_axial";
   public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
      CalcSettings settings, TaskRunContext? ctx = null)
      => LimitForceTaskHelper.RunSingle(task, section, item, settings, LimitForceScaleMode.Axial);
}

/// <summary>Пакет: k·(N, Mx, My) по всему набору усилий.</summary>
public sealed class LimitForceBatchHandler : ITaskHandler
{
   public string Kind => "limit_force_batch";
   public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
      CalcSettings settings, TaskRunContext? ctx = null)
      => LimitForceTaskHelper.RunBatch(task, section, settings, ctx, LimitForceScaleMode.All);
}

/// <summary>Пакет: k·(Mx, My) при фиксированном N.</summary>
public sealed class LimitMomentBatchHandler : ITaskHandler
{
   public string Kind => "limit_moment_batch";
   public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
      CalcSettings settings, TaskRunContext? ctx = null)
      => LimitForceTaskHelper.RunBatch(task, section, settings, ctx, LimitForceScaleMode.Moment);
}

/// <summary>Пакет: k·N при фиксированных моментах.</summary>
public sealed class LimitAxialBatchHandler : ITaskHandler
{
   public string Kind => "limit_axial_batch";
   public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
      CalcSettings settings, TaskRunContext? ctx = null)
      => LimitForceTaskHelper.RunBatch(task, section, settings, ctx, LimitForceScaleMode.Axial);
}
