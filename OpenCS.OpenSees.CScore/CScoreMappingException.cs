namespace OpenCS.OpenSees.CScore;

/// <summary>Ошибка преобразования объекта CScore в нейтральную модель OpenSees.</summary>
public sealed class CScoreMappingException : InvalidOperationException
{
    /// <summary>Создаёт ошибку преобразования с диагностикой источника.</summary>
    public CScoreMappingException(string message)
        : base(message)
    {
    }

    /// <summary>Создаёт ошибку преобразования с исходным исключением.</summary>
    public CScoreMappingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
