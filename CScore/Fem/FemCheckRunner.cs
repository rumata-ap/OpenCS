using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: InternalsVisibleTo("CSfea.Tests")]

namespace CScore.Fem;

/// <summary>
/// Запускает нормативные проверки конструктивного элемента по нескольким наборам усилий.
/// </summary>
public static class FemCheckRunner
{
    // ------------------------------------------------------------------ public API

    /// <summary>
    /// Главный метод: перебирает все выбранные наборы усилий, для каждой строки запускает
    /// проверку, собирает таблицу строк и возвращает один CalcResult с DataJson.
    /// </summary>
    public static CalcResult RunMulti(
        FemCheck      check,
        FemMember     member,
        CrossSection? barSection,
        PlateSection? plateSection,
        IReadOnlyList<ForceSet> allMemberForceSets,
        Func<CalcTask, CrossSection, LoadItem, CalcResult> barExecutor,
        Material? concreteMat = null,
        Material? rebarMat    = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        IReadOnlyList<ForceSet> forceSets = check.IsAllSets
            ? allMemberForceSets
            : allMemberForceSets.Where(f => check.GetForceSetIds().Contains(f.Id)).ToList();

        if (forceSets.Count == 0)
            return MakeError(check, created, member.Tag, "Нет наборов усилий для проверки");

        bool isPlate = check.NormCode == "rc_plate_check";

        if (isPlate && plateSection == null)
            return MakeError(check, created, member.Tag, "Не задано пластинчатое сечение");
        if (!isPlate && barSection == null)
            return MakeError(check, created, member.Tag, "Не задано расчётное сечение");

        var rows = new List<CheckRow>();

        foreach (var fs in forceSets)
        {
            var calcType = ExtractCalcType(fs.Tag, check.CalcTypeOverride);
            var task     = BuildCalcTask(check, member, calcType);

            if (isPlate)
            {
                // Для auto-phi1 (SLS): строим lookup NL-строк по метке
                var pParams = PlateCheckParams.Parse(check.ParamsJson);
                Dictionary<string, ShellLoadItem>? nlLookup =
                    (pParams.Phi1Mode == "auto" && pParams.Kind.Contains("sls"))
                        ? BuildNlLookup(fs, forceSets)
                        : null;

                // Явный NL-набор для shell_layered SLS (NlForceSetId > 0):
                Dictionary<string, ShellLoadItem>? explicitNlLookup = null;
                if (pParams.Kind == "shell_layered" && pParams.NlForceSetId > 0)
                {
                    var nlFs = allMemberForceSets.FirstOrDefault(f => f.Id == pParams.NlForceSetId);
                    if (nlFs != null)
                        explicitNlLookup = nlFs.ShellItems
                            .GroupBy(s => s.Label)
                            .ToDictionary(g => g.Key, g => g.First());
                }

                foreach (var shell in fs.ShellItems)
                {
                    ShellLoadItem? nlItem = null;
                    nlLookup?.TryGetValue(shell.Label, out nlItem);
                    // Явный NL-набор имеет приоритет над auto-lookup:
                    if (explicitNlLookup != null)
                        explicitNlLookup.TryGetValue(shell.Label, out nlItem);
                    try
                    {
                        var (util, wf, wd) = RunPlateShellCheck(
                            check, plateSection!, shell, concreteMat, rebarMat, calcType, nlItem);
                        rows.Add(new CheckRow
                        {
                            Label            = shell.Label,
                            ForceSetTag      = fs.Tag,
                            CalcType         = calcType.ToString(),
                            Utilization      = util,
                            Passed           = util <= 1.0,
                            WorstFormula     = wf,
                            WorstDescription = wd
                        });
                    }
                    catch (Exception ex)
                    {
                        rows.Add(new CheckRow
                        {
                            Label            = shell.Label,
                            ForceSetTag      = fs.Tag,
                            CalcType         = calcType.ToString(),
                            Utilization      = 0,
                            Passed           = false,
                            WorstFormula     = "error",
                            WorstDescription = ex.Message
                        });
                    }
                }
            }
            else
            {
                foreach (var item in fs.Items)
                {
                    try
                    {
                        var r    = barExecutor(task, barSection!, item);
                        double util = ExtractUtilization(r.DataJson);
                        var (wf, wd) = ExtractWorstDetail(r.DataJson);
                        rows.Add(new CheckRow
                        {
                            Label            = item.Label,
                            ForceSetTag      = fs.Tag,
                            CalcType         = calcType.ToString(),
                            Utilization      = util,
                            Passed           = util <= 1.0,
                            WorstFormula     = wf,
                            WorstDescription = wd
                        });
                    }
                    catch (Exception ex)
                    {
                        rows.Add(new CheckRow
                        {
                            Label            = item.Label,
                            ForceSetTag      = fs.Tag,
                            CalcType         = calcType.ToString(),
                            Utilization      = 0,
                            Passed           = false,
                            WorstFormula     = "error",
                            WorstDescription = ex.Message
                        });
                    }
                }
            }
        }

        int passed   = rows.Count(r => r.Passed);
        var dataJson = JsonSerializer.Serialize(new
        {
            normCode   = check.NormCode,
            memberTag  = member.Tag,
            totalRows  = rows.Count,
            passedRows = passed,
            failedRows = rows.Count - passed,
            rows       = rows.Select(r => new
            {
                label            = r.Label,
                forceSetTag      = r.ForceSetTag,
                calcType         = r.CalcType,
                utilization      = Math.Round(r.Utilization, 6),
                passed           = r.Passed,
                worstFormula     = r.WorstFormula,
                worstDescription = r.WorstDescription
            }).ToArray()
        });

        return new CalcResult
        {
            TaskId   = 0,
            TaskKind = check.NormCode,
            TaskTag  = check.DisplayTag,
            Created  = created,
            Status   = "ok",
            DataJson = dataJson
        };
    }

