namespace CScore.Sp63.Deflection;

/// <summary>Статус формульного расчёта прогиба.</summary>
public enum Sp63DeflectionStatus { InvalidInput, NotApplicable, Calculated }
/// <summary>Назначение сообщения результата.</summary>
public enum Sp63DeflectionMessageKind { Applicability, Information, Warning }
/// <summary>Сообщение формульного расчёта прогиба.</summary>
public sealed record Sp63DeflectionMessage(string Code, Sp63DeflectionMessageKind Kind,
    string NormReference, string Text);

/// <summary>Результат расчёта кривизны и прогиба по формулам СП 63.</summary>
public sealed class Sp63DeflectionResult
{
    /// <summary>Статус применимости и выполнения расчёта.</summary>
    public Sp63DeflectionStatus Status { get; set; }
    /// <summary>Полная кривизна по пп. 8.2.23–8.2.30.</summary>
    public CScore.Sp63.CrackWidth.Sp63CurvatureResult? Curvature { get; set; }
    /// <summary>Выбранная статическая схема.</summary>
    public Sp63DeflectionStaticScheme Scheme { get; set; }
    /// <summary>Коэффициент схемы S.</summary>
    public double CoefficientS { get; set; }
    /// <summary>Пролёт, м.</summary>
    public double SpanM { get; set; }
    /// <summary>Расчётный прогиб, мм.</summary>
    public double DeflectionMm { get; set; }
    /// <summary>Предельный прогиб, мм.</summary>
    public double DeflectionLimitMm { get; set; }
    /// <summary>Коэффициент использования предела.</summary>
    public double Utilization { get; set; }
    /// <summary>Вердикт f ≤ fult; null, если прогиб не вычислен.</summary>
    public bool? DeflectionPassed { get; set; }
    /// <summary>Наличие трещин от полной нагрузки.</summary>
    public bool? Cracked { get; set; }
    /// <summary>Ветвь расчёта кривизны.</summary>
    public string Branch { get; set; } = "";
    /// <summary>Причины неприменимости.</summary>
    public List<Sp63DeflectionMessage> ApplicabilityMessages { get; set; } = [];
    /// <summary>Справочные сообщения.</summary>
    public List<Sp63DeflectionMessage> InformationalMessages { get; set; } = [];
    /// <summary>Переменные с исходными подписанными усилиями и результатами.</summary>
    public Dictionary<string, double> Variables { get; set; } = [];
}
