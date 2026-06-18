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
