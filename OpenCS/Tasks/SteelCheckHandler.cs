using System;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик задачи «Проверка стального сечения по СП 16.13330.2017».
/// </summary>
public class SteelCheckHandler : ITaskHandler
{
    public string Kind => "steel_check";

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

            var context = new DesignContext
            {
                DesignLengthX = p.DesignLengthX,
                DesignLengthY = p.DesignLengthY,
                MuX = p.MuX,
                MuY = p.MuY,
                BetaM = p.BetaM,
                GammaM = p.GammaM,
                DesignLengthBit = p.DesignLengthBit
            };

            var result = SteelChecker.Run(steelSection, forces, context);

            // Группировка проверок по категориям
            string GetCategory(string formula) => formula switch
            {
                var f when f.StartsWith("8.") => "strength",
                var f when f.StartsWith("9.") => "stability",
                var f when f.StartsWith("10.") => "constructive",
                _ => "other"
            };

            var data = new
            {
                utilization = Math.Round(result.Utilization, 6),
                passed = result.IsPassed,
                sectionTag = section.Tag,
                steelTag = steelSection.Steel.Tag,
                // Параметры расчёта для отображения
                context = new
                {
                    l0x = p.DesignLengthX,
                    l0y = p.DesignLengthY,
                    muX = p.MuX,
                    muY = p.MuY,
                    betaM = p.BetaM,
                    gammaM = p.GammaM,
                    lbit = p.DesignLengthBit
                },
                // Усилия для отображения
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
                details = result.Details.Select(d => new
                {
                    formula = d.Formula,
                    description = d.Description,
                    normRef = d.NormReference,
                    category = GetCategory(d.Formula),
                    applied = Math.Round(d.Applied, 2),
                    allowable = Math.Round(d.Allowable, 2),
                    ratio = Math.Round(d.Ratio, 6),
                    passed = d.Passed,
                    variables = d.Variables.ToDictionary(
                        kv => kv.Key,
                        kv => Math.Round(kv.Value, 6))
                }).ToArray()
            };

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = "ok",
                DataJson = JsonSerializer.Serialize(data)
            };
        }
        catch (Exception ex)
        {
            return Error(task, created, ex.Message);
        }
    }

    static SteelSection? BuildSteelSection(CrossSection section)
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

    static InternalForces? ResolveForces(SteelCheckParams p, LoadItem item)
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

    static CalcResult Error(CalcTask task, string created, string message)
    {
        return new CalcResult
        {
            TaskId = task.Id,
            TaskKind = task.Kind,
            TaskTag = task.Tag,
            Created = created,
            Status = "error",
            DataJson = JsonSerializer.Serialize(new { error = message })
        };
    }
}
