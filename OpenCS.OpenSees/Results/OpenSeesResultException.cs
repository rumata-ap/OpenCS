namespace OpenCS.OpenSees.Results;

/// <summary>Ошибка схемы или данных результата OpenSees.</summary>
public sealed class OpenSeesResultException : Exception
{
    /// <summary>Код типизированной диагностики.</summary>
    public string Code { get; }

    /// <summary>Создаёт ошибку результата с кодом и сообщением.</summary>
    public OpenSeesResultException(string code, string message)
        : base(message)
    {
        Code = code;
    }
}
