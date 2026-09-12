namespace CScore.Sp63.Normal;

/// <summary>Преобразует доменный статус в статус инфраструктуры расчётной задачи.</summary>
public static class Sp63NormalTaskStatusMapper
{
    /// <summary>Возвращает статус, сохраняющий различие применимости и прочности.</summary>
    public static string ToCalcResultStatus(Sp63NormalResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status switch
        {
            Sp63NormalStatus.InvalidInput => "error",
            Sp63NormalStatus.NotApplicable => "not_applicable",
            Sp63NormalStatus.Calculated when result.StrengthPassed == true => "ok",
            Sp63NormalStatus.Calculated when result.StrengthPassed == false => "not_passed",
            _ => "error"
        };
    }
}
