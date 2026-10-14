namespace OpenCS.Services;

/// <summary>
/// Сообщения пользователю из исполнителей расчётов вместо <c>MessageBox</c>.
/// Заголовок по умолчанию — «Информация», «WARNING», «Ошибка» (ключи Info, Warning, Error).
/// </summary>
public interface IUserNotifier
{
    void Info(string message, string? title = null);
    void Warning(string message, string? title = null);
    void Error(string message, string? title = null);

    /// <summary>Вопрос «Да/Нет» со значком предупреждения; true — «Да».</summary>
    bool Confirm(string message, string? title = null);
}
