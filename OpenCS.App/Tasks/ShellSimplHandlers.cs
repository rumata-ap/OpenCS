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
                KindId, p.StepDeg, p.AcrcLimMm, p.Phi1, p.Phi2, p.SigmaSCrc, p.WplGamma, p.Model, p.CapriMode,
                p.Qx, p.Qy
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
    /// <summary>
    /// Вид для решателя — вид одиночной задачи: решатель различает ПС1/ПС2 по окончанию «sls», и с «_batch»
    /// пакеты ПС2 считались как ПС1 (с 18.06.2026 по 20.10.2026: прочность вместо трещин, ширина трещин — 0).
    /// </summary>
    string SolverKind => KindId.EndsWith("_batch", StringComparison.Ordinal) ? KindId[..^"_batch".Length] : KindId;

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
            var items = forceSet.ShellItems;
            int total = items.Count;
            var rows = new object[total];
            var passed = new bool[total];

            // Строки независимы: решатель только читает плиту и материалы (диаграммы строятся на каждый вызов).
            void SolveRow(int i)
            {
                var si = items[i];
                var (nx, ny, nxy) = sp.AutoStressToForce ? si.ResolveN(plateSection.H) : (si.Nx, si.Ny, si.Nxy);
                var solveParams = new ShellSimplSolver.SolveParams(
                    nx, ny, nxy, si.Mx, si.My, si.Mxy,
                    SolverKind, sp.StepDeg, sp.AcrcLimMm, sp.Phi1, sp.Phi2, sp.SigmaSCrc, sp.WplGamma, sp.Model, sp.CapriMode,
                    si.Qx, si.Qy
                );
                var result = ShellSimplSolver.Solve(
                    solveParams, plateSection, concreteMat, rebarMat, task.CalcType);

                double? etaMax = result.EtaMax;
                bool ok;

                // Через ограничения ПС2 тоже даёт Кисп = max r/As_n.
                if (IsSls && result.CapriConstraints == null)
                {
                    double maxAcrc = result.CapriDirs is { Count: > 0 } dirs ? dirs.Max(d => d.Strip.Acrc_mm)
                        : result.WaStrips is { Count: > 0 } strips ? strips.Max(s => s.Acrc_mm) : 0;
                    ok = maxAcrc <= sp.AcrcLimMm;
                }
                else
                    ok = etaMax.HasValue && etaMax.Value <= 1.0;

                passed[i] = ok;
                rows[i] = new
                {
                    num = si.Num,
                    label = si.Label,
                    status = ok ? "ok" : "fail",
                    eta_max = etaMax,
                    eta_q = result.Shear?.Eta,
                };
            }

            BatchProgress.ForEachRow(total, settings, ctx, SolveRow);

            int convergedCount = passed.Count(p => p);
            bool allOk = convergedCount == total;
            var data = new
            {
                all_ok = allOk,
                converged_count = convergedCount,
                total,
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
