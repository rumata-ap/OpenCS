using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OpenCS.Reporting;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки production-рендерера: печать PDF, растеризация PNG, сериализация
/// операций, отмена/таймаут и асинхронное освобождение без блокировки диспетчера.</summary>
[Collection("WebView2")]
public sealed class WebView2ReportRendererTests : IDisposable
{
    readonly List<string> _userDataFolders = [];

    // Каждый экземпляр рендерера получает свой каталог данных WebView2: общий каталог
    // конфликтует блокировками с работающим приложением и с параллельными тестами.
    WebView2ReportRenderer NewRenderer()
    {
        string folder = Path.Combine(Path.GetTempPath(), "opencs-wv2-test-" + Guid.NewGuid().ToString("N"));
        _userDataFolders.Add(folder);
        // Диспетчер задаётся явно: другой тест мог создать Application во временном
        // STA-потоке, и Application.Current.Dispatcher указывал бы на мёртвый поток.
        return new WebView2ReportRenderer(folder, Dispatcher.CurrentDispatcher);
    }

    public void Dispose()
    {
        foreach (var folder in _userDataFolders)
            try { Directory.Delete(folder, true); } catch { }
    }

    const string SampleSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 120 80\">"
        + "<rect width=\"120\" height=\"80\" fill=\"#1769aa\"/></svg>";
    const string SampleHtml = "<!doctype html><html><body><h1>Отчёт</h1></body></html>";

    // Прогоняет тело теста на STA-потоке с живым Dispatcher-циклом.
    static void RunOnDispatcher(Func<Task> body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Application не создаётся: он один на AppDomain и привязан к потоку
                // создания, поэтому второй тест в новом STA-потоке падал бы с
                // «Нельзя создать более одного экземпляра System.Windows.Application».
                var pump = Dispatcher.CurrentDispatcher;
                // Без явного контекста FromCurrentSynchronizationContext() бросает
                // InvalidOperationException: на голой STA-нити SynchronizationContext пуст.
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(pump));
                _ = body().ContinueWith(t =>
                {
                    if (t.IsFaulted) error = t.Exception?.GetBaseException();
                    pump.InvokeShutdown();
                }, TaskScheduler.FromCurrentSynchronizationContext());
                Dispatcher.Run();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // Join с ограничением: если Dispatcher.Run не завершился (WebView2 не отдал
        // событие, окно не закрылось), тест падает по таймауту, а не подвешивает весь
        // прогон — зависший STA-поток фоновый, процесс тестов завершится штатно.
        if (!thread.Join(TimeSpan.FromMinutes(2)))
            throw new TimeoutException(
                "Офскрин-прогон WebView2 не завершился за 2 минуты — поток остался в Dispatcher.Run.");

        Skip.If(error is Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException
                || (error as ReportRenderingUnavailableException)?.Reason
                    == ReportRenderingFailureReason.RuntimeMissing,
            "WebView2 Runtime не установлен на этой машине.");
        if (error != null) throw error;
    }

    [SkippableFact]
    public void ConvertAsync_ProducesA4Pdf()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-test-{Guid.NewGuid():N}.pdf");
        RunOnDispatcher(async () =>
        {
            await using var renderer = NewRenderer();
            await renderer.ConvertAsync(SampleHtml, path);

            byte[] bytes = await File.ReadAllBytesAsync(path);
            Assert.True(bytes.Length > 1000);
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));

            string text = System.Text.Encoding.Latin1.GetString(bytes);
            var box = System.Text.RegularExpressions.Regex.Match(text,
                @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]",
                System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(2));
            Assert.True(box.Success, "В PDF не найден /MediaBox.");
            Assert.Equal(595.0, double.Parse(box.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture), 0);
            Assert.Equal(842.0, double.Parse(box.Groups[2].Value,
                System.Globalization.CultureInfo.InvariantCulture), 0);
        });
        File.Delete(path);
    }

    [SkippableFact]
    public void RasterizeAsync_ProducesPngOfRequestedSize()
        => RunOnDispatcher(async () =>
        {
            await using var renderer = NewRenderer();
            byte[] png = await renderer.RasterizeAsync(SvgSizing.EnsureExplicitDimensions(SampleSvg));

            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
            int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            Assert.Equal(120, width);
            Assert.Equal(80, height);

            // Сигнальный цвет заливки #1769aa — доказывает, что содержимое отрисовано.
            var pixel = SamplePixel(png, width / 2, height / 2);
            Assert.Equal((0x17, 0x69, 0xaa), pixel);
        });

    [SkippableFact]
    public void RasterizeAsync_CanBeCalledTwiceOnSameInstance()
        => RunOnDispatcher(async () =>
        {
            await using var renderer = NewRenderer();
            byte[] first = await renderer.RasterizeAsync(SvgSizing.EnsureExplicitDimensions(SampleSvg));
            byte[] second = await renderer.RasterizeAsync(SvgSizing.EnsureExplicitDimensions(SampleSvg));
            Assert.Equal(first.Length, second.Length);
        });

    [SkippableFact]
    public void ConcurrentCalls_AreSerialized()
        => RunOnDispatcher(async () =>
        {
            await using var renderer = NewRenderer();
            string svg = SvgSizing.EnsureExplicitDimensions(SampleSvg);
            var results = await Task.WhenAll(renderer.RasterizeAsync(svg), renderer.RasterizeAsync(svg));

            Assert.All(results, png => Assert.True(png.Length > 0));
        });

    [SkippableFact]
    public void CancelledCallerToken_ThrowsOperationCanceled_NotTimedOut()
        => RunOnDispatcher(async () =>
        {
            await using var renderer = NewRenderer();
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => renderer.RasterizeAsync(SampleSvg, cts.Token));
            Assert.IsNotType<ReportRenderingUnavailableException>(ex);
        });

    [SkippableFact]
    public void DisposeAsync_IsIdempotentAndReleasesRenderer()
        => RunOnDispatcher(async () =>
        {
            var renderer = NewRenderer();
            await renderer.RasterizeAsync(SvgSizing.EnsureExplicitDimensions(SampleSvg));

            await Task.WhenAll(renderer.DisposeAsync().AsTask(), renderer.DisposeAsync().AsTask());

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => renderer.RasterizeAsync(SampleSvg));
        });

    static (byte R, byte G, byte B) SamplePixel(byte[] png, int x, int y)
    {
        using var stream = new MemoryStream(png);
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var buffer = new byte[4];
        frame.CopyPixels(new Int32Rect(x, y, 1, 1), buffer, 4, 0);
        return (buffer[2], buffer[1], buffer[0]);
    }
}