    // ------------------------------------------------------------------ plate check

    static (double util, string formula, string desc) RunPlateShellCheck(
        FemCheck      check,
        PlateSection  section,
        ShellLoadItem shell,
        Material?     concreteMat,
        Material?     rebarMat,
        CalcType      calcType,
        ShellLoadItem? nlShell = null)   // соответствующая NL-строка для auto-phi1
    {
        var p = PlateCheckParams.Parse(check.ParamsJson);

        double phi1 = ResolvePhi1(p, shell, nlShell, section.H);

        if (p.Kind.StartsWith("shell_simpl"))
        {
            if (concreteMat == null || rebarMat == null)
                throw new InvalidOperationException(
                    "Не найдены материалы бетона/арматуры плитного сечения");

            var sp = new ShellSimplSolver.SolveParams(
                shell.Nx, shell.Ny, shell.Nxy,
                shell.Mx, shell.My, shell.Mxy,
                p.Kind, p.StepDeg, p.AcrcLimMm, phi1, p.Phi2);

            var r = ShellSimplSolver.Solve(sp, section, concreteMat, rebarMat, calcType);
            return ExtractSimplResult(r, p);
        }

        if (p.Kind == "shell_layered")
        {
            if (concreteMat == null || rebarMat == null)
                throw new InvalidOperationException(
                    "Не найдены материалы бетона/арматуры плитного сечения");

            return RunLayeredCheck(section, shell, concreteMat, rebarMat, calcType, section.ConcreteDiagramType, p, nlShell, p.LtFraction);
        }

        throw new InvalidOperationException($"Неизвестный вид плитной проверки: {p.Kind}");
    }

    /// <summary>
    /// Вычисляет φ1 (долю длительности).
    /// Manual: берём p.Phi1.
    /// Auto: φ1 = clamp(1.0 + 0.4 * amp(NL) / amp(N), 1.0, 1.4).
    /// </summary>
    static double ResolvePhi1(PlateCheckParams p, ShellLoadItem shell, ShellLoadItem? nlShell, double h)
    {
        if (p.Phi1Mode != "auto" || nlShell == null) return p.Phi1;

        double ampN  = ShellAmplitude(shell,   h);
        double ampNL = ShellAmplitude(nlShell, h);

        if (ampN < 1e-12) return 1.4;
        return Math.Clamp(1.0 + 0.4 * ampNL / ampN, 1.0, 1.4);
    }

    /// <summary>Скалярная мера интенсивности оболочечных усилий (кН·м/м).</summary>
    static double ShellAmplitude(ShellLoadItem s, double h)
    {
        double h6 = h / 6.0;
        return Math.Sqrt(s.Mx * s.Mx + s.My * s.My + s.Mxy * s.Mxy
                       + (s.Nx * h6) * (s.Nx * h6)
                       + (s.Ny * h6) * (s.Ny * h6)
                       + (s.Nxy * h6) * (s.Nxy * h6));
    }

