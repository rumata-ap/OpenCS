using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CScore;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using OpenCS.Reporting;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>POC-gate: скрытое WPF-окно с WebView2 растеризует реальную карту НДС в PNG
/// и печатает её же в PDF. Доказывает, что офскрин-путь пригоден для обеих веток
/// (DOCX-картинки и PDF) до реализации production-рендерера.</summary>
[Collection("WebView2")]
public sealed class WebView2PocTests
{
    // Боковая панель карты (DrawSidebar) заливается #f8fafc на всю высоту и начинается
    // с x = 900 - 198 = 702 в координатах viewBox. Пиксель внутри неё гарантированно
    // не белый независимо от того, что содержит само сечение, — это и есть сигнал
    // «SVG реально отрисован», а не «PNG правильного размера и пустой».
    const int SidebarProbeX = 800;

    [SkippableFact]
    public void CapturePreview_RendersSectionStateMap()
    {
        string svg = RenderMapSvg();
        var expected = SvgSizing.Resolve(svg);

        byte[]? png = null;

        var (scaleX, scaleY) = RunOnHiddenView(async (view, host) =>
        {
            var dpi = VisualTreeHelper.GetDpi(host);
            host.Width = expected.Width / dpi.DpiScaleX;
            host.Height = expected.Height / dpi.DpiScaleY;
            host.UpdateLayout();

            await NavigateAsync(view, $"<html><body style=\"margin:0\">{svg}</body></html>");

            using var buffer = new MemoryStream();
            await view.CoreWebView2.CapturePreviewAsync(
                CoreWebView2CapturePreviewImageFormat.Png, buffer);
            png = buffer.ToArray();
        });

        Assert.NotNull(png);
        Assert.True(png!.Length > 8);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
        var (pngWidth, pngHeight) = ReadIhdr(png);

        // Ожидание: физические пиксели = DIP * DPI-масштаб, окно выставлено ровно под это.
        Assert.Equal((int)Math.Round(expected.Width), pngWidth);
        Assert.Equal((int)Math.Round(expected.Height), pngHeight);

        // Доля закрашенных пикселей в поле сечения, а не один пиксель: фибры разделены
        // белыми границами в 0.7 px, и точечная проба легко попадает на такую границу.
        // Прямоугольник взят внутри отрисованного сечения (оно занимает 250..519 x 50..588).
        double painted = PaintedShare(png, 260, 60, 510, 580);
        Assert.True(painted > 0.7,
            $"Поле сечения закрашено лишь на {painted:P0} — карта не отрисована. " +
            $"DPI={scaleX}x{scaleY}.");

        // Боковая панель со шкалой рисуется безусловно — контрольная точка обвязки.
        var sidebar = SamplePixel(png, SidebarProbeX, pngHeight / 2);
        Assert.False(sidebar is { R: 255, G: 255, B: 255 },
            "Пиксель боковой панели белый — SVG не отрисован целиком.");
    }

