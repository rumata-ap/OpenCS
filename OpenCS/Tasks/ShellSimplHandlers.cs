using System;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

public abstract class ShellSimplHandlerBase : ITaskHandler
{
    protected abstract string KindId { get; }
    public string Kind => KindId;

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException("Требуется контекст с DatabaseService.");

            var plateSection = ctx.Database.PlateSections
                .FirstOrDefault(s => s.Id == task.SectionId)
                ?? throw new InvalidOperationException(
                    $"Плитное сечение id={task.SectionId} не найдено.");

            var concreteMat = ctx.Database.Materials
                .FirstOrDefault(m => m.Id == plateSection.ConcreteMaterialId)
                ?? throw new InvalidOperationException(
                    $"Материал бетона id={plateSection.ConcreteMaterialId} не найден.");

            var rebarMat = ctx.Database.Materials
                .FirstOrDefault(m => m.Id == plateSection.RebarMaterialId)
                ?? throw new InvalidOperationException(
                    $"Материал арматуры id={plateSection.RebarMaterialId} не найден.");

            var p = ShellSimplParams.Parse(task.ParamsJson);
            var sp = new ShellSimplSolver.SolveParams(
                p.Nx, p.Ny, p.Nxy, p.Mx, p.My, p.Mxy,
                KindId, p.StepDeg, p.AcrcLimMm, p.Phi1, p.Phi2
            );

            var result = ShellSimplSolver.Solve(sp, plateSection, concreteMat, rebarMat, task.CalcType);

            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = "ok",
                DataJson = JsonSerializer.Serialize(result)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }
}

public sealed class ShellSimplWaSlsHandler : ShellSimplHandlerBase
{
    protected override string KindId => "shell_simpl_wa_sls";
}

public sealed class ShellSimplWaUlsHandler : ShellSimplHandlerBase
{
    protected override string KindId => "shell_simpl_wa_uls";
}

public sealed class ShellSimplCapriSlsHandler : ShellSimplHandlerBase
{
    protected override string KindId => "shell_simpl_capri_sls";
}

public sealed class ShellSimplCapriUlsHandler : ShellSimplHandlerBase
{
    protected override string KindId => "shell_simpl_capri_uls";
}

// ── Пакетные хендлеры ────────────────────────────────────────────────────

public abstract class ShellSimplBatchHandlerBase : ITaskHandler
{
    protected abstract string KindId { get; }
    public string Kind => KindId;
    protected abstract bool IsSls { get; }

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException("Требуется контекст с DatabaseService.");

            var plateSection = ctx.Database.PlateSections
                .FirstOrDefault(s => s.Id == task.SectionId)
                ?? throw new InvalidOperationException($"Плитное сечение id={task.SectionId} не найдено.");

            var concreteMat = ctx.Database.Materials
                .FirstOrDefault(m => m.Id == plateSection.ConcreteMaterialId)
                ?? throw new InvalidOperationException($"Материал бетона id={plateSection.ConcreteMaterialId} не найден.");
            var rebarMat = ctx.Database.Materials
                .FirstOrDefault(m => m.Id == plateSection.RebarMaterialId)
                ?? throw new InvalidOperationException($"Материал арматуры id={plateSection.RebarMaterialId} не найден.");

            var forceSet = ctx.Database.ForceSets
                .FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException($"Набор усилий id={task.ForceSetId} не найден.");
            if (forceSet.ShellItems.Count == 0)
                throw new InvalidOperationException($"Набор усилий «{forceSet.Tag}» не содержит строк для пластин.");

            var sp = ShellSimplParams.Parse(task.ParamsJson);
            var rows = new List<object>(forceSet.ShellItems.Count);
            int convergedCount = 0;

            foreach (var si in forceSet.ShellItems)
            {
                var solveParams = new ShellSimplSolver.SolveParams(
                    si.Nx, si.Ny, si.Nxy, si.Mx, si.My, si.Mxy,
                    KindId, sp.StepDeg, sp.AcrcLimMm, sp.Phi1, sp.Phi2
                );
                var result = ShellSimplSolver.Solve(
                    solveParams, plateSection, concreteMat, rebarMat, task.CalcType);

                double? etaMax = result.EtaMax;
                bool ok;
                string rowStatus;

                if (IsSls)
                {
                    var dirs = result.CapriDirs ?? [];
                    double maxAcrc = dirs.Count > 0 ? dirs.Max(d => d.Strip.Acrc_mm) : 0;
                    ok = maxAcrc <= sp.AcrcLimMm;
                    rowStatus = ok ? "ok" : "fail";
                    if (ok) convergedCount++;
                }
                else
                {
                    ok = etaMax.HasValue && etaMax.Value <= 1.0;
                    rowStatus = ok ? "ok" : "fail";
                    if (ok) convergedCount++;
                }

                rows.Add(new
                {
                    label = si.Label,
                    status = rowStatus,
                    eta_max = etaMax,
                });
            }

            bool allOk = convergedCount == rows.Count;
            var data = new
            {
                all_ok = allOk,
                converged_count = convergedCount,
                total = rows.Count,
                rows,
                method = KindId.Contains("wa_") ? "wa" : "capri",
                calc_type = IsSls ? "sls" : "uls",
            };

            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = allOk ? "ok" : "partial",
                DataJson = JsonSerializer.Serialize(data)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }
}

public sealed class ShellSimplWaSlsBatchHandler : ShellSimplBatchHandlerBase
{
    protected override string KindId => "shell_simpl_wa_sls_batch";
    protected override bool IsSls => true;
}

public sealed class ShellSimplWaUlsBatchHandler : ShellSimplBatchHandlerBase
{
    protected override string KindId => "shell_simpl_wa_uls_batch";
    protected override bool IsSls => false;
}

public sealed class ShellSimplCapriSlsBatchHandler : ShellSimplBatchHandlerBase
{
    protected override string KindId => "shell_simpl_capri_sls_batch";
    protected override bool IsSls => true;
}

public sealed class ShellSimplCapriUlsBatchHandler : ShellSimplBatchHandlerBase
{
    protected override string KindId => "shell_simpl_capri_uls_batch";
    protected override bool IsSls => false;
}