    static (double util, string formula, string desc) ExtractSimplResult(
        ShellSimplSolver.SolveResult r, PlateCheckParams p)
    {
        bool isSls = r.CalcType == "sls";

        if (!isSls)
        {
            double util = r.EtaMax ?? 0;
            ShellSimplStripResult? worst = null;
            if (r.WaStrips != null)
                worst = r.WaStrips.Where(s => !s.NoRebar).MaxBy(s => s.Eta);
            else
            {
                var ct = r.CriticalTop?.Strip;
                var cb = r.CriticalBot?.Strip;
                worst  = (ct?.Eta ?? 0) >= (cb?.Eta ?? 0) ? ct : cb;
            }
            return (util,
                    worst?.Name ?? "",
                    worst != null ? worst.Case : "");
        }
        else
        {
            double acrcMax = 0;
            ShellSimplStripResult? worst = null;
            if (r.WaStrips != null)
            {
                worst   = r.WaStrips.Where(s => !s.NoRebar && s.Cracked).MaxBy(s => s.Acrc_mm);
                acrcMax = worst?.Acrc_mm ?? 0;
            }
            else
            {
                var ct = r.CriticalTop?.Strip;
                var cb = r.CriticalBot?.Strip;
                worst  = (ct?.Acrc_mm ?? 0) >= (cb?.Acrc_mm ?? 0) ? ct : cb;
                acrcMax = worst?.Acrc_mm ?? 0;
            }
            double util = p.AcrcLimMm > 1e-12 ? acrcMax / p.AcrcLimMm : 0;
            return (util,
                    worst?.Name ?? "",
                    worst != null ? $"acrc={worst.Acrc_mm:F3} мм" : "");
        }
    }

    static (double util, string formula, string desc) RunLayeredCheck(
        PlateSection     section,
        ShellLoadItem    shell,
        Material         concreteMat,
        Material         rebarMat,
        CalcType         calcType,
        DiagrammType     concreteDiagType,
        PlateCheckParams pParams,
        ShellLoadItem?   nlShell = null,
        double           ltFraction = 0.0)
    {
        // П. 6.1.26: для SLS диаграмма бетона — всегда CalcType.N
        bool isSls = calcType == CalcType.N || calcType == CalcType.NL;
        var diagCalcType = isSls ? CalcType.N : calcType;

        var cDiag = concreteMat.GetDiagramms(concreteDiagType)?[diagCalcType]
            ?? concreteMat.GetDiagramms(DiagrammType.L3)?[diagCalcType]
            ?? throw new InvalidOperationException("Диаграмма бетона не построена");

        var rDiag = rebarMat.GetDiagramms(DiagrammType.L2)?[diagCalcType]
            ?? throw new InvalidOperationException("Диаграмма арматуры не построена");

        var solver = new ShellStrainSolver(section, cDiag, rDiag);

        double[] target = [shell.Nx, shell.Ny, shell.Nxy, shell.Mx, shell.My, shell.Mxy];
        var result = solver.Solve(target);

        if (!result.Converged)
            return (2.0, "НДС", $"Нет сходимости за {result.Iterations} ит., Δ={result.Residual:G2}");

        // ── SLS: ширина раскрытия трещин (п. 8.2.15) ────────────────────────────
        if (isSls)
        {
            if (!concreteMat.chars.TryGetValue(CalcType.N, out var cChSls) || cChSls == null)
                throw new InvalidOperationException("Характеристики бетона CalcType.N не найдены");
            if (!rebarMat.chars.TryGetValue(CalcType.N, out var rChSls) || rChSls == null)
                throw new InvalidOperationException("Характеристики арматуры CalcType.N не найдены");

            return RunLayeredSlsCheck(section, shell, result.StrainState, cChSls, rChSls,
                                      calcType, pParams.Phi2, pParams.AcrcLimMm,
                                      concreteMat, rebarMat, concreteDiagType, nlShell, ltFraction);
        }

        // ── ULS: деформационная проверка п. 8.1.30 делегирует в ShellLayeredCheck ──
        var r = ShellLayeredCheck.CheckUls(section, shell, concreteMat, rebarMat, calcType,
            concreteDiagType, out _, out _, out _);
        return (r.Utilization, r.Formula, r.Description);
    }