    [SkippableFact]
    public void PrintToPdf_RendersSectionStateMapOnA4()
    {
        string svg = RenderMapSvg();
        // Та же обёртка, что даст HtmlReportRenderer: SVG внутри страницы с @page A4.
        string html = "<html><head><style>@page { size:A4; margin:0; }</style></head>" +
                      $"<body style=\"margin:0\">{svg}</body></html>";

        byte[]? pdf = null;

        RunOnHiddenView(async (view, host) =>
        {
            await NavigateAsync(view, html);

            var settings = view.CoreWebView2.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.ScaleFactor = 1.0;
            settings.Orientation = CoreWebView2PrintOrientation.Portrait;
            settings.PageWidth = 8.27;   // A4 в дюймах
            settings.PageHeight = 11.69;
            settings.MarginTop = settings.MarginBottom = 0;
            settings.MarginLeft = settings.MarginRight = 0;

            using var pdfStream = await view.CoreWebView2.PrintToPdfStreamAsync(settings);
            using var buffer = new MemoryStream();
            await pdfStream.CopyToAsync(buffer);
            pdf = buffer.ToArray();
        });

        Assert.NotNull(pdf);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf!, 0, 5));

        // Геометрия страницы: A4 портрет = 595 x 842 pt (допуск 2 pt на округление движка).
        var box = Regex.Match(
            Encoding.Latin1.GetString(pdf!),
            @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));
        Assert.True(box.Success, "В PDF не найден /MediaBox — печать не дала страницы.");
        Assert.InRange(double.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture), 593, 597);
        Assert.InRange(double.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture), 840, 844);

        // Карта не потерялась: PDF с векторной картой заведомо тяжелее пустой страницы.
        // Порог грубый и служит только сигналом «контент есть»; точную проверку даёт глаз.
        Assert.True(pdf!.Length > 20_000,
            $"PDF подозрительно мал ({pdf.Length} байт) — вероятно, SVG не отрисовался.");

        // Сохранить рядом с временными файлами для обязательного ручного просмотра (шаг 9).
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "opencs-poc.pdf"), pdf);
    }

    /// <summary>Карта НДС реальной балки 300×600 (B25, два стержня A500 понизу) под изгибом:
    /// сетка фибр нарезана, диаграммы построены, деформации посчитаны — на карте есть
    /// цветное поле бетона и стержни, а не только оси и цветовая шкала.</summary>
    static string RenderMapSvg()
    {
        var section = BuildBeam();

        // Изгиб: сжатие верхней грани, растяжение нижней (там и стоит арматура).
        // SetEps проставляет ε и σ в каждой фибре — без него все σ нулевые, и диверджентный
        // колормап (ноль = белый) даёт белую карту при формально корректной геометрии.
        var k = new Kurvature { e0 = 0.001, ky = -0.005, kz = 0 };
        section.SetEps(k, CalcType.C);

        // Режим деформаций, а не напряжений: в карте σ растянутая зона бетона за трещиной
        // законно нулевая и потому белая на ¾ площади — по такой картинке ни автопроверка,
        // ни глаз не отличат «карта пустая» от «карта посчитана». В карте ε цвет есть везде.
        var plot = new SectionPlotVM(section, k, CalcType.C, SectionPlotMode.Strain);
        return SvgSizing.EnsureExplicitDimensions(
            new SectionStateSvgExporter().Render(plot, "Карта деформаций ε"));
    }

    /// <summary>Балка 300 × 600: B25 с сеткой фибр 8 × 16 и два стержня A500 понизу.
    /// Своя фикстура, а не ShearInclinedFixtures.Beam(): та задаёт характеристики через
    /// сеттер Material.C, который наполняет только внутренний словарь, оставляя список
    /// materialChars пустым, — а карте нужны построенные диаграммы всех видов расчёта.</summary>
    static CrossSection BuildBeam()
    {
        var concrete = new Material
        {
            Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0,
            MaterialChars = AllCalcTypes(() => new MaterialChars(CalcType.C)
            {
                Type = MatType.Concrete,
                Fc = -14_500.0,          // сжатие — «минус»
                Ft = 1_050.0,
                E = 30_000_000.0,
                Ec1Red = -0.0015,
                Ec2 = -0.0035,
                Et1Red = 0.00008,
                Et2 = 0.00015
            })
        };

        var steel = new Material
        {
            Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0,
            MaterialChars = AllCalcTypes(() => new MaterialChars(CalcType.C)
            {
                Type = MatType.ReSteelF,
                Class = 500.0,
                Fc = -435_000.0,
                Ft = 435_000.0,
                E = 200_000_000.0,
                // Без предельных деформаций узлы двухлинейной диаграммы совпадают
                // и D2L() падает на интерполяции.
                Ec2 = -0.0035,
                Et2 = 0.025
            })
        };

        var region = new MaterialArea
        {
            Tag = "Бетон",
            Category = AreaCategory.Region,
            Material = concrete,
            MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2
        };
        region.Contours.Add(new Contour(
            [-0.15, 0.15, 0.15, -0.15, -0.15],
            [-0.30, -0.30, 0.30, 0.30, -0.30], "hull") { Type = ContourType.Hull });
        region.SetWKT();
        region.SliceXY(nx: 8, ny: 16);

        var rebar = new MaterialArea
        {
            Tag = "Арматура",
            Category = AreaCategory.RebarGroup,
            Material = steel,
            MaterialId = steel.Id,
            DiagrammType = DiagrammType.L2
        };
        rebar.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = -0.08, Y = -0.25, Area = 0.000616 });
        rebar.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = 0.08, Y = -0.25, Area = 0.000616 });

        var section = new CrossSection { Tag = "Б-1" };
        section.Areas.Add(region);
        section.Areas.Add(rebar);
        section.ResolveAndBuildDiagramms();
        return section;
    }

    /// <summary>Одни и те же характеристики на все четыре вида расчёта: Material.GetD2L
    /// строит диаграммы сразу для C/CL/N/NL и падает, если хоть одного нет. Для POC
    /// длительные принимаются равными кратковременным — карта строится для CalcType.C.</summary>
    static List<MaterialChars> AllCalcTypes(Func<MaterialChars> factory)
    {
        var all = new List<MaterialChars>();
        foreach (var calcType in new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL })
        {
            var chars = factory();
            chars.TypeCalc = calcType;
            all.Add(chars);
        }
        return all;
    }

    /// <summary>Общий каркас офскрин-прогона: STA-нить, свой Dispatcher, скрытое окно с
    /// WebView2 и изолированный userDataFolder. Возвращает фактический DPI-масштаб окна.</summary>
    static (double ScaleX, double ScaleY) RunOnHiddenView(Func<WebView2, Window, Task> body)
    {
        Exception? error = null;
        double scaleX = 0, scaleY = 0;

        var thread = new Thread(() =>
        {
            string userData = Path.Combine(Path.GetTempPath(),
                "opencs-wv2-poc-" + Guid.NewGuid().ToString("N"));
            try
            {
                // Application намеренно не создаётся: он один на AppDomain и привязан к
                // потоку создания, поэтому второй прогон в новом STA-потоке падал бы с
                // «Нельзя создать более одного экземпляра System.Windows.Application».
                // Скрытому окну с WebView2 достаточно собственного Dispatcher.
                var pump = Dispatcher.CurrentDispatcher;
                // Без явного контекста FromCurrentSynchronizationContext() бросил бы
                // InvalidOperationException: на голой STA-нити SynchronizationContext пуст.
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(pump));

                async Task RunAsync()
                {
                    var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
                    var view = new WebView2();
                    var host = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false,
                        ShowActivated = false,
                        Left = -10000,
                        Top = -10000,
                        Width = 10,
                        Height = 10,
                        Content = view
                    };
                    host.Show();
                    await view.EnsureCoreWebView2Async(env);

                    var dpi = VisualTreeHelper.GetDpi(host);
                    scaleX = dpi.DpiScaleX;
                    scaleY = dpi.DpiScaleY;

                    try
                    {
                        await body(view, host);
                    }
                    finally
                    {
                        view.Dispose();
                        host.Close();
                    }
                }

                _ = RunAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted) error = t.Exception?.GetBaseException();
                    pump.InvokeShutdown();
                }, TaskScheduler.FromCurrentSynchronizationContext());

                Dispatcher.Run();
            }
            catch (Exception ex) { error = ex; }
            finally { try { Directory.Delete(userData, true); } catch { } }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Skip.If(error is WebView2RuntimeNotFoundException,
            "WebView2 Runtime не установлен на этой машине.");
        Assert.Null(error);

        return (scaleX, scaleY);
    }

    static async Task NavigateAsync(WebView2 view, string html)
    {
        var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNavigated(object? s, CoreWebView2NavigationCompletedEventArgs e)
            => navigated.TrySetResult(e.IsSuccess);

        view.NavigationCompleted += OnNavigated;
        try
        {
            view.NavigateToString(html);
            Assert.True(await navigated.Task);
        }
        finally { view.NavigationCompleted -= OnNavigated; }
    }

    static (int Width, int Height) ReadIhdr(byte[] png)
    {
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }

    /// <summary>Доля не-белых пикселей в прямоугольнике — мера «поле действительно залито».</summary>
    static double PaintedShare(byte[] png, int x0, int y0, int x1, int y1)
    {
        using var stream = new MemoryStream(png);
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            decoder.Frames[0], PixelFormats.Bgra32, null, 0);

        int width = x1 - x0, height = y1 - y0;
        var buffer = new byte[width * height * 4];
        frame.CopyPixels(new Int32Rect(x0, y0, width, height), buffer, width * 4, 0);

        int painted = 0;
        for (int i = 0; i < buffer.Length; i += 4)
            if (buffer[i] != 255 || buffer[i + 1] != 255 || buffer[i + 2] != 255)
                painted++;

        return (double)painted / (width * height);
    }

    static (byte R, byte G, byte B) SamplePixel(byte[] png, int x, int y)
    {
        using var stream = new MemoryStream(png);
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var buffer = new byte[4];
        frame.CopyPixels(new Int32Rect(x, y, 1, 1), buffer, 4, 0);
        return (buffer[2], buffer[1], buffer[0]);
    }
}
