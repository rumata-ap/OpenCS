namespace OpenCS.Services;

/// <summary>
/// Занятость приложения на время расчёта: сообщение, прогресс, отмена.
/// Отчёты прогресса приходят через <see cref="Progress{T}"/>: в WPF — в UI-потоке, в консоли — из пула потоков,
/// поэтому консольная реализация обязана быть потокобезопасной.
/// </summary>
public interface IBusyHost
{
    bool IsBusy { get; }
    CancellationTokenSource BeginBusyWithCancellation(string message, bool indeterminate = true);
    void ReportBusyProgress(double fraction, string? message = null);
    void EndBusy(string? message = null);
}
