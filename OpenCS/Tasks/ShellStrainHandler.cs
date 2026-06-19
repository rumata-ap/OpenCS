using System;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Задача «Плоскость деформаций пластины»: методом Ньютона 6×6 находит НДС
/// [ε₀x,ε₀y,γ₀xy,κx,κy,κxy] при заданных усилиях из ParamsJson.
/// </summary>
public sealed class ShellStrainHandler : ITaskHandler
{
    public string Kind => "shell_strain_state";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException("Требуется контекст с DatabaseService.");

            var plate = ctx.Database.PlateSections.FirstOrDefault(s => s.Id == task.SectionId)
                ?? throw new InvalidOperationException($"Плитное сечение id={task.SectionId} не найдено.");

            var (cDiag, rDiag, layerDiags, _) =
                PlateMaterialResolver.Resolve(plate, ctx.Database.Materials, task.CalcType);

            var p = ShellStrainParams.Parse(task.ParamsJson);
            double[] target = { p.Nx, p.Ny, p.Nxy, p.Mx, p.My, p.Mxy };

            var solver = new ShellStrainSolver(plate, cDiag, rDiag, layerDiags,
                tolRes: p.TolRes, maxIter: p.MaxIter,
                hDiff: settings.NewtonDeltaH,
                centralJacobian: settings.NewtonJacobian == "central");
            var r = solver.Solve(target);
            var f = r.Forces;
            var s = r.StrainState;

            var sec = plate.ComputeSecant(s, cDiag, rDiag, layerDiags);

            var data = new
            {
                converged = r.Converged, iterations = r.Iterations,
                residual = Math.Round(r.Residual, 6),
                section_h = plate.H,
                eps0x = Math.Round(s.Eps0x, 9), eps0y = Math.Round(s.Eps0y, 9),
                gamma0xy = Math.Round(s.Gamma0xy, 9),
                kx = Math.Round(s.Kx, 9), ky = Math.Round(s.Ky, 9), kxy = Math.Round(s.Kxy, 9),
                Nx_target = p.Nx, Ny_target = p.Ny, Nxy_target = p.Nxy,
                Mx_target = p.Mx, My_target = p.My, Mxy_target = p.Mxy,
                Nx_result = Math.Round(f.Nx, 4), Ny_result = Math.Round(f.Ny, 4),
                Nxy_result = Math.Round(f.Nxy, 4), Mx_result = Math.Round(f.Mx, 4),
                My_result = Math.Round(f.My, 4), Mxy_result = Math.Round(f.Mxy, 4),
                // Секущие жёсткости
                EAx_sec  = Math.Round(sec.EAx,   1),
                EAy_sec  = Math.Round(sec.EAy,   1),
                zc_x_sec = Math.Round(sec.ZcxMm, 2),
                zc_y_sec = Math.Round(sec.ZcyMm, 2),
                EIxc_sec = Math.Round(sec.EIxc,  3),
                EIyc_sec = Math.Round(sec.EIyc,  3),
                // Упругие жёсткости
                EAx_el   = Math.Round(sec.EAxEl,   1),
                EAy_el   = Math.Round(sec.EAyEl,   1),
                zc_x_el  = Math.Round(sec.ZcxElMm, 2),
                zc_y_el  = Math.Round(sec.ZcyElMm, 2),
                EIxc_el  = Math.Round(sec.EIxcEl,  3),
                EIyc_el  = Math.Round(sec.EIycEl,  3),
                // Коэффициенты снижения (-1 = не вычислен)
                phi_EAx  = Math.Round(sec.PhiEAx,  4),
                phi_EAy  = Math.Round(sec.PhiEAy,  4),
                phi_EIxc = Math.Round(sec.PhiEIxc, 4),
                phi_EIyc = Math.Round(sec.PhiEIyc, 4),
            };

            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created,
                Status = r.Converged ? "ok" : "not_converged",
                DataJson = JsonSerializer.Serialize(data),
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message }),
            };
        }
    }
}
