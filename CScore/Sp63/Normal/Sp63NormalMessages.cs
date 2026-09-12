namespace CScore.Sp63.Normal;

/// <summary>Сообщение о применимости или справочной стороне результата СП 63.</summary>
/// <param name="Code">Стабильный код для логики и локализации.</param>
/// <param name="Kind">Назначение сообщения.</param>
/// <param name="NormReference">Пункт нормы или пояснение «справочно».</param>
/// <param name="Text">Ключ локализации, а не готовый пользовательский текст.</param>
public sealed record Sp63NormalMessage(
    string Code,
    Sp63NormalMessageKind Kind,
    string NormReference,
    string Text);
