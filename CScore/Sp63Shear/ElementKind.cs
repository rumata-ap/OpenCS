namespace CScore.Sp63Shear;

/// <summary>Тип элемента для выбора режима коэффициента φn по п. 8.1.34.</summary>
public enum ElementKind
{
    /// <summary>Изгибаемый элемент без предварительного напряжения: φn = 1.</summary>
    BendingUnstressed,

    /// <summary>Остальные элементы: φn вычисляется по продольной силе.</summary>
    Other
}