    /// <summary>
    /// Вычисляет максимальную ширину раскрытия трещин (мм) по п. 8.2.15–8.2.18 СП 63
    /// для заданного деформационного состояния пластины.
    /// Перебирает все арматурные слои и оба направления (X, Y).
    /// </summary>
    static (double acrcMm, string dir) ComputeAcrcForShell(
        PlateSection     section,
        ShellLoadItem    shell,
        ShellStrainState st,
        MaterialChars    cCh,
        MaterialChars    rCh,
        double           phi1,
        double           phi2)
    {
        double Eb     = cCh.E;
        double Rb_ser = Math.Abs(cCh.Fc);
        double Rbt    = cCh.Ft;
        double Es     = rCh.E;
        double Rs_ser = Math.Abs(rCh.Ft);

        double Eb_red    = Rb_ser / 0.0015;
        double alphaFull = Es / Eb;
        double alpha     = Es / Eb_red;

        double H       = section.H;
        double bestAcrc = 0.0;
        string bestDir  = "";

        foreach (var layer in section.RebarLayers)
        {
            // ── Направление X ─────────────────────────────────────────────────
            if (layer.Asx > 1e-14)
            {
                double acrcX = ComputeAcrcStrip(
                    st.EpsX(layer.Zsx),
                    shell.Mx, shell.Nx,
                    H, H / 2.0 + Math.Abs(layer.Zsx), H / 2.0 - Math.Abs(layer.Zsx),
                    layer.Asx,
                    layer.DiameterX > 1e-9 ? layer.DiameterX : 0.012,
                    Rbt, Rb_ser, Es, Rs_ser, Eb_red, alphaFull, alpha,
                    phi1, phi2);
                if (acrcX > bestAcrc) { bestAcrc = acrcX; bestDir = "п.8.2.15 x"; }
            }

            // ── Направление Y ─────────────────────────────────────────────────
            if (layer.Asy > 1e-14)
            {
                double acrcY = ComputeAcrcStrip(
                    st.EpsY(layer.Zsy),
                    shell.My, shell.Ny,
                    H, H / 2.0 + Math.Abs(layer.Zsy), H / 2.0 - Math.Abs(layer.Zsy),
                    layer.Asy,
                    layer.DiameterY > 1e-9 ? layer.DiameterY : 0.012,
                    Rbt, Rb_ser, Es, Rs_ser, Eb_red, alphaFull, alpha,
                    phi1, phi2);
                if (acrcY > bestAcrc) { bestAcrc = acrcY; bestDir = "п.8.2.15 y"; }
            }
        }

        return (bestAcrc, bestDir);
    }

