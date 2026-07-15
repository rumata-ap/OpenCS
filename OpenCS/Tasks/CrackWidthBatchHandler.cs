using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CScore;
using CScore.Sp63;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Обработчик задачи «Ширина раскрытия трещин (весь набор)».</summary>
public sealed class CrackWidthBatchHandler : ITaskHandler
{
    public string Kind => "crack_width_batch";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException("Для crack_width_batch требуется контекст с DatabaseService.");

            var forceSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException($"Набор усилий id={task.ForceSetId} не найден.");
            if (forceSet.Items.Count == 0)
                throw new InvalidOperationException("В выбранном наборе усилий нет ни одного усилия — нечего считать.");

            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx.Database.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram);

            var p = CrackWidthTaskParams.Parse(task.ParamsJson);

            List<LoadItem>? longItems = null;
            Dictionary<string, LoadItem>? longByLabel = null;
            if (p.ForcesMode == "two_sets" && p.ForceSetLongId.HasValue)
            {
                var longSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == p.ForceSetLongId.Value)
                    ?? throw new InvalidOperationException($"Набор длительных усилий id={p.ForceSetLongId} не найден.");
                longItems = longSet.Items;
                longByLabel = longSet.Items.Where(i => !string.IsNullOrEmpty(i.Label))
                    .GroupBy(i => i.Label).ToDictionary(g => g.Key, g => g.First());
            }

            var calcCrc = task.CalcType is CalcType.N or CalcType.NL ? task.CalcType : CalcType.N;
            var solver = new CrackWidthSolver(section,
                calcCrc: calcCrc, calcService: CalcType.N,
                calcServiceLong: p.LongPartUseNL ? CalcType.NL : (CalcType?)null,
                phi2: p.Phi2,
                acrcUltLong: p.AcrcUltLong, acrcUltShort: p.AcrcUltShort,
                sp63EtaMin: settings.Sp63DescEtaMin);

            var etaParams = LimitForceParams.Parse(task.ParamsJson);
            double etaThreshold = etaParams.EtaSlendernessThreshold
                ?? EccentricityAmplifier.SlendernessThreshold;
            bool ten = settings.ResolveConcreteTension(CalcType.N);

            var items = forceSet.Items;
            int total = items.Count;
            var rowResults = new List<object>(total);
            int passedCount = 0;

            for (int i = 0; i < total; i++)
            {
                var fi = items[i];
                double nTotal = fi.N, mxTotal = fi.Mx, myTotal = fi.My;
                double mxLong, myLong;

                if (p.ForcesMode == "two_sets" && longItems != null)
                {
                    LoadItem? longItem = null;
                    if (!string.IsNullOrEmpty(fi.Label) && longByLabel!.TryGetValue(fi.Label, out var byLabel))
                        longItem = byLabel;
                    else if (i < longItems.Count)
                        longItem = longItems[i];

                    if (longItem is null)
                        throw new InvalidOperationException(
                            $"Не найдено длительное усилие для строки '{fi.Label}' (index={i}). " +
                            "Наборы усилий должны совпадать по меткам или порядку.");

                    mxLong = longItem.Mx;
                    myLong = longItem.My;
                }
                else if (p.ForcesMode == "share")
                {
                    mxLong = mxTotal * p.LongShare;
                    myLong = myTotal * p.LongShare;
                }
                else
                {
                    mxLong = mxTotal;
                    myLong = myTotal;
                }

                double mxLongIn = mxLong, myLongIn = myLong, mxTotalIn = mxTotal, myTotalIn = myTotal;
                object? etaRow = null;
                if (etaParams.EtaEnabled)
                {
                    double psiX = CrackWidthEta.AutoPsi(mxLongIn, mxTotalIn);
                    double psiY = CrackWidthEta.AutoPsi(myLongIn, myTotalIn);
                    var strainSolver = new StrainSolver(section, CalcType.N,
                        ten: ten,
                        tol: settings.NewtonTolerance,
                        maxIter: settings.NewtonMaxIter,
                        h: settings.NewtonDeltaH,
                        centralJacobian: settings.NewtonJacobian == "central");
                    var wiring = RodEtaWiring.Apply(
                        section, nTotal, mxTotalIn, myTotalIn,
                        etaParams.EtaL0x, etaParams.EtaL0y,
                        psiX, psiY, etaParams.EtaIterative,
                        (mx, my) => strainSolver.Solve(nTotal, mx, my),
                        etaThreshold);
                    var scaled = CrackWidthEta.ScaleLongTotal(
                        mxLongIn, mxTotalIn, myLongIn, myTotalIn, wiring.MxEff, wiring.MyEff);
                    mxLong = scaled.MxLongEff;
                    mxTotal = scaled.MxTotalEff;
                    myLong = scaled.MyLongEff;
                    myTotal = scaled.MyTotalEff;
                    etaRow = new
                    {
                        mode = etaParams.EtaIterative ? "iterative" : "formula",
                        psiX,
                        psiY,
                        etaX = Math.Round(wiring.X.Eta, 6),
                        etaY = Math.Round(wiring.Y.Eta, 6),
                        mxOriginal = mxTotalIn,
                        myOriginal = myTotalIn,
                        mxEff = wiring.MxEff,
                        myEff = wiring.MyEff,
                        stableX = wiring.X.Stable,
                        stableY = wiring.Y.Stable,
                    };
                }

                var res = solver.Compute(N: nTotal, mxLong: mxLong, mxTotal: mxTotal, myLong: myLong, myTotal: myTotal);
                if (res.PassedLong && res.PassedShort) passedCount++;

                rowResults.Add(new
                {
                    label = fi.Label,
                    num = fi.Num,
                    N = nTotal,
                    Mx_long = Math.Round(mxLong, 4),
                    Mx_total = Math.Round(mxTotal, 4),
                    My_long = Math.Round(myLong, 4),
                    My_total = Math.Round(myTotal, 4),
                    cracked = res.Cracked,
                    acrc_long = Math.Round(res.AcrcLong, 4),
                    acrc_short = Math.Round(res.AcrcShort, 4),
                    passed_long = res.PassedLong,
                    passed_short = res.PassedShort,
                    status = (res.PassedLong && res.PassedShort) ? "ok" : "not_passed",
                    eta = etaRow
                });
            }

            bool allPassed = passedCount == total;

            var data = new
            {
                all_passed = allPassed,
                passed_count = passedCount,
                total,
                rows = rowResults
            };

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = allPassed ? "ok" : "not_passed",
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
