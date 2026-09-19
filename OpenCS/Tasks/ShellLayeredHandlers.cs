using System;
using System.Linq;
using System.Text.Json;
using CScore;
using CScore.Fem;
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

            var p = ShellStrainParams.Parse(task.ParamsJson);
            var rows = new List<object>(forceSet.ShellItems.Count);
            int passedCount = 0;

            foreach (var si in forceSet.ShellItems)
            {
                var (nx, ny, nxy) = p.AutoStressToForce ? si.ResolveN(plate.H) : (si.Nx, si.Ny, si.Nxy);
                var resolved = new ShellLoadItem
                {
                    Num = si.Num, Label = si.Label,
                    Nx = nx, Ny = ny, Nxy = nxy,
                    Mx = si.Mx, My = si.My, Mxy = si.Mxy,
                    Qx = si.Qx, Qy = si.Qy,
                };

                var chk = ShellLayeredCheck.CheckUls(plate, resolved, concreteMat, rebarMat,
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

/// <summary>Данные результата shell_layered_sls в DataJson — полная картина трещин
/// (все полосы), не только победившая.</summary>
public sealed class ShellLayeredSlsResultData
{
    public List<ShellCrackStripResult> Strips { get; set; } = [];
    /// <summary>Индекс победившей записи в Strips; -1, если ни одна не растрескалась.</summary>
    public int GoverningIndex { get; set; } = -1;
    public double AcrcMaxMm { get; set; }
    public double AcrcLimMm { get; set; }
    public double Utilization { get; set; }
    public bool Passed { get; set; }
    public bool Converged { get; set; } = true;
    /// <summary>Общие числовые переменные результата для отчёта.</summary>
    public Dictionary<string, double> Variables { get; set; } = [];
}

/// <summary>
/// Задача «Ширина раскрытия трещин слоистой пластины» (SLS): одно состояние усилий,
/// полная картина трещин (все армслои × X/Y). Без комбинации длительного/
/// непродолжительного раскрытия (п. 8.2.7) — φ1 задаётся вручную.
/// </summary>
public sealed class ShellLayeredSlsHandler : ITaskHandler
{
    public string Kind => "shell_layered_sls";

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
            var concreteMat = ctx.Database.Materials.FirstOrDefault(m => m.Id == plate.ConcreteMaterialId)
                ?? throw new InvalidOperationException($"Материал бетона id={plate.ConcreteMaterialId} не найден.");
            var rebarMat = ctx.Database.Materials.FirstOrDefault(m => m.Id == plate.RebarMaterialId)
                ?? throw new InvalidOperationException($"Материал арматуры id={plate.RebarMaterialId} не найден.");

            var p = ShellLayeredSlsParams.Parse(task.ParamsJson);
            var shell = new ShellLoadItem
            {
                Nx = p.Nx, Ny = p.Ny, Nxy = p.Nxy,
                Mx = p.Mx, My = p.My, Mxy = p.Mxy,
            };

            // П. 6.1.26: диаграмма всегда CalcType.N, независимо от task.CalcType.
            var cDiag = concreteMat.GetDiagramms(plate.ConcreteDiagramType)?[CalcType.N]
                ?? concreteMat.GetDiagramms(DiagrammType.L3)?[CalcType.N]
                ?? throw new InvalidOperationException("Диаграмма бетона не построена.");
            var rDiag = rebarMat.GetDiagramms(
                    DiagrammCompatibility.Coerce(rebarMat.Type, DiagrammType.L2))?[CalcType.N]
                ?? throw new InvalidOperationException("Диаграмма арматуры не построена.");

            var solver = new ShellStrainSolver(plate, cDiag, rDiag);
            double[] target = [shell.Nx, shell.Ny, shell.Nxy, shell.Mx, shell.My, shell.Mxy];
            var solveResult = solver.Solve(target);

            if (!solveResult.Converged)
            {
                return new CalcResult
                {
                    TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                    Created = created, Status = "not_converged",
                    DataJson = JsonSerializer.Serialize(new ShellLayeredSlsResultData { Converged = false }),
                };
            }

            // Материальные характеристики также всегда CalcType.N (п. 6.1.26).
            var cCh = concreteMat.GetChars(CalcType.N);
            if (cCh == null)
                throw new InvalidOperationException("Характеристики бетона CalcType.N не найдены.");
            var rCh = rebarMat.GetChars(CalcType.N);
            if (rCh == null)
                throw new InvalidOperationException("Характеристики арматуры CalcType.N не найдены.");

            // П. 8.2.8/8.2.14 — M_crc по деформационной модели вместо Rbt·γ·Wred; п. 8.2.18 —
            // σs,crc в том же сечении с трещиной при M = M_crc. Обе величины даёт один поиск.
            var crackingSolver = new ShellCrackingSolver(plate, cDiag, rDiag);

            var strips = ShellLayeredCrackWidth.ComputeAll(
                plate, shell, solveResult.StrainState, cCh, rCh,
                p.Phi1, p.Phi2, p.SigmaSCrc, p.WplGamma,
                (t, alongX) => crackingSolver.Solve(t, alongX)).ToList();
            var governing = strips.Where(s => s.Cracked).OrderByDescending(s => s.AcrcMm).FirstOrDefault();
            int governingIndex = governing != null ? strips.IndexOf(governing) : -1;
            double acrcMax = governing?.AcrcMm ?? 0.0;
            double utilization = p.AcrcLimMm > 1e-12 ? acrcMax / p.AcrcLimMm : 0.0;
            bool passed = utilization <= 1.0;

            var data = new ShellLayeredSlsResultData
            {
                Strips = strips,
                GoverningIndex = governingIndex,
                AcrcMaxMm = acrcMax,
                AcrcLimMm = p.AcrcLimMm,
                Utilization = utilization,
                Passed = passed,
                Converged = true,
                Variables = new Dictionary<string, double>
                {
                    ["phi1"] = p.Phi1,
                    ["phi2"] = p.Phi2,
                    ["acrc_lim_mm"] = p.AcrcLimMm,
                    ["acrc_max_mm"] = acrcMax,
                    ["utilization"] = utilization,
                },
            };

            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = passed ? "ok" : "not_passed",
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

/// <summary>Задача «Ширина раскрытия трещин слоистой пластины» (SLS), по набору усилий.
/// На строку — только победившая полоса (ComputeWorst), чтобы не раздувать DataJson.</summary>
public sealed class ShellLayeredSlsBatchHandler : ITaskHandler
{
    public string Kind => "shell_layered_sls_batch";

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
            var concreteMat = ctx.Database.Materials.FirstOrDefault(m => m.Id == plate.ConcreteMaterialId)
                ?? throw new InvalidOperationException($"Материал бетона id={plate.ConcreteMaterialId} не найден.");
            var rebarMat = ctx.Database.Materials.FirstOrDefault(m => m.Id == plate.RebarMaterialId)
                ?? throw new InvalidOperationException($"Материал арматуры id={plate.RebarMaterialId} не найден.");
            var forceSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException($"Набор усилий id={task.ForceSetId} не найден.");
            if (forceSet.ShellItems.Count == 0)
                throw new InvalidOperationException($"Набор усилий «{forceSet.Tag}» не содержит строк для пластин.");

            var p = ShellLayeredSlsParams.Parse(task.ParamsJson);
            var cCh = concreteMat.GetChars(CalcType.N)
                ?? throw new InvalidOperationException("Характеристики бетона CalcType.N не найдены.");
            var rCh = rebarMat.GetChars(CalcType.N)
                ?? throw new InvalidOperationException("Характеристики арматуры CalcType.N не найдены.");
            var cDiag = concreteMat.GetDiagramms(plate.ConcreteDiagramType)?[CalcType.N]
                ?? concreteMat.GetDiagramms(DiagrammType.L3)?[CalcType.N]
                ?? throw new InvalidOperationException("Диаграмма бетона не построена.");
            var rDiag = rebarMat.GetDiagramms(
                    DiagrammCompatibility.Coerce(rebarMat.Type, DiagrammType.L2))?[CalcType.N]
                ?? throw new InvalidOperationException("Диаграмма арматуры не построена.");

            // Один поиск состояния трещинообразования на сечение (см. одиночную задачу);
            // сам решатель кэша не держит, но и не зависит от набора усилий.
            var crackingSolver = new ShellCrackingSolver(plate, cDiag, rDiag);

            var rows = new List<object>(forceSet.ShellItems.Count);
            int okCount = 0;
            foreach (var si in forceSet.ShellItems)
            {
                var (nx, ny, nxy) = p.AutoStressToForce
                    ? si.ResolveN(plate.H)
                    : (si.Nx, si.Ny, si.Nxy);
                var shell = new ShellLoadItem
                {
                    Num = si.Num, Label = si.Label,
                    Nx = nx, Ny = ny, Nxy = nxy,
                    Mx = si.Mx, My = si.My, Mxy = si.Mxy,
                };

                var solver = new ShellStrainSolver(plate, cDiag, rDiag);
                double[] target = [shell.Nx, shell.Ny, shell.Nxy, shell.Mx, shell.My, shell.Mxy];
                var solveResult = solver.Solve(target);
                string rowStatus;
                double? acrcMm = null;
                double? angle = null;
                string direction = "";
                string face = "";

                if (!solveResult.Converged)
                {
                    rowStatus = "not_converged";
                }
                else
                {
                    var worst = ShellLayeredCrackWidth.ComputeWorst(
                        plate, shell, solveResult.StrainState, cCh, rCh,
                        p.Phi1, p.Phi2, p.SigmaSCrc, p.WplGamma,
                        (t, alongX) => crackingSolver.Solve(t, alongX));
                    acrcMm = worst?.AcrcMm ?? 0.0;
                    angle = worst?.CrackAngleDeg;
                    direction = worst?.Direction ?? "";
                    face = worst is null ? "" : (worst.IsTop ? "верх" : "низ");
                    bool passed = p.AcrcLimMm > 1e-12
                        ? acrcMm.Value / p.AcrcLimMm <= 1.0
                        : true;
                    rowStatus = passed ? "ok" : "not_passed";
                }

                if (rowStatus == "ok") okCount++;
                rows.Add(new
                {
                    num = si.Num,
                    label = si.Label,
                    status = rowStatus,
                    acrc_mm = acrcMm,
                    crack_angle_deg = angle,
                    direction,
                    face,
                });
            }

            bool allOk = okCount == rows.Count;
            var data = new { all_ok = allOk, ok_count = okCount, total = rows.Count, rows };
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
