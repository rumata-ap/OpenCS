namespace OpenCS.OpenSees.Structural;

/// <summary>Единая на модель политика drilling-стабилизации shell-элементов. Асимметрия Q4/T3
/// (Q4: числовой -drillingStab ИЛИ булев -drillingNL; T3: только -drillingNL) валидируется
/// отдельно в ShellOpenSeesModel.Validate(), т.к. требует знания состава Elements.</summary>
public sealed record DrillingPolicy
{
    public ShellDrillingMode Mode { get; init; } = ShellDrillingMode.None;
    public double? StabilizationValue { get; init; }

    public void Validate()
    {
        if (Mode == ShellDrillingMode.Stabilization)
        {
            if (StabilizationValue is null or <= 0)
                throw new InvalidOperationException(
                    "Для режима Stabilization требуется положительное StabilizationValue.");
        }
        else if (StabilizationValue is not null)
        {
            throw new InvalidOperationException(
                "StabilizationValue задан, но режим DrillingPolicy не Stabilization — " +
                "вероятная ошибка конфигурации.");
        }
    }
}
