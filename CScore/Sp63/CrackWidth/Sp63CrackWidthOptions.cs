using CScore.Sp63.Normal;

namespace CScore.Sp63.CrackWidth;

/// <summary>Статус результата упрощённой проверки раскрытия трещин.</summary>
public enum Sp63CrackWidthStatus
{
    /// <summary>Входные параметры противоречат поддержанному контракту.</summary>
    InvalidInput,
    /// <summary>Нормативный формульный режим недоступен для этих данных.</summary>
    NotApplicable,
    /// <summary>Расчёт выполнен и содержит вердикт по ширине раскрытия трещин.</summary>
    Calculated
}

/// <summary>Назначение сообщения результата.</summary>
public enum Sp63CrackWidthMessageKind
{
    /// <summary>Причина, по которой формульная проверка неприменима.</summary>
    Applicability,
    /// <summary>Справочная информация без отрицательного вердикта.</summary>
    Information,
    /// <summary>Предупреждение о принятом ограничении или исходных данных.</summary>
    Warning
}

/// <summary>
/// Типизированные настройки упрощённой (не деформационно-модельной) проверки ширины раскрытия
/// трещин нормальных сечений по п. 8.2.9-8.2.16 СП 63 — стержневой аналог уже реализованной
/// плитной проверки <see cref="CScore.ShellSimplSolver"/>.
/// </summary>
/// <param name="ShapeKind">Тип поддерживаемой формы (пока только прямоугольник).</param>
/// <param name="Axis">Ось изгиба.</param>
/// <param name="Phi1">Коэффициент длительности действия нагрузки, п. 8.2.10.</param>
/// <param name="Phi2">Коэффициент профиля продольной арматуры, п. 8.2.10.</param>
/// <param name="AcrcLimMm">Предельно допустимая ширина раскрытия трещин, мм.</param>
/// <param name="SigmaSCrc">Способ получения σs,crc в ψs, п. 8.2.18.</param>
/// <param name="WplGamma">Источник коэффициента пластичности γ в Wpl = γ·Wred.</param>
/// <param name="Mode">Режим проверки: одна составляющая с заданным φ1 или полная по п. 8.2.7.</param>
/// <param name="LongTermShare">
/// Доля постоянных и временных длительных нагрузок ψ = Ml/M (0…1); применяется к N и M.
/// Используется только в режиме <see cref="Sp63CrackWidthMode.LongAndShort"/>.
/// </param>
/// <param name="AcrcLimShortMm">
/// Предельная ширина непродолжительного раскрытия, мм (режим
/// <see cref="Sp63CrackWidthMode.LongAndShort"/>; продолжительное — <paramref name="AcrcLimMm"/>).
/// </param>
public sealed record Sp63CrackWidthOptions(
    Sp63NormalShapeKind ShapeKind,
    Sp63NormalAxis Axis,
    double Phi1,
    double Phi2,
    double AcrcLimMm,
    SigmaSCrcMethod SigmaSCrc = SigmaSCrcMethod.ReleasedConcrete8137,
    WplGammaMethod WplGamma = WplGammaMethod.Sp63,
    Sp63CrackWidthMode Mode = Sp63CrackWidthMode.SingleTerm,
    double LongTermShare = 1.0,
    double AcrcLimShortMm = 0.4);

/// <summary>Режим упрощённой проверки ширины раскрытия трещин.</summary>
public enum Sp63CrackWidthMode
{
    /// <summary>Одна составляющая acrc,i по п. 8.2.15 с заданным пользователем φ1.</summary>
    SingleTerm,
    /// <summary>
    /// Продолжительное acrc = acrc1 (8.119) и непродолжительное acrc = acrc1 + acrc2 − acrc3
    /// (8.120) раскрытие по п. 8.2.7, каждое со своим предельным значением по п. 8.2.6.
    /// </summary>
    LongAndShort
}
