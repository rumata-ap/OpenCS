using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using OpenCS.Reporting;

namespace OpenCS.Services;

/// <summary>Единственная WPF-реализация движка документирования: печатает HTML в PDF и
/// растеризует SVG в PNG через скрытое окно с WebView2. Один экземпляр на приложение;
/// операции сериализуются, освобождение — асинхронное, без блокировки диспетчера.</summary>
public sealed class WebView2ReportRenderer : IHtmlToPdfConverter, ISvgRasterizer, IAsyncDisposable
{
    /// <summary>Официальный URL Evergreen Bootstrapper — данные для сообщения UI-слоя,
    /// не пользовательская строка рендерера.</summary>
    public const string EvergreenBootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(60);

    readonly string? _userDataFolder;
    readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly CancellationTokenSource _lifetime = new();
    readonly TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource _teardownStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    Window? _host;
    WebView2? _view;
    int _activeOperations;
    int _disposed;
    Task _teardown = Task.CompletedTask;

    /// <summary>Создаёт движок документирования.</summary>
    /// <param name="userDataFolder">Изолированный каталог данных WebView2. В приложении —
    /// <c>null</c> (каталог по умолчанию); в тестах обязателен уникальный временный путь,
    /// иначе блокировки конфликтуют с работающим приложением и параллельными тестами.</param>
    public WebView2ReportRenderer(string? userDataFolder = null) => _userDataFolder = userDataFolder;

    /// <inheritdoc/>
    public async Task ConvertAsync(string html, string outputPdfPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPdfPath);

