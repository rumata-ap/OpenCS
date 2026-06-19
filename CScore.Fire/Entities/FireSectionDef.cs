namespace CScore.Fire.Entities;

/// <summary>
/// Огневое сечение — конфигурация теплотехнического расчёта по СП 468.1325800.
/// </summary>
/// <remarks>
/// Привязано к существующему поперечному сечению (<see cref="SectionId"/>) и задаёт
/// длительность пожара, огневую кривую, шаги сетки и времени, а также граничные
/// условия по рёбрам контура и отверстий.
/// </remarks>
public class FireSectionDef
{
    /// <summary>Идентификатор записи (SQLite auto-increment).</summary>
    public int Id { get; set; }

    /// <summary>Порядковый номер для сортировки в дереве проекта.</summary>
    public int Num { get; set; }

    /// <summary>Метка / наименование огневого расчёта.</summary>
    public string Tag { get; set; } = "";

    /// <summary>Идентификатор поперечного сечения (<see cref="CScore.CrossSection"/>).</summary>
    public int SectionId { get; set; }

    /// <summary>Длительность расчёта пожара, мин.</summary>
    public double FireDurationMin { get; set; } = 60.0;

    /// <summary>Огневая кривая: <c>iso834</c>, <c>hydrocarbon</c> или <c>slow</c>.</summary>
    public string FireCurve { get; set; } = "iso834";

    /// <summary>Шаг триангуляции / размер элемента сетки, м.</summary>
    public double MeshStepM { get; set; } = 0.01;

    /// <summary>Шаг интегрирования по времени, с.</summary>
    public double TimeStepS { get; set; } = 5.0;

    /// <summary>Параметр θ схемы θ-метода (0 = явная, 1 = неявная).</summary>
    public double Theta { get; set; } = 1.0;

    /// <summary>Допуск сходимости итераций Пикара по температуре, °C.</summary>
    public double PicardTolCelsius { get; set; } = 0.5;

    /// <summary>Максимальное число итераций Пикара на шаге.</summary>
    public int PicardMaxIter { get; set; } = 20;

    /// <summary>Интервал сохранения снимков температурного поля, мин.</summary>
    public double SnapshotStepMin { get; set; } = 5.0;

    /// <summary>
    /// Пресет граничных условий внешнего контура:
    /// <c>1-sided</c> … <c>all-sided</c> или <c>manual</c>.
    /// </summary>
    public string BcPreset { get; set; } = "manual";

    /// <summary>Пресет граничных условий отверстий: <c>ambient</c> или <c>adiabatic</c>.</summary>
    public string HoleBcPreset { get; set; } = "ambient";

    /// <summary>Алгоритм триангуляции: <c>advancing_front</c> или <c>ruppert</c>.</summary>
    public string Algorithm { get; set; } = "advancing_front";

    /// <summary>Число итераций сглаживания триангуляции.</summary>
    public int SmoothIterTri { get; set; } = 5;

    /// <summary>Тип конечного элемента сетки: <c>linear</c> (T3) или <c>quadratic</c> (T6).</summary>
    public string MeshElementType { get; set; } = "linear";

    /// <summary>Граничные условия по рёбрам контура и отверстий.</summary>
    public List<FireBoundaryEdgeDef> Edges { get; set; } = [];

    /// <summary>
    /// Тип заполнителя бетона: <c>silicate</c>, <c>carbonate</c>, <c>lightweight</c>.
    /// Пустая строка — наследовать из материала связанного сечения.
    /// </summary>
    public string AggregateType { get; set; } = "";
}
