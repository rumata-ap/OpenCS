using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CScore.Fem;

/// <summary>
/// Запускает нормативную проверку конструктивного элемента через CalcTask.
/// Подготавливает задачу из FemMember.DesignParamsJson и перебирает все LoadItem из ForceSet.
/// Не сохраняет результат в БД — это делает вызывающий код (AppViewModel).
/// </summary>
public static class FemCheckRunner
{
    /// <summary>
    /// Вычисляет нормативную проверку. Перебирает LoadItem из ForceSet через переданный executor,
    /// возвращает CalcResult с наихудшей утилизацией.
    /// </summary>
    /// <param name="check">Описание нормативной проверки.</param>
    /// <param name="member">Конструктивный элемент с DesignParamsJson.</param>
    /// <param name="section">Расчётное сечение.</param>
    /// <param name="forceSet">Набор усилий.</param>
    /// <param name="executor">Функция запуска CalcTask → CalcResult. По умолчанию null — нужно передать TaskRunner.Run.</param>
    public static CalcResult Run(
        FemCheck check,
        FemMember member,
        CrossSection section,
        ForceSet forceSet,
        Func<CalcTask, CrossSection, LoadItem, CalcResult>? executor = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (forceSet.Items.Count == 0)
            return MakeError(check, created, "ForceSet не содержит строк усилий");
        if (executor == null)
            return MakeError(check, created, "Не задан executor (TaskRunner.Run)");

        var task = BuildCalcTask(check, member);
        CalcResult? worst = null;
        foreach (var item in forceSet.Items)
        {
            var r = executor(task, section, item);
            if (r.Status == "error") return r;
            worst = PickWorst(worst, r);
        }
        return worst ?? MakeError(check, created, "Нет результатов");
    }

    /// <summary>Подготавливает CalcTask из параметров FemCheck и FemMember.</summary>
    public static CalcTask BuildCalcTask(FemCheck check, FemMember member)
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

    /// <summary>Возвращает CalcResult с наибольшей утилизацией из двух.</summary>
    public static CalcResult PickWorst(CalcResult? a, CalcResult b)
    {
        if (a == null) return b;
        return ExtractUtilization(b.DataJson) > ExtractUtilization(a.DataJson) ? b : a;
    }

    static double ExtractUtilization(string dataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (doc.RootElement.TryGetProperty("utilization", out var u))
                return u.GetDouble();
        }
        catch { }
        return 0;
    }

    static CalcResult MakeError(FemCheck check, string created, string message) => new()
    {
        TaskId   = 0,
        TaskKind = check.NormCode,
        TaskTag  = $"FemCheck#{check.Id}",
        Created  = created,
        Status   = "error",
        DataJson = JsonSerializer.Serialize(new { error = message })
    };
}
