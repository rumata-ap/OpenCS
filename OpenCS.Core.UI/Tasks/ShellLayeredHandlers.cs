using System;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Задача «Проверка прочности слоистой пластины» (ULS): нелинейный расчёт НДС
/// по усилиям и проверка прочности по СП 63 п. 8.1.30 (предельные деформации
/// бетона/арматуры) через ShellLayeredCheck.CheckUls.
/// </summary>
public sealed class ShellLayeredUlsHandler : ITaskHandler
{
    public string Kind => "shell_layered_uls";

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

            var concreteMat = ctx.Database.Materials
                .FirstOrDefault(m => m.Id == plate.ConcreteMaterialId)
                ?? throw new InvalidOperationException(
                    $"Материал бетона id={plate.ConcreteMaterialId} не найден.");
            var rebarMat = ctx.Database.Materials
                .FirstOrDefault(m => m.Id == plate.RebarMaterialId)
                ?? throw new InvalidOperationException(
                    $"Материал арматуры id={plate.RebarMaterialId} не найден.");

            var p = ShellStrainParams.Parse(task.ParamsJson);
            var shell = new ShellLoadItem { Nx = p.Nx, Ny = p.Ny, Nxy = p.Nxy,
                                            Mx = p.Mx, My = p.My, Mxy = p.Mxy };

            var chk = ShellLayeredCheck.CheckUls(plate, shell, concreteMat, rebarMat,
                task.CalcType, plate.ConcreteDiagramType,
                out var st, out var f, out var sec,
                tensionOverride: settings.ConsiderConcreteTensionUls);

            bool passed = chk.Converged && chk.Passed;
            string status = !chk.Converged ? "not_converged"
                          : passed ? "ok" : "fail";

            var data = new
            {
                converged = chk.Converged, iterations = chk.Iterations,
                residual = Math.Round(chk.Residual, 6),
                section_h = plate.H,
                eps0x = Math.Round(st.Eps0x, 9), eps0y = Math.Round(st.Eps0y, 9),
                gamma0xy = Math.Round(st.Gamma0xy, 9),
                kx = Math.Round(st.Kx, 9), ky = Math.Round(st.Ky, 9), kxy = Math.Round(st.Kxy, 9),
                Nx_target = p.Nx, Ny_target = p.Ny, Nxy_target = p.Nxy,
                Mx_target = p.Mx, My_target = p.My, Mxy_target = p.Mxy,
                Nx_result = Math.Round(f.Nx, 4), Ny_result = Math.Round(f.Ny, 4),
                Nxy_result = Math.Round(f.Nxy, 4), Mx_result = Math.Round(f.Mx, 4),
                My_result = Math.Round(f.My, 4), Mxy_result = Math.Round(f.Mxy, 4),
                EAx_sec = Math.Round(sec.EAx, 1),   EAy_sec = Math.Round(sec.EAy, 1),
                zc_x_sec = Math.Round(sec.ZcxMm, 2), zc_y_sec = Math.Round(sec.ZcyMm, 2),
                EIxc_sec = Math.Round(sec.EIxc, 3), EIyc_sec = Math.Round(sec.EIyc, 3),
                EAx_el = Math.Round(sec.EAxEl, 1),   EAy_el = Math.Round(sec.EAyEl, 1),
                zc_x_el = Math.Round(sec.ZcxElMm, 2), zc_y_el = Math.Round(sec.ZcyElMm, 2),
                EIxc_el = Math.Round(sec.EIxcEl, 3), EIyc_el = Math.Round(sec.EIycEl, 3),
                phi_EAx = Math.Round(sec.PhiEAx, 4),  phi_EAy = Math.Round(sec.PhiEAy, 4),
                phi_EIxc = Math.Round(sec.PhiEIxc, 4), phi_EIyc = Math.Round(sec.PhiEIyc, 4),
                check = new
                {
                    passed,
                    verdict = passed ? Loc.S("ShellStrainCheckVerdictOk")
                                     : (chk.Converged ? Loc.S("ShellStrainCheckVerdictFail")
                                                      : Loc.S("ShellStrainCheckNotConverged")),
                    formula = chk.Formula,
                    note = chk.Description,
                },
            };

            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = status,
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

/// <summary>Пакетная проверка прочности слоистой пластины по набору усилий.</summary>
public sealed class ShellLayeredUlsBatchHandler : ITaskHandler
{
    public string Kind => "shell_layered_uls_batch";

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
            var concreteMat = ctx.Database.Materials
                .FirstOrDefault(m => m.Id == plate.ConcreteMaterialId)
                ?? throw new InvalidOperationException($"Материал бетона id={plate.ConcreteMaterialId} не найден.");
            var rebarMat = ctx.Database.Materials
                .FirstOrDefault(m => m.Id == plate.RebarMaterialId)
                ?? throw new InvalidOperationException($"Материал арматуры id={plate.RebarMaterialId} не найден.");

            var forceSet = ctx.Database.ForceSets
                .FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException($"Набор усилий id={task.ForceSetId} не найден.");
            if (forceSet.ShellItems.Count == 0)
                throw new InvalidOperationException($"Набор усилий «{forceSet.Tag}» не содержит строк для пластин.");

            var rows = new List<object>(forceSet.ShellItems.Count);
            int passedCount = 0;

            foreach (var si in forceSet.ShellItems)
            {
                var chk = ShellLayeredCheck.CheckUls(plate, si, concreteMat, rebarMat,
                    task.CalcType, plate.ConcreteDiagramType,
                    out _, out _, out _,
                    tensionOverride: settings.ConsiderConcreteTensionUls);

                bool passed = chk.Converged && chk.Passed;
                if (passed) passedCount++;
                string rowStatus = !chk.Converged ? "not_converged"
                                 : passed ? "ok" : "fail";

                rows.Add(new
                {
                    num = si.Num,
                    label = si.Label,
                    status = rowStatus,
                    passed,
                    formula = chk.Formula,
                    note = chk.Description,
                    iterations = chk.Iterations,
                    residual = Math.Round(chk.Residual, 6),
                });
            }

            bool allOk = passedCount == rows.Count;
            var data = new
            {
                all_ok = allOk,
                converged_count = passedCount,
                total = rows.Count,
                rows,
            };

            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = allOk ? "ok" : "partial",
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