    /// <summary>
    /// Формулы п. 8.2.15–8.2.18 СП 63 для одной полосы (X или Y).
    /// </summary>
    /// <param name="eps_s">Деформация в арматуре (из ShellStrainState).</param>
    /// <param name="M_des">Расчётный момент полосы (кН·м/м).</param>
    /// <param name="N_des">Расчётная продольная сила полосы (кН/м).</param>
    /// <param name="h">Полная толщина пластины (м).</param>
    /// <param name="h0">Рабочая высота (от сжатой грани до растянутой арматуры, м).</param>
    /// <param name="aPrime">Защитный слой сжатой грани (м).</param>
    /// <param name="As_t">Площадь растянутой арматуры (м²/м).</param>
    /// <param name="ds">Диаметр арматуры (м).</param>
    internal static double ComputeAcrcStrip(
        double eps_s,
        double M_des, double N_des,
        double h, double h0, double aPrime,
        double As_t, double ds,
        double Rbt, double Rb_ser, double Es, double Rs_ser,
        double Eb_red, double alphaFull, double alpha,
        double phi1, double phi2)
    {
        if (eps_s <= 0.0) return 0.0;

        double sigma_s = Math.Min(Es * eps_s, Rs_ser);
        if (sigma_s < 1e-3) return 0.0;

        // Приведённое сечение (As_c = 0)
        ShellSimplSolver.FullSectionProps(h, h0, aPrime, As_t, 0.0, alphaFull,
            out double A_red, out double I_red);
        double S_red  = h * h / 2.0 + alphaFull * As_t * h0;
        double ycFull = S_red / A_red;
        double yt     = h - ycFull;
        double Wred   = I_red / yt;
        double Wpl    = 1.3 * Wred;
        double ex     = Wred / A_red;

        // Момент трещинообразования (п. 8.2.11)
        double mcrc = Math.Max(0.0, Rbt * Wpl - N_des * ex);

        if (M_des <= mcrc) return 0.0;

        // ψs — коэффициент неравномерности деформаций (п. 8.2.18)
        double xm_crc  = ShellSimplSolver.NeutralAxis(h0, aPrime, As_t, 0.0, alpha);
        double zs_crc  = h0 - xm_crc / 3.0;
        double sigma_s_crc = 0.0;
        if (zs_crc > 1e-9)
            sigma_s_crc = Math.Min(Math.Max(0.0, mcrc / (zs_crc * As_t)), Rs_ser);

        double psi_s = sigma_s > 1e-3
            ? Math.Clamp(1.0 - 0.8 * sigma_s_crc / sigma_s, 0.1, 1.0)
            : 1.0;

        // Нейтральная ось при расчётном моменте (п. 8.2.17)
        double xm  = ShellSimplSolver.NeutralAxis(h0, aPrime, As_t, 0.0, alpha);
        double h_bt = Math.Min(Math.Max(h - xm, 2.0 * aPrime), h0 / 2.0);
        double ls_raw = 0.5 * h_bt / As_t * ds;
        double ls_min = Math.Max(10.0 * ds, 0.10);
        double ls_max = Math.Min(40.0 * ds, 0.40);
        double ls_m   = Math.Clamp(ls_raw, ls_min, ls_max);

        // φ3 — вид нагружения (п. 8.2.15): 1.2 при наличии сжимающей осевой силы
        double phi3 = N_des > 1e-3 ? 1.2 : 1.0;

        double acrc_m = phi1 * phi2 * phi3 * psi_s * (sigma_s / Es) * ls_m;
        return acrc_m * 1000.0;
    }

