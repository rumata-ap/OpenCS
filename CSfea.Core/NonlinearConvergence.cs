namespace CSfea.Core;

/// <summary>
/// Сводка сходимости шаговых нелинейных решателей: все ли шаги нагружения сошлись и
/// какой шаг первым не сошёлся. Решатели не прерывают расчёт на несошедшемся шаге
/// (сигнатуры сохранены), поэтому вызывающий код обязан проверить историю.
/// </summary>
public static class NonlinearConvergence
{
    /// <summary>Номер первого несошедшегося шага (с 1) по истории Ньютона оболочек; null — все сошлись.</summary>
    public static int? FirstFailedStep(this IReadOnlyList<ShellMesh.NewtonRecord> history)
    {
        int step = 0;
        bool stepConverged = true;
        foreach (var r in history)
        {
            if (r.Step != step)
            {
                if (step > 0 && !stepConverged) return step;
                step = r.Step;
                stepConverged = false;
            }
            if (r.Converged) stepConverged = true;
        }
        return step > 0 && !stepConverged ? step : null;
    }

    /// <summary>Все шаги истории Ньютона оболочек сошлись.</summary>
    public static bool AllConverged(this IReadOnlyList<ShellMesh.NewtonRecord> history)
        => history.FirstFailedStep() == null;

    /// <summary>Номер первого несошедшегося шага (с 1) по записям рамы; null — все сошлись.</summary>
    public static int? FirstFailedStep(this IReadOnlyList<NonlinearStepRecord> records)
    {
        for (int i = 0; i < records.Count; i++)
            if (!records[i].Converged) return i + 1;
        return null;
    }

    /// <summary>Все шаги рамы сошлись.</summary>
    public static bool AllConverged(this IReadOnlyList<NonlinearStepRecord> records)
        => records.FirstFailedStep() == null;
}
