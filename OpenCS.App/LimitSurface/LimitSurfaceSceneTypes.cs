using CScore.LimitSurface;

namespace OpenCS.LimitSurface;

/// <summary>Система осей сцены: значения как есть или доли размаха сетки 0…1 по каждой оси.</summary>
public enum AxisMode { Absolute, Relative }

/// <summary>
/// Категория активного ограничения: материал контрольной точки и сторона предела; у ширины раскрытия —
/// условие (продолжительное/непродолжительное раскрытие, σs ≤ Rs,ser, сжатие бетона, граница тела ПС2).
/// </summary>
public enum ActiveCategory
{
   None, ConcreteCompression, ConcreteTension, RebarCompression, RebarTension,
   AcrcLong, AcrcShort, SteelStress, CrackBody,
}

/// <summary>Положение точки набора относительно поверхности.</summary>
public enum PointState
{
   /// <summary>Внутри, Кисп ≤ 1.</summary>
   Inside,
   /// <summary>Снаружи, Кисп &gt; 1 (или ∞).</summary>
   Outside,
   /// <summary>Начало луча вне тела поверхности — Кисп неоднозначен.</summary>
   Ambiguous,
}

/// <summary>Точка набора в сцене: Кисп по текущему режиму, положение, точка поверхности на луче, ограничение в ней.</summary>
public sealed record ScenePoint(int Index, LimitSurfacePointRecord Source, double Ratio, PointState State,
   ForcePoint? Capacity, ActiveCategory Active, bool SingleSign);

/// <summary>Сводка по точкам: максимальный Кисп, число точек снаружи и неоднозначных.</summary>
public sealed record SceneSummary(double MaxRatio, int Outside, int Ambiguous);

/// <summary>Точка в осях сцены: X = Mx, Y = My, Z = N.</summary>
public readonly record struct Vec3(double X, double Y, double Z);

/// <summary>Раскраска поверхности в 3D.</summary>
public enum SurfaceColoring
{
   /// <summary>По активному ограничению (материал и сторона предела).</summary>
   Active,
   /// <summary>Зона однозначной эпюры (8.53) — только ПС1.</summary>
   SingleSign,
   /// <summary>Полосы по N.</summary>
   ByN,
   /// <summary>Однотонная.</summary>
   Mono,
}

/// <summary>Группа треугольников одного цвета; подпись легенды — ключ строки или готовый текст (полосы N).</summary>
public sealed record ColorGroup(string ColorHex, string LegendKey, string? LegendText, IReadOnlyList<int> Triangles);

/// <summary>Вид среза: Mx–My при N; N–My при Mx; N–Mx при My; N–M по направлению α.</summary>
public enum SliceKind { AtN, AtMx, AtMy, NM }

/// <summary>Линии сетки параметров на поверхности: меридианы (полюс → полюс) и замкнутые параллели.</summary>
public sealed record SurfaceGridLines(IReadOnlyList<IReadOnlyList<ForcePoint>> Meridians,
   IReadOnlyList<IReadOnlyList<ForcePoint>> Parallels);

/// <summary>Контуры среза на одном уровне в 2D-координатах среза.</summary>
public sealed record SliceCurve(double Level, IReadOnlyList<IReadOnlyList<(double X, double Y)>> Loops);
