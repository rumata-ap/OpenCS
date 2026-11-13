namespace OpenCS.ViewModels;

/// <summary>Строка окна пакета с причиной — для общего фильтра, сортировки и подсветки.</summary>
public interface IBatchVerdictRow
{
    /// <summary>Причина строки.</summary>
    BatchVerdictView Reason { get; }
}
