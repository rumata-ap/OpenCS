namespace CScore.Sp63.CrackWidth;

/// <summary>Преобразует доменный статус в статус инфраструктуры расчётной задачи.</summary>
public static class Sp63CrackWidthTaskStatusMapper
{
    /// <summary>Возвращает статус, сохраняющий различие применимости и вердикта по acrc.</summary>
    public static string ToCalcResultStatus(Sp63CrackWidthResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status switch
        {
            Sp63CrackWidthStatus.InvalidInput => "error",
            Sp63CrackWidthStatus.NotApplicable => "not_applicable",
            Sp63CrackWidthStatus.Calculated when result.LimitPassed == true => "ok",
            Sp63CrackWidthStatus.Calculated when result.LimitPassed == false => "not_passed",
            _ => "error"
        };
    }
}
