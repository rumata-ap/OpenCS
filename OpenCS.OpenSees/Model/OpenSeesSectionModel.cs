namespace OpenCS.OpenSees.Model;

/// <summary>Нейтральная fiber-секция для передачи в OpenSees.</summary>
public sealed class OpenSeesSectionModel
{
    /// <summary>Материалы, на которые ссылаются волокна.</summary>
    public IReadOnlyList<OpenSeesMaterialDefinition> Materials { get; init; } = [];

    /// <summary>Подготовленные волокна в порядке, заданном адаптером.</summary>
    public IReadOnlyList<OpenSeesFiber> Fibers { get; init; } = [];

    /// <summary>Крутильная жёсткость секции GJ в Н·м².</summary>
    public double GJ { get; init; }

    /// <summary>Соглашение координат и компонент сил.</summary>
    public OpenSeesCoordinateConvention Convention { get; init; } =
        OpenSeesCoordinateConvention.CScoreDefault;

    /// <summary>Проверяет целостность нейтральной модели.</summary>
    public void Validate() => OpenSeesSectionModelValidator.Validate(this);
}
