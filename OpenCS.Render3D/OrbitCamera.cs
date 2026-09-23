using System.Numerics;

namespace OpenCS.Render3D;

/// <summary>
/// Орбитальная перспективная камера: вращается вокруг цели, ось Z мира — вверх.
/// Поле зрения по вертикали — 45°.
/// </summary>
public sealed class OrbitCamera
{
    const float MaxPitch = 1.55f;

    /// <summary>Точка, вокруг которой вращается камера.</summary>
    public Vector3 Target { get; set; }

    /// <summary>Азимут, рад (вокруг оси Z).</summary>
    public float Yaw { get; set; } = -0.9f;

    /// <summary>Угол места, рад (положительный — взгляд сверху).</summary>
    public float Pitch { get; set; } = 0.45f;

    /// <summary>Расстояние от камеры до цели.</summary>
    public float Distance { get; set; } = 10f;

    /// <summary>Нацеливает камеру на габарит сцены.</summary>
    public void Fit(Vector3 min, Vector3 max)
    {
        Target = (min + max) / 2;
        Distance = MathF.Max((max - min).Length() * 1.3f, 1e-3f);
    }

    /// <summary>Вращение по смещению мыши в пикселях.</summary>
    public void Orbit(float dx, float dy)
    {
        Yaw -= dx * 0.008f;
        Pitch = Math.Clamp(Pitch + dy * 0.008f, -MaxPitch, MaxPitch);
    }

    /// <summary>Сдвиг цели по смещению мыши в пикселях при заданной высоте окна.</summary>
    public void Pan(float dx, float dy, float viewportHeight)
    {
        var p = CreateProjector(1, MathF.Max(viewportHeight, 1));
        float k = Distance / p.Focal;
        Target += (-dx * p.Right + dy * p.Up) * k;
    }

    /// <summary>Приближение (<paramref name="steps"/> &gt; 0) или отдаление.</summary>
    public void Zoom(float steps) => Distance *= MathF.Pow(0.87f, steps);

    /// <summary>Проектор для окна заданного размера в пикселях.</summary>
    public Projector CreateProjector(float width, float height)
    {
        var eye = Target + Distance * new Vector3(
            MathF.Cos(Pitch) * MathF.Cos(Yaw), MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch));
        var fwd = Vector3.Normalize(Target - eye);
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitZ));
        var up = Vector3.Cross(right, fwd);
        float focal = height / 2 / MathF.Tan(MathF.PI / 8);
        return new Projector(eye, fwd, right, up, focal, width / 2, height / 2, Distance * 1e-3f);
    }
}

/// <summary>Перспективная проекция мира на экран для одного кадра.</summary>
public readonly record struct Projector(
    Vector3 Eye, Vector3 Forward, Vector3 Right, Vector3 Up,
    float Focal, float CenterX, float CenterY, float Near)
{
    /// <summary>Экранные X, Y и глубина вдоль луча зрения; глубина &lt; 0 — точка за ближней плоскостью.</summary>
    public Vector3 Project(Vector3 p)
    {
        var d = p - Eye;
        float z = Vector3.Dot(d, Forward);
        if (z < Near) return new Vector3(0, 0, -1);
        return new Vector3(CenterX + Focal * Vector3.Dot(d, Right) / z, CenterY - Focal * Vector3.Dot(d, Up) / z, z);
    }
}
