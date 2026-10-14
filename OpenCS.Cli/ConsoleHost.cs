using System.Collections.ObjectModel;
using System.Diagnostics;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>
/// Журнал консоли: записи копятся (вывод <c>--json</c> берёт их по расчёту) и сразу пишутся в stderr.
/// Вызовы приходят и из пула потоков — запись под замком.
/// </summary>
sealed class ConsoleLog(TextWriter stderr) : ILogService
{
    readonly object _gate = new();

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public void Info(string message) => Add(message, LogLevel.Info);
    public void Warning(string message) => Add(message, LogLevel.Warning);
    public void Error(string message) => Add(message, LogLevel.Error);

    void Add(string message, LogLevel level)
    {
        lock (_gate)
        {
            LogEntries.Add(new LogEntry(message, level, DateTime.Now));
            string prefix = level switch
            {
                LogLevel.Warning => Loc.S("CliLevelWarning") + ": ",
                LogLevel.Error => Loc.S("CliLevelError") + ": ",
                _ => ""
            };
            stderr.WriteLine(prefix + message);
        }
    }

    /// <summary>Число записей — отметка перед расчётом, чтобы потом взять его сообщения.</summary>
    public int Count { get { lock (_gate) return LogEntries.Count; } }

    /// <summary>Записи начиная с отметки <paramref name="from"/>.</summary>
    public IReadOnlyList<LogEntry> Since(int from)
    {
        lock (_gate) return LogEntries.Skip(from).ToList();
    }
}

/// <summary>
/// Уведомления в журнал. На вопрос «Да/Нет» консоль отвечает «Нет» с предупреждением, с <c>--yes</c> — «Да».
/// </summary>
sealed class ConsoleNotifier(ILogService log, bool yes) : IUserNotifier
{
    public void Info(string message, string? title = null) => log.Info(message);
    public void Warning(string message, string? title = null) => log.Warning(message);
    public void Error(string message, string? title = null) => log.Error(message);

    public bool Confirm(string message, string? title = null)
    {
        if (yes)
        {
            log.Info(message);
            return true;
        }
        log.Warning(string.Format(Loc.S("CliConfirmDeclined"), message));
        return false;
    }
}

/// <summary>
/// Занятость консоли: токен расчёта связан с токеном команды (Ctrl+C), прогресс — строкой в stderr
/// не чаще шага 10 % и 0,5 с. Отчёты прогресса приходят из пула потоков.
/// </summary>
sealed class ConsoleBusyHost(TextWriter stderr, bool quiet, CancellationToken commandToken) : IBusyHost
{
    readonly object _gate = new();
    readonly Stopwatch _clock = new();
    CancellationTokenSource? _cts;
    int _busy;
    int _lastPercent;
    string _message = "";

    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    public CancellationTokenSource BeginBusyWithCancellation(string message, bool indeterminate = true)
    {
        lock (_gate)
        {
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(commandToken);
            _message = message;
            _lastPercent = 0;
            _clock.Restart();
            Volatile.Write(ref _busy, 1);
            return _cts;
        }
    }

    public void ReportBusyProgress(double fraction, string? message = null)
    {
        if (quiet || !IsBusy) return;
        int percent = (int)Math.Clamp(fraction * 100, 0, 100);
        lock (_gate)
        {
            if (percent < _lastPercent + 10 || (percent < 100 && _clock.ElapsedMilliseconds < 500)) return;
            _lastPercent = percent;
            _clock.Restart();
            stderr.WriteLine($"  {message ?? _message} {percent}%");
        }
    }

    public void EndBusy(string? message = null) => Volatile.Write(ref _busy, 0);
}
