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
    /// Поперечная сила строки (спека, раздел 8) при подобранных сетках: A_red для φn — по площади сеток,
    /// приведённой к фасетке θ_Q. null — Qx = Qy = 0 или подбор по ПС2 (проверка относится к ПС1).
    /// </summary>
    public delegate PlateShearResult? ShearRow(double qx, double qy, double nx, double ny, double nxy,
        double mx, double my, double mxy, CapraMauryDesignResult design);

    /// <summary>
    /// ПС1 — диаграммы вида расчёта задачи; ПС2 — диаграммы вида N, предел и параметры трещин из
    /// параметров задачи, отправная As_c — из ПС1 той же фасетки.
    /// </summary>
    public static (PlateSection Section, double ZTop, double ZBot, DesignRow Design, ShearRow Shear) Prepare(
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
                (nx, ny, nxy, mx, my, mxy, step) => CapraMauryDesignSolver.DesignSls(c, nx, ny, nxy, mx, my, mxy, step),
                (_, _, _, _, _, _, _, _, _) => null);
        }
        var u = CapraMauryDesignSolver.CreateUls(plate, concrete, rebar, task.CalcType);
        return (plate, u.ZTop, u.ZBot,
            (nx, ny, nxy, mx, my, mxy, step) => CapraMauryDesignSolver.DesignUls(u, nx, ny, nxy, mx, my, mxy, step),
            (qx, qy, nx, ny, nxy, mx, my, mxy, d) =>
            {
                double t = Math.Atan2(qy, qx), c2 = Math.Cos(t) * Math.Cos(t), s2 = 1.0 - c2;
                double asN = (d.Top.Asx + d.Bottom.Asx) * c2 + (d.Top.Asy + d.Bottom.Asy) * s2;
                return PlateShearCheck.Check(qx, qy, nx, ny, nxy, mx, my, mxy, plate, concrete, rebar, task.CalcType, asN);
            });
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
            var (plate, zTop, zBot, design, shearRow) = CapraMauryDesignTaskSupport.Prepare(task, ctx, sls, p);
            var r = design(p.Nx, p.Ny, p.Nxy, p.Mx, p.My, p.Mxy, p.StepDeg);
            var shear = shearRow(p.Qx, p.Qy, p.Nx, p.Ny, p.Nxy, p.Mx, p.My, p.Mxy, r);
            var data = new
            {
                forces = new { nx = p.Nx, ny = p.Ny, nxy = p.Nxy, mx = p.Mx, my = p.My, mxy = p.Mxy, qx = p.Qx, qy = p.Qy },
                h = plate.H, z_top = zTop, z_bot = zBot,
                design = r,
                shear,
            };
            // (8.55) не выполняется — поперечная арматура не помогает, подбор неполон.
            bool ok = r.Feasible && (shear == null || shear.EtaStrip <= 1.0);
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = ok ? "ok" : "partial",
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
            var (plate, _, _, design, shearRow) = CapraMauryDesignTaskSupport.Prepare(task, ctx, sls, p);
            var forceSet = ctx!.Database!.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException($"Набор усилий id={task.ForceSetId} не найден.");
            if (forceSet.ShellItems.Count == 0)
                throw new InvalidOperationException($"Набор усилий «{forceSet.Tag}» не содержит строк для пластин.");

            // Контекст подбора неизменяем — строки считаются независимо (параллельно при включённой настройке).
            var items = forceSet.ShellItems;
            var rowArr = new CapraMauryDesignBatchRow[items.Count];
            BatchProgress.ForEachRow(items.Count, settings, ctx, i =>
            {
                var si = items[i];
                var (nx, ny, nxy) = p.AutoStressToForce ? si.ResolveN(plate.H) : (si.Nx, si.Ny, si.Nxy);
                var r = design(nx, ny, nxy, si.Mx, si.My, si.Mxy, p.StepDeg);
                var shear = shearRow(si.Qx, si.Qy, nx, ny, nxy, si.Mx, si.My, si.Mxy, r);
                var diagnostics = r.Diagnostics.ToList();
                if (shear is { EtaStrip: > 1.0 })
                    diagnostics.Add($"(8.55): Кисп,Q = {shear.EtaStrip:F2} > 1");
                rowArr[i] = new CapraMauryDesignBatchRow
                {
                    Num = si.Num, Label = si.Label,
                    Nx = nx, Ny = ny, Nxy = nxy, Mx = si.Mx, My = si.My, Mxy = si.Mxy, Qx = si.Qx, Qy = si.Qy,
                    TopAsx = r.Top.Asx, TopAsy = r.Top.Asy, BotAsx = r.Bottom.Asx, BotAsy = r.Bottom.Asy,
                    AswPerSw = shear?.AswPerSw ?? 0.0, EtaQStrip = shear?.EtaStrip ?? 0.0,
                    Feasible = r.Feasible && (shear == null || shear.EtaStrip <= 1.0),
                    Diagnostics = string.Join("; ", diagnostics),
                };
            });
            var rows = rowArr.ToList();

            var data = new CapraMauryDesignBatchData
            {
                Limit = sls ? "sls" : "uls",
                AcrcLimMm = sls ? p.AcrcLimMm : 0.0,
                Total = rows.Count,
                Feasible = rows.Count(r => r.Feasible),
                StepDeg = p.StepDeg,
                Rows = rows,
                Envelope = CapraMauryDesignBatchData.BuildEnvelope(rows),
                EnvelopeAsw = CapraMauryDesignBatchData.BuildAswEnvelope(rows),
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
    public double Qx { get; set; }
    public double Qy { get; set; }
    public double TopAsx { get; set; }
    public double TopAsy { get; set; }
    public double BotAsx { get; set; }
    public double BotAsy { get; set; }
    /// <summary>Требуемая поперечная арматура Asw/(sw·1 м), м²/м²; 0 — не требуется или Q = 0.</summary>
    public double AswPerSw { get; set; }
    /// <summary>Кисп по (8.55) — бетонная полоса между наклонными сечениями; 0 — Q = 0.</summary>
    public double EtaQStrip { get; set; }
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
    /// <summary>Наибольшая требуемая Asw/sw (м²/м²) с управляющей строкой; null — ни в одной строке не требуется.</summary>
    public CapraMauryDesignEnvelopeItem? EnvelopeAsw { get; set; }

    /// <summary>Огибающая поперечной арматуры по строкам с решением.</summary>
    public static CapraMauryDesignEnvelopeItem? BuildAswEnvelope(IReadOnlyList<CapraMauryDesignBatchRow> rows)
    {
        var best = rows.Where(r => r.Feasible && r.AswPerSw > 0.0).MaxBy(r => r.AswPerSw);
        return best == null ? null
            : new CapraMauryDesignEnvelopeItem { Component = "asw", As = best.AswPerSw, Num = best.Num, Label = best.Label };
    }

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
