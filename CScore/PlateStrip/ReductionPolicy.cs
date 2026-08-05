namespace CScore.PlateStrip;

/// <summary>Политика редукции плитного отклика в стержневое сечение.</summary>
public enum ReductionPolicy
{
    /// <summary>Упрощённая одноосная диагностическая оценка.</summary>
    DirectUniaxial,

    /// <summary>Интегрирование конститутивного отклика по ширине полосы.</summary>
    ConstitutiveIntegration
}
