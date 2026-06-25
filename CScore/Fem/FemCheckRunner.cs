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
    /// CalcTask, собирает таблицу строк и возвращает один CalcResult с DataJson.
    /// </summary>
    public static CalcResult RunMulti(
        FemCheck      check,
        FemMember     member,
        CrossSection? barSection,
        PlateSection? plateSection,
        IReadOnlyList<ForceSet> allMemberForceSets,
        Func<CalcTask, CrossSection, LoadItem, CalcResult> barExecutor)
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
            var task = BuildCalcTask(check, member, calcType);

            if (isPlate)
            {
                foreach (var shell in fs.ShellItems)
                {
                    rows.Add(new CheckRow
                    {
                        Label            = shell.Label,
                        ForceSetTag      = fs.Tag,
                        CalcType         = calcType.ToString(),
                        Utilization      = 0,
                        Passed           = false,
                        WorstFormula     = "",
                        WorstDescription = "rc_plate_check не реализован"
                    });
                }
            }
            else
            {
                foreach (var item in fs.Items)
                {
                    try
                    {
                        var r = barExecutor(task, barSection!, item);
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

        int passed = rows.Count(r => r.Passed);
        var dataJson = JsonSerializer.Serialize(new
        {
            normCode    = check.NormCode,
            memberTag   = member.Tag,
            totalRows   = rows.Count,
            passedRows  = passed,
            failedRows  = rows.Count - passed,
            rows        = rows.Select(r => new
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

    /// <summary>Совместимость: одиночный набор усилий — делегирует в RunMulti.</summary>
    public static CalcResult Run(
        FemCheck  check,
        FemMember member,
        CrossSection section,
        ForceSet  forceSet,
        Func<CalcTask, CrossSection, LoadItem, CalcResult>? executor = null)
    {
        if (executor == null)
        {
            var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return MakeError(check, created, member.Tag, "Не задан executor");
        }
        return RunMulti(check, member, section, null, [forceSet], executor);
    }

    // ------------------------------------------------------------------ helpers

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
