using System.Numerics;

namespace OpenCS.Render3D;

/// <summary>
/// Нейтральная (не зависящая от UI-фреймворка) 3D-сцена: отрезки, треугольники и точки
/// в мировых координатах. Цвета — ARGB (0xAARRGGBB); толщины и размеры — в пикселях экрана.
/// </summary>
public sealed class Scene3D
{
    /// <summary>Отрезок постоянной экранной толщины.</summary>
    public readonly record struct Line(Vector3 A, Vector3 B, uint Argb, float Width);

    /// <summary>Треугольник; видим с обеих сторон, яркость зависит от угла к лучу зрения.</summary>
    public readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C, uint Argb);

    /// <summary>Точка-квадрат постоянного экранного размера.</summary>
    public readonly record struct Point(Vector3 P, uint Argb, float Size);

    public List<Line> Lines { get; } = [];
    public List<Triangle> Triangles { get; } = [];
    public List<Point> Points { get; } = [];

    /// <summary>Общее число примитивов сцены.</summary>
    public int Count => Lines.Count + Triangles.Count + Points.Count;

    /// <summary>Габаритный параллелепипед сцены; <c>false</c>, если сцена пуста.</summary>
    public bool TryGetBounds(out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);
        foreach (var l in Lines) { Grow(ref min, ref max, l.A); Grow(ref min, ref max, l.B); }
        foreach (var t in Triangles) { Grow(ref min, ref max, t.A); Grow(ref min, ref max, t.B); Grow(ref min, ref max, t.C); }
        foreach (var p in Points) Grow(ref min, ref max, p.P);
        return min.X <= max.X;
    }

    static void Grow(ref Vector3 min, ref Vector3 max, Vector3 p)
    {
        min = Vector3.Min(min, p);
        max = Vector3.Max(max, p);
    }
}
