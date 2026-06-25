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
                foreach (var shell in fs.ShellItems)
                {
                    try
                    {
                        var (util, wf, wd) = RunPlateShellCheck(
                            check, plateSection!, shell, concreteMat, rebarMat, calcType);
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
        FemCheck     check,
        PlateSection section,
        ShellLoadItem shell,
        Material?    concreteMat,
        Material?    rebarMat,
        CalcType     calcType)
    {
        var p = PlateCheckParams.Parse(check.ParamsJson);

        if (p.Kind.StartsWith("shell_simpl"))
        {
            if (concreteMat == null || rebarMat == null)
                throw new InvalidOperationException(
                    "Не найдены материалы бетона/арматуры плитного сечения");

            var sp = new ShellSimplSolver.SolveParams(
                shell.Nx, shell.Ny, shell.Nxy,
                shell.Mx, shell.My, shell.Mxy,
                p.Kind, p.StepDeg, p.AcrcLimMm, p.Phi1, p.Phi2);

            var r = ShellSimplSolver.Solve(sp, section, concreteMat, rebarMat, calcType);
            return ExtractSimplResult(r, p);
        }

        if (p.Kind == "shell_layered")
        {
            if (concreteMat == null || rebarMat == null)
                throw new InvalidOperationException(
                    "Не найдены материалы бетона/арматуры плитного сечения");

            return RunLayeredCheck(section, shell, concreteMat, rebarMat, calcType, section.ConcreteDiagramType);
        }

        throw new InvalidOperationException($"Неизвестный вид плитной проверки: {p.Kind}");
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
        PlateSection section,
        ShellLoadItem shell,
        Material    concreteMat,
        Material    rebarMat,
        CalcType    calcType,
        DiagrammType concreteDiagType)
    {
        // Строим диаграммы из материалов
        var cDiag = concreteMat.GetDiagramms(concreteDiagType)?[calcType]
            ?? concreteMat.GetDiagramms(DiagrammType.L3)?[calcType]
            ?? throw new InvalidOperationException("Диаграмма бетона не построена");

        var rDiag = rebarMat.GetDiagramms(DiagrammType.L2)?[calcType]
            ?? throw new InvalidOperationException("Диаграмма арматуры не построена");

        // Диаграммы для слоёв с индивидуальными материалами — не нужны (используем глобальные)
        var solver = new ShellStrainSolver(section, cDiag, rDiag);

        double[] target = [shell.Nx, shell.Ny, shell.Nxy, shell.Mx, shell.My, shell.Mxy];
        var result = solver.Solve(target);

        if (!result.Converged)
            return (2.0, "НДС", $"Нет сходимости за {result.Iterations} ит., Δ={result.Residual:G2}");

        // Утилизация через максимальную сжимающую деформацию бетона
        var st  = result.StrainState;
        double h = section.H;
        double zTop = h / 2.0;
        double zBot = -h / 2.0;

        // Главные деформации на верхней и нижней гранях
        double eps2_top = MinPrincipalStrain(st.EpsX(zTop), st.EpsY(zTop), st.GammaXY(zTop));
        double eps2_bot = MinPrincipalStrain(st.EpsX(zBot), st.EpsY(zBot), st.GammaXY(zBot));

        // Предельная деформация бетона по СП 63 (ε_b2 ≈ 0.0035 для B20–B45)
        const double epsCu = 0.0035;
        double utilC = Math.Max(Math.Abs(eps2_top), Math.Abs(eps2_bot)) / epsCu;

        // Максимальная деформация арматуры по z-координатам слоёв
        double utilS = 0;
        foreach (var layer in section.RebarLayers)
        {
            double epsX = st.EpsX(layer.Zsx);
            double epsY = st.EpsY(layer.Zsy);
            utilS = Math.Max(utilS, Math.Max(Math.Abs(epsX), Math.Abs(epsY)) / 0.025);
        }

        double util  = Math.Max(utilC, utilS);
        string which = utilC >= utilS ? "бетон" : "арматура";
        return (util, "ε-критерий", $"{which}: ε={util * (utilC >= utilS ? epsCu : 0.025):G3}");
    }

    static double MinPrincipalStrain(double ex, double ey, double gxy)
    {
        double avg    = (ex + ey) / 2.0;
        double radius = Math.Sqrt(Math.Pow((ex - ey) / 2.0, 2) + Math.Pow(gxy / 2.0, 2));
        return avg - radius;
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
