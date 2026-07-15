using System;
using System.Linq;
using System.Text.Json;
using CScore;
using CScore.Sp63;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик задачи «Ширина раскрытия трещин» на одну строку усилий («полная» нагрузка).
/// Длительная часть — по CrackWidthTaskParams.ForcesMode: total_only / share / manual / force_item_long.
/// </summary>
public sealed class CrackWidthHandler : ITaskHandler
{
    public string Kind => "crack_width";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx?.Database?.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram);

            var p = CrackWidthTaskParams.Parse(task.ParamsJson);

            double nTotal = item.N, mxTotal = item.Mx, myTotal = item.My;
            double mxLong, myLong;

            switch (p.ForcesMode)
            {
                case "share":
                    mxLong = mxTotal * p.LongShare;
                    myLong = myTotal * p.LongShare;
                    break;
                case "manual":
                    mxLong = p.MxLongManual ?? 0.0;
                    myLong = p.MyLongManual ?? 0.0;
                    break;
                case "force_item_long":
                {
                    if (ctx?.Database is null)
                        throw new InvalidOperationException("Для режима force_item_long требуется контекст с DatabaseService.");
                    if (p.ForceSetLongId is null || p.ForceItemLongId is null)
                        throw new InvalidOperationException("Не выбрана строка длительной нагрузки (force_item_long).");

                    var longSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == p.ForceSetLongId.Value)
                        ?? throw new InvalidOperationException($"Набор усилий id={p.ForceSetLongId} не найден.");
                    var longItem = longSet.Items.FirstOrDefault(i => i.Id == p.ForceItemLongId.Value)
                        ?? throw new InvalidOperationException($"Строка усилия id={p.ForceItemLongId} не найдена в наборе '{longSet.Tag}'.");

                    mxLong = longItem.Mx;
                    myLong = longItem.My;
                    break;
                }
                default: // "total_only" и неизвестные значения
                    mxLong = mxTotal;
                    myLong = myTotal;
                    break;
            }

            double mxLongIn = mxLong, myLongIn = myLong, mxTotalIn = mxTotal, myTotalIn = myTotal;
            object? etaData = null;

            var etaParams = LimitForceParams.Parse(task.ParamsJson);
            if (etaParams.EtaEnabled)
            {
                double psiX = CrackWidthEta.AutoPsi(mxLongIn, mxTotalIn);
                double psiY = CrackWidthEta.AutoPsi(myLongIn, myTotalIn);
                double threshold = etaParams.EtaSlendernessThreshold
                    ?? EccentricityAmplifier.SlendernessThreshold;

                bool ten = settings.ResolveConcreteTension(CalcType.N);
                var strainSolver = new StrainSolver(section, CalcType.N,
                    ten: ten,
                    tol: settings.NewtonTolerance,
                    maxIter: settings.NewtonMaxIter,
                    h: settings.NewtonDeltaH,
                    centralJacobian: settings.NewtonJacobian == "central");

                var wiring = RodEtaWiring.Apply(
                    section, nTotal, mxTotalIn, myTotalIn,
                    etaParams.EtaL0x, etaParams.EtaL0y,
                    psiX, psiY,
                    etaParams.EtaIterative,
                    (mx, my) => strainSolver.Solve(nTotal, mx, my),
                    threshold);

                var scaled = CrackWidthEta.ScaleLongTotal(
                    mxLongIn, mxTotalIn, myLongIn, myTotalIn, wiring.MxEff, wiring.MyEff);
                mxLong = scaled.MxLongEff;
                mxTotal = scaled.MxTotalEff;
                myLong = scaled.MyLongEff;
                myTotal = scaled.MyTotalEff;

                etaData = new
                {
                    mode = etaParams.EtaIterative ? "iterative" : "formula",
                    slendernessThreshold = threshold,
                    psiX,
                    psiY,
                    mxOriginal = mxTotalIn,
                    myOriginal = myTotalIn,
                    mxLongOriginal = mxLongIn,
                    myLongOriginal = myLongIn,
                    l0x = Math.Round(wiring.X.L0, 4),
                    hx = Math.Round(wiring.X.H, 4),
                    slendernessX = wiring.X.H > 1e-9 ? Math.Round(wiring.X.L0 / wiring.X.H, 2) : (double?)null,
                    dX = double.IsFinite(wiring.X.D) ? Math.Round(wiring.X.D, 2) : (double?)null,
                    etaX = Math.Round(wiring.X.Eta, 6),
                    ncrX = double.IsFinite(wiring.X.Ncr) ? Math.Round(wiring.X.Ncr, 4) : (double?)null,
                    slenderX = wiring.X.Slender,
                    stableX = wiring.X.Stable,
                    extrapolationFailedX = wiring.X.ExtrapolationFailed,
                    etaHistoryX = wiring.X.EtaHistory.Select(e => Math.Round(e, 6)).ToArray(),
                    l0y = Math.Round(wiring.Y.L0, 4),
                    hy = Math.Round(wiring.Y.H, 4),
                    slendernessY = wiring.Y.H > 1e-9 ? Math.Round(wiring.Y.L0 / wiring.Y.H, 2) : (double?)null,
                    dY = double.IsFinite(wiring.Y.D) ? Math.Round(wiring.Y.D, 2) : (double?)null,
                    etaY = Math.Round(wiring.Y.Eta, 6),
                    ncrY = double.IsFinite(wiring.Y.Ncr) ? Math.Round(wiring.Y.Ncr, 4) : (double?)null,
                    slenderY = wiring.Y.Slender,
                    stableY = wiring.Y.Stable,
                    extrapolationFailedY = wiring.Y.ExtrapolationFailed,
                    etaHistoryY = wiring.Y.EtaHistory.Select(e => Math.Round(e, 6)).ToArray(),
                };
            }

            var calcCrc = task.CalcType is CalcType.N or CalcType.NL ? task.CalcType : CalcType.N;
            var solver = new CrackWidthSolver(section,
                calcCrc: calcCrc, calcService: CalcType.N,
                calcServiceLong: p.LongPartUseNL ? CalcType.NL : (CalcType?)null,
                acrcUltLong: p.AcrcUltLong, acrcUltShort: p.AcrcUltShort,
                sp63EtaMin: settings.Sp63DescEtaMin);

            var res = solver.Compute(N: nTotal, mxLong: mxLong, mxTotal: mxTotal, myLong: myLong, myTotal: myTotal);

            var data = new
            {
                N = nTotal,
                Mx_long = Math.Round(mxLong, 4),
                Mx_total = Math.Round(mxTotal, 4),
                My_long = Math.Round(myLong, 4),
                My_total = Math.Round(myTotal, 4),
                Mx_long_input = Math.Round(mxLongIn, 4),
                Mx_total_input = Math.Round(mxTotalIn, 4),
                My_long_input = Math.Round(myLongIn, 4),
                My_total_input = Math.Round(myTotalIn, 4),
                cracked = res.Cracked,
                acrc_long = Math.Round(res.AcrcLong, 4),
                acrc_short = Math.Round(res.AcrcShort, 4),
                acrc_ult_long = res.AcrcUltLong,
                acrc_ult_short = res.AcrcUltShort,
                passed_long = res.PassedLong,
                passed_short = res.PassedShort,
                Mcrc = Math.Round(res.Mcrc, 4),
                Mx_crc = Math.Round(res.MxCrc, 4),
                My_crc = Math.Round(res.MyCrc, 4),
                crc_converged = res.CrcConverged,
                eps_max_tension = Math.Round(res.EpsMaxTension, 8),
                eps_tension_limit = Math.Round(res.EpsTensionLimit, 8),
                h0 = Math.Round(res.H0 * 1000.0, 1),
                sigma_s = Math.Round(res.SigmaS / 1000.0, 2),
                psi_s = Math.Round(res.PsiS, 4),
                ls = Math.Round(res.Ls * 1000.0, 2),
                ds_eq = Math.Round(res.DsEq * 1000.0, 2),
                As_tens = Math.Round(res.AsTens * 1e4, 4),
                Abt = Math.Round(res.Abt * 1e4, 2),
                e0 = res.PlaneLong.HasValue ? Math.Round(res.PlaneLong.Value.e0, 8) : (double?)null,
                ky = res.PlaneLong.HasValue ? Math.Round(res.PlaneLong.Value.ky, 8) : (double?)null,
                kz = res.PlaneLong.HasValue ? Math.Round(res.PlaneLong.Value.kz, 8) : (double?)null,
                plane_converged = res.PlaneLong.HasValue,
                eta = etaData
            };

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = (res.PassedLong && res.PassedShort) ? "ok" : "not_passed",
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
