using System;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик задачи «Момент трещинообразования» на одну строку усилий.
/// Направление момента — по вектору (Mx, My) заданного усилия (если оба нули — ось X).
/// </summary>
public sealed class CrackingHandler : ITaskHandler
{
    public string Kind => "cracking";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx?.Database?.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram);

            double mag = Math.Sqrt(item.Mx * item.Mx + item.My * item.My);
            double dmx = mag > 1e-12 ? item.Mx / mag : 1.0;
            double dmy = mag > 1e-12 ? item.My / mag : 0.0;

            var solver = new CrackingSolver(section, CalcType.CL);
            var res = solver.CrackingMoment(item.N, dmx, dmy);
            double mcrc = Math.Sqrt(res.Mx * res.Mx + res.My * res.My);

            var data = new
            {
                converged = res.Converged,
                N = item.N,
                Mx_crc = Math.Round(res.Mx, 4),
                My_crc = Math.Round(res.My, 4),
                Mcrc = Math.Round(mcrc, 4),
                eps_max_tension = Math.Round(res.EpsMaxTension, 8),
                eps_tension_limit = Math.Round(solver.TensionLimit(), 8),
                e0 = res.StrainPlane.HasValue ? Math.Round(res.StrainPlane.Value.e0, 8) : (double?)null,
                ky = res.StrainPlane.HasValue ? Math.Round(res.StrainPlane.Value.ky, 8) : (double?)null,
                kz = res.StrainPlane.HasValue ? Math.Round(res.StrainPlane.Value.kz, 8) : (double?)null,
                plane_converged = res.StrainPlane.HasValue
            };

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = res.Converged ? "ok" : "not_converged",
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
