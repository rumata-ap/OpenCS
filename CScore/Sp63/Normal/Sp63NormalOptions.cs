namespace CScore.Sp63.Normal;

/// <summary>Поддерживаемый тип формы нормального сечения.</summary>
public enum Sp63NormalShapeKind
{
    /// <summary>Сплошное осевое прямоугольное сечение.</summary>
    Rectangular
}

/// <summary>Ось изгиба одноосной проверки.</summary>
public enum Sp63NormalAxis
{
    /// <summary>Изгиб с моментом Mx; координата высоты — Y.</summary>
    Mx,
    /// <summary>Изгиб с моментом My; координата высоты — X.</summary>
    My
}

/// <summary>Схема статической определимости элемента.</summary>
public enum Sp63StructuralScheme
{
    /// <summary>Статически определимый элемент: эксцентриситеты складываются.</summary>
    StaticallyDeterminate,
    /// <summary>Статически неопределимый элемент: выбирается максимум.</summary>
    StaticallyIndeterminate
}

/// <summary>Режим учёта устойчивости элемента.</summary>
public enum Sp63NormalStabilityMode
{
    /// <summary>Учитывать гибкость элемента и коэффициент η.</summary>
    Member,
    /// <summary>Явно ограничить проверку плоскостью сечения без η.</summary>
    SectionOnlyExplicit
}

/// <summary>Статус результата упрощённой проверки нормального сечения.</summary>
public enum Sp63NormalStatus
{
    /// <summary>Входные параметры противоречат поддержанному контракту.</summary>
    InvalidInput,
    /// <summary>Нормативный формульный режим недоступен для этих данных.</summary>
    NotApplicable,
    /// <summary>Расчёт выполнен и содержит вердикт прочности.</summary>
    Calculated
}

/// <summary>Назначение сообщения результата.</summary>
public enum Sp63NormalMessageKind
{
    /// <summary>Причина, по которой формульная проверка неприменима.</summary>
    Applicability,
    /// <summary>Справочная информация без отрицательного вердикта.</summary>
    Information,
    /// <summary>Предупреждение о принятом ограничении или исходных данных.</summary>
    Warning
}

/// <summary>Типизированные настройки формульной проверки нормального сечения.</summary>
/// <param name="ShapeKind">Тип поддерживаемой формы.</param>
/// <param name="Axis">Ось изгиба.</param>
/// <param name="MemberContext">Данные элемента для п. 8.1.7 и 8.1.15.</param>
public sealed record Sp63NormalOptions(
    Sp63NormalShapeKind ShapeKind,
    Sp63NormalAxis Axis,
    Sp63MemberContext MemberContext);