    /// <summary>
    /// Расчёт ширины раскрытия трещин по п. 8.2.15–8.2.18 СП 63
    /// для слоистой модели пластины (shell_layered).
    /// CalcType.N → непродолжительное раскрытие (acrc1 + acrc2 − acrc3, п. 8.2.7).
    /// CalcType.NL → продолжительное раскрытие (только acrc1, φ1 = 1.4).
    /// </summary>
    static (double util, string formula, string desc) RunLayeredSlsCheck(
        PlateSection     section,
        ShellLoadItem    shell,
        ShellStrainState st,
        MaterialChars    cCh,
        MaterialChars    rCh,
        CalcType         calcType,
        double           phi2,
        double           acrcLimMm,
        Material         concreteMat,
        Material         rebarMat,
        DiagrammType     concreteDiagType,
        ShellLoadItem?   nlShell,
        double           ltFraction = 0.0)
    {
        string suffix = "";
        double acrcMax;
        string acrcDir;

        if (calcType == CalcType.NL)
        {
            // Только acrc1 (продолжительное раскрытие)
            (acrcMax, acrcDir) = ComputeAcrcForShell(section, shell, st, cCh, rCh, phi1: 1.4, phi2);
        }
        else // CalcType.N → непродолжительное, п. 8.2.7
        {
            // acrc2: N-набор, φ1 = 1.0
            var (acrc2, dir2) = ComputeAcrcForShell(section, shell, st, cCh, rCh, phi1: 1.0, phi2);

            if (nlShell != null)
            {
                // Решить ShellStrainSolver для NL-набора (диаграмма CalcType.N по п. 6.1.26)
                var cDiagNl = concreteMat.GetDiagramms(concreteDiagType)?[CalcType.N]
                    ?? concreteMat.GetDiagramms(DiagrammType.L3)?[CalcType.N]
                    ?? throw new InvalidOperationException("Диаграмма бетона N не построена (NL-набор)");
                var rDiagNl = rebarMat.GetDiagramms(DiagrammType.L2)?[CalcType.N]
                    ?? throw new InvalidOperationException("Диаграмма арматуры N не построена (NL-набор)");

                var solverNl = new ShellStrainSolver(section, cDiagNl, rDiagNl);
                double[] targetNl = [nlShell.Nx, nlShell.Ny, nlShell.Nxy,
                                      nlShell.Mx, nlShell.My, nlShell.Mxy];
                var resNl = solverNl.Solve(targetNl);

                if (!resNl.Converged)
                    suffix = $" (NL-набор: нет сход. Δ={resNl.Residual:G2})";

                var stNl = resNl.Converged ? resNl.StrainState : st;

                // acrc1: NL-набор, φ1 = 1.4
                var (acrc1, dir1) = ComputeAcrcForShell(section, nlShell, stNl, cCh, rCh, phi1: 1.4, phi2);
                // acrc3: NL-набор, φ1 = 1.0
                var (acrc3, _)   = ComputeAcrcForShell(section, nlShell, stNl, cCh, rCh, phi1: 1.0, phi2);

                // п. 8.2.7: acrc_непрод = acrc1 + acrc2 − acrc3
                acrcMax = acrc1 + acrc2 - acrc3;
                acrcDir = dir1.Length > 0 ? dir1 : dir2;
            }
            else
            {
                // Нет явного NL-набора
                if (ltFraction > 1e-9)
                {
                    // Виртуальный NL = N * LtFraction
                    var virtualNl = new ShellLoadItem
                    {
                        Label = shell.Label,
                        Nx  = shell.Nx  * ltFraction,
                        Ny  = shell.Ny  * ltFraction,
                        Nxy = shell.Nxy * ltFraction,
                        Mx  = shell.Mx  * ltFraction,
                        My  = shell.My  * ltFraction,
                        Mxy = shell.Mxy * ltFraction,
                    };

                    var cDiagVirt = concreteMat.GetDiagramms(concreteDiagType)?[CalcType.N]
                        ?? concreteMat.GetDiagramms(DiagrammType.L3)?[CalcType.N]
                        ?? throw new InvalidOperationException("Диаграмма бетона N не построена (virtual NL)");
                    var rDiagVirt = rebarMat.GetDiagramms(DiagrammType.L2)?[CalcType.N]
                        ?? throw new InvalidOperationException("Диаграмма арматуры N не построена (virtual NL)");

                    var solverVirt = new ShellStrainSolver(section, cDiagVirt, rDiagVirt);
                    double[] targetVirt = [virtualNl.Nx, virtualNl.Ny, virtualNl.Nxy,
                                           virtualNl.Mx, virtualNl.My, virtualNl.Mxy];
                    var resVirt = solverVirt.Solve(targetVirt);

                    suffix = resVirt.Converged ? $" (NL=N×{ltFraction:G})" : $" (NL=N×{ltFraction:G}, нет сход.)";
                    var stVirt = resVirt.Converged ? resVirt.StrainState : st;

                    var (acrc1, dir1) = ComputeAcrcForShell(section, virtualNl, stVirt, cCh, rCh, phi1: 1.4, phi2);
                    var (acrc3, _)    = ComputeAcrcForShell(section, virtualNl, stVirt, cCh, rCh, phi1: 1.0, phi2);

                    acrcMax = acrc1 + acrc2 - acrc3;
                    acrcDir = dir1.Length > 0 ? dir1 : dir2;
                }
                else
                {
                    acrcMax = acrc2;
                    acrcDir = dir2;
                    suffix  = " (NL-набор не задан, acrc≈acrc2)";
                }
            }
        }

        acrcMax = Math.Max(acrcMax, 0.0);
        double util = acrcLimMm > 1e-12 ? acrcMax / acrcLimMm : 0.0;

        string desc = acrcMax > 1e-6
            ? $"acrc={acrcMax:F3} мм{suffix}"
            : $"трещин нет{suffix}";

        return (util, acrcDir, desc);
    }

    /// <summary>
    /// Для N-набора усилий ищет соответствующий NL-набор среди всех наборов
    /// (по тегу: "... (N)" → "... (NL)") и возвращает словарь label→ShellLoadItem.
    /// </summary>
    static Dictionary<string, ShellLoadItem>? BuildNlLookup(
        ForceSet currentNSet, IReadOnlyList<ForceSet> allSets)
    {
        // Тег N-набора: "Плита — РСН 1 (N)" → ищем "Плита — РСН 1 (NL)"
        string nTag  = currentNSet.Tag ?? "";
        string nlTag = nTag.EndsWith("(N)")
            ? nTag[..^3].TrimEnd() + "(NL)"
            : null!;

        if (nlTag == null) return null;

        var nlSet = allSets.FirstOrDefault(f => f.Tag == nlTag);
        if (nlSet == null) return null;

        return nlSet.ShellItems.ToDictionary(s => s.Label, s => s);
    }

