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
public sealed record Sp63CrackWidthOptions(
    Sp63NormalShapeKind ShapeKind,
    Sp63NormalAxis Axis,
    double Phi1,
    double Phi2,
    double AcrcLimMm);
