using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CScore;
using CScore.ParametricRc;
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
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram, ekbEtaMin: settings.EkbDescEtaMin);
            bool ten = settings.ResolveConcreteTension(task.CalcType);

            var etaParams = LimitForceParams.Parse(task.ParamsJson);
            double etaSlendernessThreshold = etaParams.EtaSlendernessThreshold
                ?? CScore.Sp63.EccentricityAmplifier.SlendernessThreshold;

            var items = forceSet.Items;
            int total = items.Count;
            var rowResults  = new object[total];
            var convergedArr = new bool[total];
            var applicability = items
                .Select(fi => ParametricRebarApplicability.Evaluate(
                    section, task.Kind, fi))
                .ToArray();
            int done = 0;

            if (settings.BatchParallel && total > 1)
            {
                Parallel.For(0, total, (i, state) =>
                {
                    if (ctx?.CancellationToken.IsCancellationRequested == true) { state.Stop(); return; }
                    var fi    = items[i];
                    if (!applicability[i].IsApplicable)
                    {
                        rowResults[i] = BuildNotApplicableRow(fi, applicability[i].ReasonCode!);
                        BatchProgress.Report(ctx, ref done, total);
                        return;
                    }
                    var clone = section.CloneForCalc();
                    var solver = new StrainSolver(clone, task.CalcType, ten: ten,
                        tol:     settings.NewtonTolerance,
                        maxIter: settings.NewtonMaxIter,
                        h:       settings.NewtonDeltaH,
                        centralJacobian: settings.NewtonJacobian == "central");

                    var (mxTarget, myTarget, etaRowData) = ApplyEta(
                        clone, etaParams, etaSlendernessThreshold, fi, solver);

                    var k = solver.Solve(fi.N, mxTarget, myTarget);
                    convergedArr[i] = solver.Converged;
                    rowResults[i]   = BuildRow(fi, k, solver, mxTarget, myTarget, etaRowData);
                    BatchProgress.Report(ctx, ref done, total);
                });
                ctx?.CancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                for (int i = 0; i < total; i++)
                {
                    var fi     = items[i];
                    if (!applicability[i].IsApplicable)
                    {
                        rowResults[i] = BuildNotApplicableRow(fi, applicability[i].ReasonCode!);
                        BatchProgress.Report(ctx, ref done, total);
                        continue;
                    }
                    var solver = new StrainSolver(section, task.CalcType, ten: ten,
                        tol:     settings.NewtonTolerance,
                        maxIter: settings.NewtonMaxIter,
                        h:       settings.NewtonDeltaH,
                        centralJacobian: settings.NewtonJacobian == "central");

                    var (mxTarget, myTarget, etaRowData) = ApplyEta(
                        section, etaParams, etaSlendernessThreshold, fi, solver);

                    var k = solver.Solve(fi.N, mxTarget, myTarget);
                    convergedArr[i] = solver.Converged;
                    rowResults[i]   = BuildRow(fi, k, solver, mxTarget, myTarget, etaRowData);
                    BatchProgress.Report(ctx, ref done, total);
                }
            }

            int convergedCount = convergedArr.Count(c => c);
            int notApplicableCount = applicability.Count(a => !a.IsApplicable);
            bool allConverged  = notApplicableCount == 0 && convergedCount == total;
            string status = total > 0 && notApplicableCount == total
                ? "not_applicable"
                : notApplicableCount > 0 ? "partial"
                : allConverged ? "ok" : "partial";

            var data = new
            {
                all_converged   = allConverged,
                converged_count = convergedCount,
                not_applicable_count = notApplicableCount,
                total,
                rows = rowResults
            };

            return new CalcResult
            {
                TaskId   = task.Id,
                TaskKind = task.Kind,
                TaskTag  = task.Tag,
                Created  = created,
                Status   = status,
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

    static object BuildRow(LoadItem fi, Kurvature k, StrainSolver solver,
        double mxTarget, double myTarget, object? eta) => new
    {
        label      = fi.Label,
        num        = fi.Num,
        N          = fi.N,
        Mx         = fi.Mx,
        My         = fi.My,
        MxTarget   = Math.Round(mxTarget, 4),
        MyTarget   = Math.Round(myTarget, 4),
        e0         = Math.Round(k.e0, 8),
        ky         = Math.Round(k.ky, 8),
        kz         = Math.Round(k.kz, 8),
        iterations = solver.Iterations,
        residual   = Math.Round(solver.Residual, 6),
        status     = solver.Converged ? "ok" : "not_converged",
        eta
    };

    static object BuildNotApplicableRow(LoadItem fi, string reasonCode) => new
    {
        label = fi.Label,
        num = fi.Num,
        N = fi.N,
        Mx = fi.Mx,
        My = fi.My,
        MxTarget = (double?)null,
        MyTarget = (double?)null,
        e0 = (double?)null,
        ky = (double?)null,
        kz = (double?)null,
        iterations = (int?)null,
        residual = (double?)null,
        status = "not_applicable",
        reason_code = reasonCode,
        eta = (object?)null
    };

    /// <summary>
    /// Усиливает Mx/My текущей строки по п. 8.1.15 (η), если задача это включает.
    /// Работает с локальным для итерации <paramref name="section"/> (клон при
    /// параллельном режиме) и <paramref name="solver"/> — потокобезопасно.
    /// </summary>
    static (double mxTarget, double myTarget, object? eta) ApplyEta(
        CrossSection section, LimitForceParams etaParams, double slendernessThreshold,
        LoadItem fi, StrainSolver solver)
    {
        if (!etaParams.EtaEnabled)
            return (fi.Mx, fi.My, null);

        var wiring = CScore.Sp63.RodEtaWiring.Apply(
            section, fi.N, fi.Mx, fi.My,
            etaParams.EtaL0x, etaParams.EtaL0y,
            etaParams.EtaPsiX ?? 1.0, etaParams.EtaPsiY ?? 1.0,
            etaParams.EtaIterative,
            (mx, my) => solver.Solve(fi.N, mx, my),
            slendernessThreshold);

        var etaData = new
        {
            mode         = etaParams.EtaIterative ? "iterative" : "formula",
            slendernessThreshold,
            l0x          = StrainStateJsonHelper.FiniteRounded(wiring.X.L0, 4),
            hx           = StrainStateJsonHelper.FiniteRounded(wiring.X.H,  4),
            ix  = StrainStateJsonHelper.FiniteRounded(wiring.X.I, 4),
            radiusFallbackX = wiring.X.RadiusFromBoundingBox,
            slendernessX = wiring.X.I > 1e-9
                ? StrainStateJsonHelper.FiniteRounded(wiring.X.L0 / wiring.X.I, 2)
                : (double?)null,
            dX           = StrainStateJsonHelper.FiniteRounded(wiring.X.D, 2),
            etaX         = StrainStateJsonHelper.FiniteRounded(wiring.X.Eta, 6),
            ncrX         = StrainStateJsonHelper.FiniteRounded(wiring.X.Ncr, 4),
            slenderX     = wiring.X.Slender,
            stableX      = wiring.X.Stable,
            extrapolationFailedX = wiring.X.ExtrapolationFailed,
            l0y          = StrainStateJsonHelper.FiniteRounded(wiring.Y.L0, 4),
            hy           = StrainStateJsonHelper.FiniteRounded(wiring.Y.H,  4),
            iy  = StrainStateJsonHelper.FiniteRounded(wiring.Y.I, 4),
            radiusFallbackY = wiring.Y.RadiusFromBoundingBox,
            slendernessY = wiring.Y.I > 1e-9
                ? StrainStateJsonHelper.FiniteRounded(wiring.Y.L0 / wiring.Y.I, 2)
                : (double?)null,
            dY           = StrainStateJsonHelper.FiniteRounded(wiring.Y.D, 2),
            etaY         = StrainStateJsonHelper.FiniteRounded(wiring.Y.Eta, 6),
            ncrY         = StrainStateJsonHelper.FiniteRounded(wiring.Y.Ncr, 4),
            slenderY     = wiring.Y.Slender,
            stableY      = wiring.Y.Stable,
            extrapolationFailedY = wiring.Y.ExtrapolationFailed,
        };

        return (wiring.MxEff, wiring.MyEff, etaData);
    }
}
