namespace CScore.Fire;

/// <summary>Расчётная ошибка огневого модуля со стабильным ключом локализации.</summary>
public sealed class FireCalculationException : InvalidOperationException
{
   /// <summary>Ключ сообщения в словарях локализации.</summary>
   public string ErrorKey { get; }

   /// <summary>Создать расчётную ошибку.</summary>
   public FireCalculationException(string errorKey)
      : base(errorKey)
   {
      ErrorKey = errorKey;
   }
}
