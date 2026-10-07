namespace CScore.Fem;

/// <summary>Распределённая нагрузка, назначенная на конструктивный стержень FEM-схемы.</summary>
public sealed class FemMemberLoad
{
    public int Id { get; set; }
    public int SchemaId { get; set; }
    public int LoadCaseId { get; set; }
    public int MemberId { get; set; }

    /// <summary>Система координат интенсивности: "local" или "global".</summary>
    public string CoordinateSystem { get; set; } = "local";

    /// <summary>Распределение интенсивности: "uniform" или "trapezoidal".</summary>
    public string DistributionType { get; set; } = "uniform";

    /// <summary>Отступ от начала конструктивного стержня I, м.</summary>
    public double StartOffsetM { get; set; }

    /// <summary>Отступ от конца конструктивного стержня J, м.</summary>
    public double EndOffsetM { get; set; }

    /// <summary>Компоненты интенсивности в начале участка, Н/м.</summary>
    public double QxStart { get; set; }
    public double QyStart { get; set; }
    public double QzStart { get; set; }

    /// <summary>Компоненты интенсивности в конце участка, Н/м.</summary>
    public double QxEnd { get; set; }
    public double QyEnd { get; set; }
    public double QzEnd { get; set; }

    /// <summary>Компоненты сосредоточенного момента (только для DistributionType="point"), Н·м.
    /// Применяются только если точка приложения совпадает с узлом расчётной сетки.</summary>
    public double Mx { get; set; }
    public double My { get; set; }
    public double Mz { get; set; }
}
