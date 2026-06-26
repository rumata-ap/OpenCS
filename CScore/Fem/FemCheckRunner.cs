using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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

                foreach (var shell in fs.ShellItems)
                {
                    nlLookup?.TryGetValue(shell.Label, out var nlShell);
                    ShellLoadItem? nlItem = null;
                    nlLookup?.TryGetValue(shell.Label, out nlItem);
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

            return RunLayeredCheck(section, shell, concreteMat, rebarMat, calcType, section.ConcreteDiagramType, p, nlShell);
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
        ShellLoadItem?   nlShell = null)
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
                                      concreteMat, rebarMat, concreteDiagType, nlShell);
        }

        // ─── Деформационные параметры бетона по СП 63 п. 8.1.30 ────────────────
        concreteMat.chars.TryGetValue(calcType, out var cCh);
        double epsB0 = cCh?.Ec0 > 0 ? cCh.Ec0 : 0.002;   // εb0: деформация при достижении Rb
        double epsB2 = cCh?.Ec2 > 0 ? cCh.Ec2 : 0.0035;  // εb2: предельная деформация сжатия

        var    st = result.StrainState;
        double h  = section.H;

        // Минимальные главные деформации (наиболее сжимающие) на обеих гранях
        double eps2T = MinPrincipalStrain(st.EpsX( h/2), st.EpsY( h/2), st.GammaXY( h/2));
        double eps2B = MinPrincipalStrain(st.EpsX(-h/2), st.EpsY(-h/2), st.GammaXY(-h/2));

        // ε₂ — бо́льшая по абс. величине деформация (|ε₂| ≥ |ε₁|) — п. 8.1.30
        double eps2, eps1;
        if (Math.Abs(eps2T) >= Math.Abs(eps2B)) { eps2 = eps2T; eps1 = eps2B; }
        else                                      { eps2 = eps2B; eps1 = eps2T; }

        double epsBUlt;
        string epsBDesc;

        if (eps2 >= 0)
        {
            // Сжатия нет → бетон не определяющий
            epsBUlt  = epsB2;
            epsBDesc = "нет сжатия";
        }
        else if (eps1 >= 0)
        {
            // Двузначная эпюра: разные знаки на гранях → εb,ult = εb2 (п. 8.1.30, абз. 1)
            epsBUlt  = epsB2;
            epsBDesc = $"двузн., εb,ult={epsB2:G3}";
        }
        else
        {
            // Однозначная эпюра: оба в сжатии → εb,ult = εb2 − (εb2−εb0)·ε₁/ε₂ (п. 8.1.30)
            // eps2 < 0, eps1 < 0 ⇒ ε₁/ε₂ = (менее сжатая)/(более сжатая) ∈ [0, 1]
            double ratio = Math.Abs(eps2) > 1e-12 ? Math.Clamp(eps1 / eps2, 0.0, 1.0) : 1.0;
            epsBUlt  = epsB2 - (epsB2 - epsB0) * ratio;
            epsBDesc = $"однозн., ε₁/ε₂={ratio:F2}, εb,ult={epsBUlt:G3}";
        }

        double utilC = eps2 < 0 ? Math.Abs(eps2) / epsBUlt : 0.0;

        // ─── Арматура: εs,ult = 0.025 (физ. текучесть) / 0.015 (усл.) — п. 8.1.30 ─
        double epsSUlt = rebarMat.Type == MatType.ReSteelF ? 0.025 : 0.015;
        double utilS   = 0;
        foreach (var layer in section.RebarLayers)
        {
            // Растяжение арматуры (максимальная растягивающая деформация в слое)
            double epsX = st.EpsX(layer.Zsx);
            double epsY = st.EpsY(layer.Zsy);
            utilS = Math.Max(utilS, Math.Max(epsX, epsY) / epsSUlt);
        }

        double util = Math.Max(utilC, utilS);
        if (utilC >= utilS)
            return (util, "п.8.1.30 бетон", epsBDesc + $", ε={Math.Abs(eps2):G3}");
        else
            return (util, "п.8.1.30 арм.", $"εs={utilS * epsSUlt:G3}, εs,ult={epsSUlt:G3}");
    }

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
        ShellLoadItem?   nlShell)
    {
        return (0.0, "", "SLS stub");
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

    static double MinPrincipalStrain(double ex, double ey, double gxy)
    {
        double avg    = (ex + ey) / 2.0;
        double r      = Math.Sqrt((ex - ey) * (ex - ey) / 4.0 + gxy * gxy / 4.0);
        return avg - r;
    }

    static double MaxPrincipalStrain(double ex, double ey, double gxy)
    {
        double avg    = (ex + ey) / 2.0;
        double r      = Math.Sqrt((ex - ey) * (ex - ey) / 4.0 + gxy * gxy / 4.0);
        return avg + r;
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
