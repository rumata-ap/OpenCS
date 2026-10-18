using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CScore;
using CScore.ShellSimpl;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Общие шаги задач подбора Капра–Мори: сечение пластины, материалы, контекст подбора.</summary>
static class CapraMauryDesignTaskSupport
{
    /// <summary>Подбор одной строки усилий (Nx, Ny, Nxy, Mx, My, Mxy, шаг фасеток).</summary>
    public delegate CapraMauryDesignResult DesignRow(double nx, double ny, double nxy, double mx, double my, double mxy, double stepDeg);

    /// <summary>
    /// ПС1 — диаграммы вида расчёта задачи; ПС2 — диаграммы вида N, предел и параметры трещин из
    /// параметров задачи, отправная As_c — из ПС1 той же фасетки.
    /// </summary>
    public static (PlateSection Section, double ZTop, double ZBot, DesignRow Design) Prepare(
        CalcTask task, TaskRunContext? ctx, bool sls, ShellSimplParams p)
    {
        if (ctx?.Database is null)
            throw new InvalidOperationException("Требуется контекст с DatabaseService.");
        var plate = ctx.Database.PlateSections.FirstOrDefault(s => s.Id == task.SectionId)
            ?? throw new InvalidOperationException($"Плитное сечение id={task.SectionId} не найдено.");
        var concrete = ctx.Database.Materials.FirstOrDefault(m => m.Id == plate.ConcreteMaterialId)
            ?? throw new InvalidOperationException($"Материал бетона id={plate.ConcreteMaterialId} не найден.");
        var rebar = ctx.Database.Materials.FirstOrDefault(m => m.Id == plate.RebarMaterialId)
            ?? throw new InvalidOperationException($"Материал арматуры id={plate.RebarMaterialId} не найден.");
        if (sls)
        {
            var c = CapraMauryDesignSolver.CreateSls(plate, concrete, rebar, p.AcrcLimMm, p.Phi1, p.Phi2, p.SigmaSCrc);
            return (plate, c.ZTop, c.ZBot,
                (nx, ny, nxy, mx, my, mxy, step) => CapraMauryDesignSolver.DesignSls(c, nx, ny, nxy, mx, my, mxy, step));
        }
        var u = CapraMauryDesignSolver.CreateUls(plate, concrete, rebar, task.CalcType);
        return (plate, u.ZTop, u.ZBot,
            (nx, ny, nxy, mx, my, mxy, step) => CapraMauryDesignSolver.DesignUls(u, nx, ny, nxy, mx, my, mxy, step));
    }

    public static CalcResult Error(CalcTask task, string created, Exception ex) => new()
    {
        TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
        Created = created, Status = "error",
        DataJson = JsonSerializer.Serialize(new { error = ex.Message })
    };
}

/// <summary>
/// Подбор сеток по Капра–Мори для усилий из параметров задачи, модель полосы — НДМ.
/// Площади в результате — м²/м.
/// </summary>
public abstract class CapraMauryDesignHandlerBase(bool sls) : ITaskHandler
{
    public abstract string Kind { get; }

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var p = ShellSimplParams.Parse(task.ParamsJson);
            var (plate, zTop, zBot, design) = CapraMauryDesignTaskSupport.Prepare(task, ctx, sls, p);
            var r = design(p.Nx, p.Ny, p.Nxy, p.Mx, p.My, p.Mxy, p.StepDeg);
            var data = new
            {
                forces = new { nx = p.Nx, ny = p.Ny, nxy = p.Nxy, mx = p.Mx, my = p.My, mxy = p.Mxy },
                h = plate.H, z_top = zTop, z_bot = zBot,
                design = r,
            };
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = r.Feasible ? "ok" : "partial",
                DataJson = JsonSerializer.Serialize(data)
            };
        }
        catch (Exception ex)
        {
            return CapraMauryDesignTaskSupport.Error(task, created, ex);
        }
    }
}

/// <summary>Подбор сеток по Капра–Мори, ПС1 (<c>shell_simpl_capri_design_uls</c>).</summary>
public sealed class CapraMauryDesignUlsHandler() : CapraMauryDesignHandlerBase(false)
{
    public override string Kind => "shell_simpl_capri_design_uls";
}

/// <summary>Подбор сеток по Капра–Мори, ПС2 (<c>shell_simpl_capri_design_sls</c>): a_crc ≤ a_crc,ult на всех фасетках.</summary>
public sealed class CapraMauryDesignSlsHandler() : CapraMauryDesignHandlerBase(true)
{
    public override string Kind => "shell_simpl_capri_design_sls";
}

/// <summary>
/// Подбор сеток по Капра–Мори для набора усилий: строки с четырьмя площадями и огибающая —
/// максимум каждой компоненты с управляющей строкой.
/// </summary>
public abstract class CapraMauryDesignBatchHandlerBase(bool sls) : ITaskHandler
{
    public abstract string Kind { get; }

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var p = ShellSimplParams.Parse(task.ParamsJson);
            var (plate, _, _, design) = CapraMauryDesignTaskSupport.Prepare(task, ctx, sls, p);
            var forceSet = ctx!.Database!.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException($"Набор усилий id={task.ForceSetId} не найден.");
            if (forceSet.ShellItems.Count == 0)
                throw new InvalidOperationException($"Набор усилий «{forceSet.Tag}» не содержит строк для пластин.");

