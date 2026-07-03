using System;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Базовый класс для всех steel-задач. Содержит общую логику построения
/// SteelSection, DesignContext и формирования результата.
/// </summary>
public abstract class SteelTaskHandlerBase : ITaskHandler
{
    public abstract string Kind { get; }

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var p = SteelCheckParams.Parse(task.ParamsJson);
            var steelSection = BuildSteelSection(section);
            if (steelSection == null)
                return Error(task, created, "Сечение не содержит подходящего стального материала");

            var forces = ResolveForces(p, item);
            if (forces == null)
                return Error(task, created, "Не заданы усилия");

            var context = BuildContext(p);
            return Execute(task, created, section, steelSection, forces, context);
        }
        catch (Exception ex)
        {
            return Error(task, created, ex.Message);
        }
    }

    protected abstract CalcResult Execute(
        CalcTask task, string created, CrossSection section,
        SteelSection steelSection, InternalForces forces, DesignContext context);

    protected static DesignContext BuildContext(SteelCheckParams p) => new()
    {
        DesignLengthX = p.DesignLengthX,
        DesignLengthY = p.DesignLengthY,
        MuX = p.MuX,
        MuY = p.MuY,
        BetaM = p.BetaM,
        GammaM = p.GammaM,
        DesignLengthBit = p.DesignLengthBit
    };

    protected static SteelSection? BuildSteelSection(CrossSection section)
    {
        var steelMat = section.Areas
            .Select(a => a.Material)
            .FirstOrDefault(m => m != null && m.Type is MatType.Steel or MatType.Custom);
        if (steelMat == null) return null;

        var steelArea = section.Areas.FirstOrDefault(a => a.Material == steelMat);
        if (steelArea?.Hull?.Points == null || steelArea.Hull.Points.Count < 4)
            return null;

        var outer = steelArea.Hull.Points.Select(pt => (pt.X, pt.Y)).ToList();
        var E = steelMat.E > 0 ? steelMat.E : 210e9;

        return new SteelSection
        {
            OuterContour = outer,
            Steel = new Material
            {
                Type = MatType.Steel,
                E = E,
                Tag = steelMat.Tag,
                materialChars = [.. steelMat.materialChars]
            }
        };
    }

    protected static InternalForces? ResolveForces(SteelCheckParams p, LoadItem item)
    {
        if (p.ManualForces != null)
        {
            return new InternalForces
            {
                LoadCaseName = "Ручной ввод",
                N  = p.ManualForces.N,
                Mx = p.ManualForces.Mx,
                My = p.ManualForces.My,
                Mz = p.ManualForces.Mz,
                Qy = p.ManualForces.Qy,
                Qz = p.ManualForces.Qz
            };
        }
        if (item != null)
        {
            return new InternalForces
            {
                LoadCaseName = item.Label ?? "",
                N  = item.N,
                Mx = item.Mx,
                My = item.My,
                Mz = item.T,
                Qy = item.Vx,
                Qz = item.Vy
            };
        }
        return null;
    }

    protected static CalcResult Ok(CalcTask task, string created, string sectionTag, string steelTag,
                                    CheckDetail[] details, InternalForces forces, DesignContext context)
    {
        double maxUtil = details.Length > 0 ? details.Max(d => d.Ratio) : 0;
        var data = new
        {
            utilization = Math.Round(maxUtil, 6),
            passed = maxUtil <= 1.0,
            sectionTag,
            steelTag,
            context = new
            {
                l0x = context.DesignLengthX,
                l0y = context.DesignLengthY,
                muX = context.MuX,
                muY = context.MuY,
                betaM = context.BetaM,
                gammaM = context.GammaM,
                lbit = context.DesignLengthBit
            },
            forces = new
            {
                name = forces.LoadCaseName,
                n = Math.Round(forces.N, 1),
                mx = Math.Round(forces.Mx, 1),
                my = Math.Round(forces.My, 1),
                mz = Math.Round(forces.Mz, 1),
                qy = Math.Round(forces.Qy, 1),
                qz = Math.Round(forces.Qz, 1)
            },
            details = details.Select(d => new
            {
                formula = d.Formula,
                description = d.Description,
                normRef = d.NormReference,
                applied = Math.Round(d.Applied, 2),
                allowable = Math.Round(d.Allowable, 2),
                ratio = Math.Round(d.Ratio, 6),
                passed = d.Passed,
                variables = d.Variables.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 6))
            }).ToArray()
        };
        return new CalcResult
        {
            TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
            Created = created, Status = "ok",
            DataJson = JsonSerializer.Serialize(data)
        };
    }

    protected static CalcResult Error(CalcTask task, string created, string message)
    {
        return new CalcResult
        {
            TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
            Created = created, Status = "error",
            DataJson = JsonSerializer.Serialize(new { error = message })
        };
    }
}