    // ------------------------------------------------------------------ bar check helpers

    /// <summary>Совместимость: одиночный набор усилий — делегирует в RunMulti.</summary>
    public static CalcResult Run(
        FemCheck     check,
        FemMember    member,
        CrossSection section,
        ForceSet     forceSet,
        Func<CalcTask, CrossSection, LoadItem, CalcResult>? executor = null)
    {
        if (executor == null)
        {
            var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return MakeError(check, created, member.Tag, "Не задан executor");
        }
        return RunMulti(check, member, section, null, [forceSet], executor);
    }

    // ------------------------------------------------------------------ shared helpers

    /// <summary>Определяет CalcType из тега набора усилий или из переопределения.</summary>
    public static CalcType ExtractCalcType(string forceSetTag, string? overrideValue)
    {
        if (!string.IsNullOrEmpty(overrideValue))
            return overrideValue switch
            {
                "CL" => CalcType.CL,
                "N"  => CalcType.N,
                "NL" => CalcType.NL,
                _    => CalcType.C
            };

        if (forceSetTag.Contains("(NL)")) return CalcType.NL;
        if (forceSetTag.Contains("(CL)")) return CalcType.CL;
        if (forceSetTag.Contains("(N)"))  return CalcType.N;
        if (forceSetTag.Contains("(C)"))  return CalcType.C;
        return CalcType.C;
    }

    /// <summary>Извлекает формулу и описание определяющей проверки из DataJson CalcResult.</summary>
    public static (string formula, string description) ExtractWorstDetail(string dataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (!doc.RootElement.TryGetProperty("details", out var details))
                return ("", "");

            string bestFormula = "", bestDesc = "";
            double bestRatio = -1;
            foreach (var d in details.EnumerateArray())
            {
                double ratio = d.TryGetProperty("ratio", out var r) ? r.GetDouble() : 0;
                if (ratio > bestRatio)
                {
                    bestRatio   = ratio;
                    bestFormula = d.TryGetProperty("formula",     out var f)  ? f.GetString()  ?? "" : "";
                    bestDesc    = d.TryGetProperty("description", out var ds) ? ds.GetString() ?? "" : "";
                }
            }
            return (bestFormula, bestDesc);
        }
        catch { return ("", ""); }
    }

    /// <summary>Возвращает CalcResult с наибольшей утилизацией из двух.</summary>
    public static CalcResult PickWorst(CalcResult? a, CalcResult b)
    {
        if (a == null) return b;
        return ExtractUtilization(b.DataJson) > ExtractUtilization(a.DataJson) ? b : a;
    }

    /// <summary>Подготавливает CalcTask из параметров FemCheck и FemMember.</summary>
    public static CalcTask BuildCalcTask(FemCheck check, FemMember member, CalcType? calcType = null)
    {
        var paramsJson = check.ParamsJson
            ?? FemDesignParams.Parse(member.DesignParamsJson).ToJson();
        return new CalcTask
        {
            Kind       = check.NormCode,
            Tag        = $"{member.Tag}/{check.NormCode}",
            ParamsJson = paramsJson
        };
    }

    static double ExtractUtilization(string dataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            return doc.RootElement.TryGetProperty("utilization", out var u) ? u.GetDouble() : 0;
        }
        catch { return 0; }
    }

    static CalcResult MakeError(FemCheck check, string created, string memberTag, string message) => new()
    {
        TaskId   = 0,
        TaskKind = check.NormCode,
        TaskTag  = check.DisplayTag,
        Created  = created,
        Status   = "error",
        DataJson = JsonSerializer.Serialize(new { error = message, memberTag })
    };

    record CheckRow
    {
        public string Label            { get; init; } = "";
        public string ForceSetTag      { get; init; } = "";
        public string CalcType         { get; init; } = "";
        public double Utilization      { get; init; }
        public bool   Passed           { get; init; }
        public string WorstFormula     { get; init; } = "";
        public string WorstDescription { get; init; } = "";
    }
}
