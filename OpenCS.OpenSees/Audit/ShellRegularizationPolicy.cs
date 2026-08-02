namespace OpenCS.OpenSees.Audit;

/// <summary>Режим regularization. Наличие enum или поля само по себе не означает
/// фактическое применение regularization без native adapter.</summary>
public enum ShellRegularizationMode
{
    /// <summary>Regularization не применяется.</summary>
    None,

    /// <summary>В native material передаётся характеристическая длина элемента.</summary>
    ElementCharacteristicLength,

    /// <summary>Применяется метод полосы растрескивания.</summary>
    CrackBand,

    /// <summary>Применяется энергия разрушения.</summary>
    FractureEnergy
}

/// <summary>Метод вычисления характеристической длины shell-элемента.</summary>
public enum ShellCharacteristicLengthMethod
{
    /// <summary>Квадратный корень из площади элемента.</summary>
    SqrtArea
}

/// <summary>Политика regularization audit-расчёта.</summary>
public sealed record ShellRegularizationPolicy
{
    /// <summary>Запрошенный режим regularization.</summary>
    public ShellRegularizationMode Mode { get; init; } = ShellRegularizationMode.None;

    /// <summary>Метод вычисления характеристической длины.</summary>
    public ShellCharacteristicLengthMethod Method { get; init; } = ShellCharacteristicLengthMethod.SqrtArea;
}
