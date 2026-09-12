namespace CScore.Sp63.CrackWidth;

/// <summary>Сообщение о применимости или справочной стороне результата СП 63.</summary>
/// <param name="Code">Стабильный код для логики и локализации.</param>
/// <param name="Kind">Назначение сообщения.</param>
/// <param name="NormReference">Пункт нормы или пояснение «справочно».</param>
/// <param name="Text">
/// Ключ локализации, а не готовый пользовательский текст. Причины неприменимости геометрии
/// и раскладки арматуры используют те же ключи "Sp63Normal_*", что и упрощённая проверка
/// нормального сечения (<see cref="CScore.Sp63.Normal.Sp63NormalMessage"/>) — geometрия
/// и раскладка стержней распознаются общим <see cref="CScore.Sp63.Normal.Sp63RebarLayoutAnalyzer"/>,
/// поэтому причины неприменимости у обеих проверок дословно совпадают.
/// </param>
public sealed record Sp63CrackWidthMessage(
    string Code,
    Sp63CrackWidthMessageKind Kind,
    string NormReference,
    string Text);
