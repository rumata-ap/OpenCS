using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCS.Render3D;
using SkiaSharp;

namespace OpenCS.Views.Helpers;

/// <summary>Итог замера скорости вращения 3D-вида.</summary>
/// <param name="Frames">Число кадров.</param>
/// <param name="Fps">Средняя частота кадров по настенному времени.</param>
/// <param name="AvgFrameMs">Среднее время отрисовки кадра на UI-потоке, мс (для Helix — 0: не измеряется).</param>
public readonly record struct ViewBenchmarkResult(int Frames, double Fps, double AvgFrameMs);

/// <summary>
/// WPF-хост программного Skia-рендера <see cref="SceneRenderer"/>: Skia рисует прямо в буфер
/// <see cref="WriteableBitmap"/> в физических пикселях, WPF выводит его как картинку.
/// ЛКМ/ПКМ — вращение, СКМ или Shift+ЛКМ — панорама, колесо — масштаб, двойной щелчок — вписать.
/// </summary>
public sealed class SkiaSceneView : FrameworkElement
{
    readonly SceneRenderer _renderer = new() { Background = SKColors.White };
    readonly OrbitCamera _camera = new();
    WriteableBitmap? _bitmap;
    Scene3D? _scene;
    Point? _drag;
    bool _pan;

    /// <summary>Статистика последнего кадра.</summary>
    public RenderStats LastStats { get; private set; }

    /// <summary>Полное время последнего кадра на UI-потоке (рендер + передача картинки WPF), мс.</summary>
    public double LastFrameMs { get; private set; }

    /// <summary>Вызывается после каждого кадра.</summary>
    public event EventHandler? FrameRendered;

    public SkiaSceneView()
    {
        Focusable = true;
    }

    /// <summary>Задаёт сцену; при <paramref name="fit"/> камера вписывается в её габарит.</summary>
    public void SetScene(Scene3D? scene, bool fit)
    {
        _scene = scene;
        if (fit) ZoomExtents();
        else InvalidateVisual();
    }

    public void ZoomExtents()
    {
        if (_scene != null && _scene.TryGetBounds(out var min, out var max)) _camera.Fit(min, max);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var sw = Stopwatch.StartNew();
        var dpi = VisualTreeHelper.GetDpi(this);
        int pw = (int)(ActualWidth * dpi.DpiScaleX), ph = (int)(ActualHeight * dpi.DpiScaleY);
        if (pw < 2 || ph < 2) return;

        if (_bitmap == null || _bitmap.PixelWidth != pw || _bitmap.PixelHeight != ph)
            _bitmap = new WriteableBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32, null);

        _bitmap.Lock();
        try
        {
            var info = new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, _bitmap.BackBuffer, _bitmap.BackBufferStride);
            var canvas = surface.Canvas;
            if (_scene == null)
            {
                canvas.Clear(_renderer.Background);
            }
            else
            {
                // Толщины линий и размеры точек сцены — в логических пикселях WPF.
                canvas.Scale((float)dpi.DpiScaleX, (float)dpi.DpiScaleY);
                LastStats = _renderer.Render(canvas, (float)ActualWidth, (float)ActualHeight, _scene, _camera);
            }
            canvas.Flush();
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, pw, ph));
        }
        finally
        {
            _bitmap.Unlock();
        }

        dc.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
        LastFrameMs = sw.Elapsed.TotalMilliseconds;
        FrameRendered?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Полный оборот камеры за <paramref name="frames"/> кадров с замером.</summary>
    public Task<ViewBenchmarkResult> RunBenchmarkAsync(int frames)
    {
        var tcs = new TaskCompletionSource<ViewBenchmarkResult>();
        int done = 0;
        double frameMs = 0;
        float step = 2 * MathF.PI / frames;
        var wall = Stopwatch.StartNew();

        void OnFrame(object? s, EventArgs e) => frameMs += LastFrameMs;
        void OnTick(object? s, EventArgs e)
        {
            if (done == frames)
            {
                CompositionTarget.Rendering -= OnTick;
                FrameRendered -= OnFrame;
                tcs.SetResult(new ViewBenchmarkResult(frames, frames / wall.Elapsed.TotalSeconds, frameMs / frames));
                return;
            }
            _camera.Yaw += step;
            done++;
            InvalidateVisual();
        }

        FrameRendered += OnFrame;
        CompositionTarget.Rendering += OnTick;
        return tcs.Task;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.ClickCount == 2) { ZoomExtents(); return; }
        _drag = e.GetPosition(this);
        _pan = e.ChangedButton == MouseButton.Middle || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag is not { } from) return;
        var p = e.GetPosition(this);
        float dx = (float)(p.X - from.X), dy = (float)(p.Y - from.Y);
        if (_pan) _camera.Pan(dx, dy, (float)ActualHeight);
        else _camera.Orbit(dx, dy);
        _drag = p;
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = null;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _camera.Zoom(e.Delta / 120f);
        InvalidateVisual();
        e.Handled = true;
    }
}
