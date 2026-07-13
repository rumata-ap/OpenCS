using System;
using System.Linq;
using System.Text.Json;
using CScore;
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

            var solver = new CrackWidthSolver(section,
                calcCrc: CalcType.CL, calcService: CalcType.N,
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
                cracked = res.Cracked,
                acrc_long = Math.Round(res.AcrcLong, 4),
                acrc_short = Math.Round(res.AcrcShort, 4),
                acrc_ult_long = res.AcrcUltLong,
                acrc_ult_short = res.AcrcUltShort,
                passed_long = res.PassedLong,
                passed_short = res.PassedShort,
                Mcrc = Math.Round(res.Mcrc, 4),
                sigma_s = Math.Round(res.SigmaS / 1000.0, 2),
                psi_s = Math.Round(res.PsiS, 4),
                ls = Math.Round(res.Ls * 1000.0, 2),
                ds_eq = Math.Round(res.DsEq * 1000.0, 2),
                As_tens = Math.Round(res.AsTens * 1e4, 4),
                Abt = Math.Round(res.Abt * 1e4, 2)
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