        await RunAsync(async (view, token) =>
        {
            string tempHtml = Path.Combine(Path.GetTempPath(), $"opencs-report-{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(tempHtml, html, new UTF8Encoding(false), token).ConfigureAwait(true);
            try
            {
                await NavigateAsync(view, () => view.CoreWebView2.Navigate(new Uri(tempHtml).AbsoluteUri), token)
                    .ConfigureAwait(true);

                var settings = view.CoreWebView2.Environment.CreatePrintSettings();
                settings.Orientation = CoreWebView2PrintOrientation.Portrait;
                settings.MediaSize = CoreWebView2PrintMediaSize.Custom;
                settings.PageWidth = 210.0 / 25.4;
                settings.PageHeight = 297.0 / 25.4;
                settings.MarginTop = settings.MarginBottom = 0;
                settings.MarginLeft = settings.MarginRight = 0;
                settings.ScaleFactor = 1.0;
                settings.ShouldPrintBackgrounds = true;
                settings.ShouldPrintHeaderAndFooter = false;

                bool printed = await AwaitUnderlyingAsync(
                    view.CoreWebView2.PrintToPdfAsync(outputPdfPath, settings), token).ConfigureAwait(true);
                if (!printed)
                    throw new ReportRenderingUnavailableException(
                        ReportRenderingFailureReason.RenderFailed,
                        $"PrintToPdfAsync вернул false для '{outputPdfPath}'.");
                return true;
            }
            finally
            {
                try { File.Delete(tempHtml); } catch { /* временный файл */ }
            }
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<byte[]> RasterizeAsync(string svg, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svg);
        var size = SvgSizing.Resolve(svg);
        string normalized = SvgSizing.EnsureExplicitDimensions(svg);

        return RunAsync(async (view, token) =>
        {
            var dpi = VisualTreeHelper.GetDpi(_host!);
            _host!.Width = size.Width / dpi.DpiScaleX;
            _host.Height = size.Height / dpi.DpiScaleY;
            _host.UpdateLayout();

            await NavigateAsync(view,
                () => view.NavigateToString($"<html><body style=\"margin:0\">{normalized}</body></html>"),
                token).ConfigureAwait(true);

            using var buffer = new MemoryStream();
            await AwaitUnderlyingAsync(
                view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, buffer),
                token).ConfigureAwait(true);
            return buffer.ToArray();
        }, ct);
    }

    // Общая обвязка: учёт активных операций, gate, linked CTS и классификация отмены.
    async Task<T> RunAsync<T>(Func<WebView2, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        EnterOperation();
        using var timeout = new CancellationTokenSource(OperationTimeout);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetime.Token, ct, timeout.Token);
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                return await _dispatcher.InvokeAsync(async () =>
                {
                    var view = await EnsureViewAsync().ConfigureAwait(true);
                    return await work(view, linked.Token).ConfigureAwait(true);
                }).Task.Unwrap().ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException ex) when (!_lifetime.IsCancellationRequested
                                                    && !ct.IsCancellationRequested
                                                    && timeout.IsCancellationRequested)
        {
            throw new ReportRenderingUnavailableException(
                ReportRenderingFailureReason.TimedOut,
                $"Операция рендеринга не уложилась в {OperationTimeout.TotalSeconds:F0} с.", inner: ex);
        }
        finally { LeaveOperation(); }
    }

    // Создаётся ровно один раз: настоящее окно с реальным HWND — Visibility.Hidden без Show()
    // не гарантирует создание HwndSource, необходимого WebView2.
    async Task<WebView2> EnsureViewAsync()
    {
        if (_view != null) return _view;

        var view = new WebView2();
        var host = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -10000,
            Top = -10000,
            Width = 1,
            Height = 1,
            Content = view
        };
        host.Show();
        try
        {
            var environment = _userDataFolder == null
                ? null
                : await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder)
                    .ConfigureAwait(true);
            await view.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            host.Close();
            throw new ReportRenderingUnavailableException(
                ReportRenderingFailureReason.RuntimeMissing,
                "WebView2 Runtime не найден при инициализации CoreWebView2.",
                EvergreenBootstrapperUrl, ex);
        }
        catch (Exception ex)
        {
            host.Close();
            throw new ReportRenderingUnavailableException(
                ReportRenderingFailureReason.RenderFailed,
                "Не удалось инициализировать CoreWebView2.", inner: ex);
        }

        _host = host;
        _view = view;
        return view;
    }

    static async Task NavigateAsync(WebView2 view, Action navigate, CancellationToken ct)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            => completed.TrySetResult(e.IsSuccess);

        view.NavigationCompleted += OnCompleted;
        try
        {
            navigate();
            await using var registration = ct.Register(() => completed.TrySetCanceled(ct)).ConfigureAwait(true);
            if (!await completed.Task.ConfigureAwait(true))
                throw new ReportRenderingUnavailableException(
                    ReportRenderingFailureReason.NavigationFailed,
                    "NavigationCompleted вернул IsSuccess = false.");
        }
        finally { view.NavigationCompleted -= OnCompleted; }
    }

    // PrintToPdfAsync/CapturePreviewAsync своего токена не имеют: отмену сигнализирует
    // ожидание, но underlying task обязательно дожидается до возврата — иначе WebView2
    // продолжил бы писать в файл, который вызывающий уже считает свободным.
    static async Task<T> AwaitUnderlyingAsync<T>(Task<T> operation, CancellationToken ct)
    {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = ct.Register(() => cancelled.TrySetResult(true)).ConfigureAwait(true);

        if (await Task.WhenAny(operation, cancelled.Task).ConfigureAwait(true) != (Task)operation)
        {
            try { await operation.ConfigureAwait(true); }
            catch { /* дожидаемся завершения, результат уже не нужен */ }
            ct.ThrowIfCancellationRequested();
        }
        return await operation.ConfigureAwait(true);
    }

    // CapturePreviewAsync возвращает не-generic Task — та же логика ожидания.
    static async Task AwaitUnderlyingAsync(Task operation, CancellationToken ct)
    {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = ct.Register(() => cancelled.TrySetResult(true)).ConfigureAwait(true);

        if (await Task.WhenAny(operation, cancelled.Task).ConfigureAwait(true) != operation)
        {
            try { await operation.ConfigureAwait(true); }
            catch { /* дожидаемся завершения, результат уже не нужен */ }
            ct.ThrowIfCancellationRequested();
        }
        await operation.ConfigureAwait(true);
    }

    void EnterOperation()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Interlocked.Increment(ref _activeOperations);
        if (Volatile.Read(ref _disposed) == 0) return;
        LeaveOperation();
        throw new ObjectDisposedException(nameof(WebView2ReportRenderer));
    }

    void LeaveOperation()
    {
        if (Interlocked.Decrement(ref _activeOperations) == 0 && Volatile.Read(ref _disposed) != 0)
            _idle.TrySetResult();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _lifetime.Cancel();
            if (Volatile.Read(ref _activeOperations) == 0) _idle.TrySetResult();
            _teardown = TeardownAsync();
            _teardownStarted.TrySetResult();
        }
        return new ValueTask(WaitTeardownAsync());
    }

    async Task WaitTeardownAsync()
    {
        await _teardownStarted.Task.ConfigureAwait(false);
        await _teardown.ConfigureAwait(false);
    }

    async Task TeardownAsync()
    {
        // 1-3: дождаться, пока все начатые и стоящие в очереди операции выйдут.
        await _idle.Task.ConfigureAwait(false);
        // 4: убедиться, что control свободен.
        try { await _gate.WaitAsync().ConfigureAwait(false); _gate.Release(); }
        catch (ObjectDisposedException) { /* gate уже освобождён */ }

        // 5-6: освобождение control и окна строго в UI-потоке.
        await _dispatcher.InvokeAsync(() =>
        {
            try { _view?.Dispose(); } catch { /* teardown: ошибка не должна мешать закрытию */ }
            try { _host?.Close(); } catch { /* teardown: ошибка не должна мешать закрытию */ }
            _view = null;
            _host = null;
        }).Task.ConfigureAwait(false);

        // 7.
        _gate.Dispose();
        _lifetime.Dispose();
    }
}
