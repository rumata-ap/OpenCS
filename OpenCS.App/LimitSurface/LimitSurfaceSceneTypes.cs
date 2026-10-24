using CScore.LimitSurface;

namespace OpenCS.LimitSurface;

/// <summary>Система осей сцены: значения как есть или доли размаха сетки 0…1 по каждой оси.</summary>
public enum AxisMode { Absolute, Relative }

/// <summary>Категория активного ограничения: материал контрольной точки и сторона предела.</summary>
public enum ActiveCategory { None, ConcreteCompression, ConcreteTension, RebarCompression, RebarTension }

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