            var rows = new List<CapraMauryDesignBatchRow>(forceSet.ShellItems.Count);
            foreach (var si in forceSet.ShellItems)
            {
                var (nx, ny, nxy) = p.AutoStressToForce ? si.ResolveN(plate.H) : (si.Nx, si.Ny, si.Nxy);
                var r = design(nx, ny, nxy, si.Mx, si.My, si.Mxy, p.StepDeg);
                rows.Add(new CapraMauryDesignBatchRow
                {
                    Num = si.Num, Label = si.Label,
                    Nx = nx, Ny = ny, Nxy = nxy, Mx = si.Mx, My = si.My, Mxy = si.Mxy,
                    TopAsx = r.Top.Asx, TopAsy = r.Top.Asy, BotAsx = r.Bottom.Asx, BotAsy = r.Bottom.Asy,
                    Feasible = r.Feasible,
                    Diagnostics = string.Join("; ", r.Diagnostics),
                });
            }

            var data = new CapraMauryDesignBatchData
            {
                Limit = sls ? "sls" : "uls",
                AcrcLimMm = sls ? p.AcrcLimMm : 0.0,
                Total = rows.Count,
                Feasible = rows.Count(r => r.Feasible),
                StepDeg = p.StepDeg,
                Rows = rows,
                Envelope = CapraMauryDesignBatchData.BuildEnvelope(rows),
            };
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = data.Feasible == data.Total ? "ok" : "partial",
                DataJson = JsonSerializer.Serialize(data)
            };
        }
        catch (Exception ex)
        {
            return CapraMauryDesignTaskSupport.Error(task, created, ex);
        }
    }
}

/// <summary>Пакетный подбор по ПС1 (<c>shell_simpl_capri_design_uls_batch</c>).</summary>
public sealed class CapraMauryDesignUlsBatchHandler() : CapraMauryDesignBatchHandlerBase(false)
{
    public override string Kind => "shell_simpl_capri_design_uls_batch";
}

/// <summary>Пакетный подбор по ПС2 (<c>shell_simpl_capri_design_sls_batch</c>).</summary>
public sealed class CapraMauryDesignSlsBatchHandler() : CapraMauryDesignBatchHandlerBase(true)
{
    public override string Kind => "shell_simpl_capri_design_sls_batch";
}
/// <summary>Строка пакетного подбора; площади — м²/м.</summary>
public sealed class CapraMauryDesignBatchRow
{
    public int Num { get; set; }
    public string Label { get; set; } = "";
    public double Nx { get; set; }
    public double Ny { get; set; }
    public double Nxy { get; set; }
    public double Mx { get; set; }
    public double My { get; set; }
    public double Mxy { get; set; }
    public double TopAsx { get; set; }
    public double TopAsy { get; set; }
    public double BotAsx { get; set; }
    public double BotAsy { get; set; }
    public bool Feasible { get; set; }
    public string Diagnostics { get; set; } = "";
}

/// <summary>Компонента огибающей: наибольшая площадь и строка, которая её задаёт.</summary>
public sealed class CapraMauryDesignEnvelopeItem
{
    public string Component { get; set; } = "";
    public double As { get; set; }
    public int Num { get; set; }
    public string Label { get; set; } = "";
}

/// <summary>Результат пакетного подбора (JSON задачи).</summary>
public sealed class CapraMauryDesignBatchData
{
    /// <summary>"uls" — подбор по ПС1, "sls" — по ПС2.</summary>
    public string Limit { get; set; } = "uls";
    /// <summary>ПС2: предельная ширина раскрытия трещин, мм.</summary>
    public double AcrcLimMm { get; set; }
    public int Total { get; set; }
    public int Feasible { get; set; }
    public double StepDeg { get; set; }
    public List<CapraMauryDesignBatchRow> Rows { get; set; } = [];
    public List<CapraMauryDesignEnvelopeItem> Envelope { get; set; } = [];

    /// <summary>Компоненты в порядке: верх x, верх y, низ x, низ y. Строки без решения в огибающую не входят.</summary>
    public static List<CapraMauryDesignEnvelopeItem> BuildEnvelope(IReadOnlyList<CapraMauryDesignBatchRow> rows)
    {
        var parts = new (string Name, Func<CapraMauryDesignBatchRow, double> Get)[]
        {
            ("top_x", r => r.TopAsx), ("top_y", r => r.TopAsy), ("bot_x", r => r.BotAsx), ("bot_y", r => r.BotAsy),
        };
        var result = new List<CapraMauryDesignEnvelopeItem>(4);
        foreach (var (name, get) in parts)
        {
            var best = rows.Where(r => r.Feasible).MaxBy(get);
            result.Add(best == null
                ? new CapraMauryDesignEnvelopeItem { Component = name }
                : new CapraMauryDesignEnvelopeItem { Component = name, As = get(best), Num = best.Num, Label = best.Label });
        }
        return result;
    }
}
