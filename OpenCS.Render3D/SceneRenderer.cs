using System.Diagnostics;
using System.Numerics;
using SkiaSharp;

namespace OpenCS.Render3D;

/// <summary>Статистика одного кадра.</summary>
/// <param name="Triangles">Число треугольников, отправленных в Skia.</param>
/// <param name="PrepareMs">Проекция, освещение и сортировка по глубине, мс.</param>
/// <param name="DrawMs">Растеризация одним вызовом <c>DrawVertices</c>, мс.</param>
public readonly record struct RenderStats(int Triangles, double PrepareMs, double DrawMs);

/// <summary>
/// Программный рендер <see cref="Scene3D"/> в <see cref="SKCanvas"/>: перспективная проекция,
/// сортировка примитивов по глубине (алгоритм художника), вся сцена — одним <c>DrawVertices</c>.
/// Не зависит от UI-фреймворка; хост (WPF, Avalonia) отвечает только за холст и ввод мыши.
/// </summary>
public sealed class SceneRenderer
{
    // Отрезки и точки чуть «подтягиваются» к камере, чтобы рёбра не тонули в своих гранях.
    const float LineDepthBias = 0.998f;
    const float PointDepthBias = 0.997f;

    struct Prim
    {
        public SKPoint A, B, C, D;
        public SKColor Color;
        public bool IsQuad;
    }

    Prim[] _prims = new Prim[1024];
    float[] _depth = new float[1024];
    int _count;

    SKPoint[] _pos = [];
    SKColor[] _cols = [];

    /// <summary>Цвет фона.</summary>
    public SKColor Background { get; set; } = SKColors.White;

    /// <summary>Рисует сцену на холст размером <paramref name="width"/>×<paramref name="height"/> пикселей.</summary>
    public RenderStats Render(SKCanvas canvas, float width, float height, Scene3D scene, OrbitCamera camera)
    {
        canvas.Clear(Background);
        if (width < 2 || height < 2) return default;

        var sw = Stopwatch.StartNew();
        var pr = camera.CreateProjector(width, height);
        _count = 0;

        foreach (var t in scene.Triangles)
        {
            var a = pr.Project(t.A); var b = pr.Project(t.B); var c = pr.Project(t.C);
            if (a.Z < 0 || b.Z < 0 || c.Z < 0) continue;
            var n = Vector3.Cross(t.B - t.A, t.C - t.A);
            float len = n.Length();
            float shade = len > 0 ? 0.55f + 0.45f * MathF.Abs(Vector3.Dot(n / len, pr.Forward)) : 1f;
            Add(new Prim { A = new(a.X, a.Y), B = new(b.X, b.Y), C = new(c.X, c.Y), Color = Shade(t.Argb, shade) },
                (a.Z + b.Z + c.Z) / 3);
        }

        foreach (var l in scene.Lines)
        {
            var a = pr.Project(l.A); var b = pr.Project(l.B);
            if (a.Z < 0 || b.Z < 0) continue;
            float dx = b.X - a.X, dy = b.Y - a.Y, len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) continue;
            float nx = -dy / len * l.Width / 2, ny = dx / len * l.Width / 2;
            Add(new Prim
            {
                A = new(a.X + nx, a.Y + ny), B = new(b.X + nx, b.Y + ny),
                C = new(b.X - nx, b.Y - ny), D = new(a.X - nx, a.Y - ny),
                Color = new SKColor(l.Argb), IsQuad = true
            }, (a.Z + b.Z) / 2 * LineDepthBias);
        }

        foreach (var p in scene.Points)
        {
            var q = pr.Project(p.P);
            if (q.Z < 0) continue;
            float h = p.Size / 2;
            Add(new Prim
            {
                A = new(q.X - h, q.Y - h), B = new(q.X + h, q.Y - h),
                C = new(q.X + h, q.Y + h), D = new(q.X - h, q.Y + h),
                Color = new SKColor(p.Argb), IsQuad = true
            }, q.Z * PointDepthBias);
        }

        // Дальние — первыми: ключ сортировки — глубина со знаком минус.
        Array.Sort(_depth, _prims, 0, _count);
        int tris = Emit();
        double prepMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        if (tris > 0)
        {
            using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles,
                _pos.AsSpan(0, tris * 3).ToArray(), null, _cols.AsSpan(0, tris * 3).ToArray());
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawVertices(vertices, SKBlendMode.Dst, paint);
        }
        return new RenderStats(tris, prepMs, sw.Elapsed.TotalMilliseconds);
    }

    void Add(in Prim prim, float depth)
    {
        if (_count == _prims.Length)
        {
            Array.Resize(ref _prims, _count * 2);
            Array.Resize(ref _depth, _count * 2);
        }
        _prims[_count] = prim;
        _depth[_count++] = -depth;
    }

    int Emit()
    {
        int tris = 0;
        for (int i = 0; i < _count; i++) tris += _prims[i].IsQuad ? 2 : 1;
        if (_pos.Length < tris * 3)
        {
            _pos = new SKPoint[tris * 3];
            _cols = new SKColor[tris * 3];
        }

        int k = 0;
        for (int i = 0; i < _count; i++)
        {
            ref var p = ref _prims[i];
            Put(ref k, p.A, p.B, p.C, p.Color);
            if (p.IsQuad) Put(ref k, p.A, p.C, p.D, p.Color);
        }
        return tris;
    }

    void Put(ref int k, SKPoint a, SKPoint b, SKPoint c, SKColor col)
    {
        _pos[k] = a; _pos[k + 1] = b; _pos[k + 2] = c;
        _cols[k] = _cols[k + 1] = _cols[k + 2] = col;
        k += 3;
    }

    static SKColor Shade(uint argb, float k)
    {
        var c = new SKColor(argb);
        return new SKColor((byte)(c.Red * k), (byte)(c.Green * k), (byte)(c.Blue * k), c.Alpha);
    }
}
