using CScore.LimitSurface;

namespace OpenCS.LimitSurface;

/// <summary>
/// Модель сцены окна поверхности без WPF: Кисп точек, оси, группы цвета, срезы. Сетка восстанавливается
/// один раз; смена режима, осей, раскраски или среза пересчитывает только сцену.
/// </summary>
public sealed partial class LimitSurfaceScene
{
   /// <summary>Восстанавливает сетку из результата и считает её габарит.</summary>
   public LimitSurfaceScene(LimitSurfaceResultData data)
   {
      Data = data;
      Mesh = LimitSurfaceResultData.DecodeMesh(data.Mesh);
      var v = Mesh.Vertices;
      Min = new ForcePoint(v.Min(p => p.N), v.Min(p => p.Mx), v.Min(p => p.My));
      Max = new ForcePoint(v.Max(p => p.N), v.Max(p => p.Mx), v.Max(p => p.My));
   }

   /// <summary>Результат задачи.</summary>
   public LimitSurfaceResultData Data { get; }
   /// <summary>Восстановленная сетка.</summary>
   public LimitSurfaceMesh Mesh { get; }
   /// <summary>Нижний угол габарита сетки.</summary>
   public ForcePoint Min { get; }
   /// <summary>Верхний угол габарита сетки.</summary>
   public ForcePoint Max { get; }

   /// <summary>
   /// Режим луча при открытии окна: у ПС2 — N = const (п. 8.2.4, M &gt; M_crc при данном N, как задача cracking),
   /// у ПС1 — пропорциональный.
   /// </summary>
   public UtilizationMode DefaultMode => IsCrack ? UtilizationMode.Moment : UtilizationMode.All;

   /// <summary>Кисп и положение каждой точки набора для режима луча.</summary>
   public IReadOnlyList<ScenePoint> EvaluatePoints(UtilizationMode mode) => Data.Points.Select((p, i) =>
   {
      var u = Mesh.Utilization(p.N, p.Mx, p.My, mode);
      var state = !u.RayOriginInside ? PointState.Ambiguous : u.Inside ? PointState.Inside : PointState.Outside;
      return new ScenePoint(i, p, u.Ratio, state, u.Capacity,
         u.Active is { } a ? CategoryOf(a) : ActiveCategory.None, u.Active?.SingleSign ?? false);
   }).ToList();

   /// <summary>Максимальный Кисп (∞, если луч не пересёк поверхность), число точек снаружи и неоднозначных.</summary>
   public static SceneSummary Summarize(IReadOnlyList<ScenePoint> points) => new(
      points.Count == 0 ? 0 : points.Max(p => p.Ratio),
      points.Count(p => p.State == PointState.Outside),
      points.Count(p => p.State == PointState.Ambiguous));

   /// <summary>Координаты сцены: X = Mx, Y = My, Z = N; относительные — доли размаха сетки по каждой оси.</summary>
   public Vec3 ToScene(ForcePoint p, AxisMode axes) => axes == AxisMode.Absolute
      ? new Vec3(p.Mx, p.My, p.N)
      : new Vec3(Rel(p.Mx, Min.Mx, Max.Mx), Rel(p.My, Min.My, Max.My), Rel(p.N, Min.N, Max.N));

   static double Rel(double v, double lo, double hi) => hi > lo ? (v - lo) / (hi - lo) : 0.5;

   /// <summary>Категория активного ограничения: материал контрольной точки и сторона предела.</summary>
   public ActiveCategory CategoryOf(ActiveConstraint a)
   {
      if (a.PointIndex < 0 || a.PointIndex >= Data.ControlPoints.Count) return ActiveCategory.None;
      bool rebar = Data.ControlPoints[a.PointIndex].Kind == StrainPointKind.Rebar;
      return (rebar, a.Side < 0) switch
      {
         (false, true) => ActiveCategory.ConcreteCompression,
         (false, false) => ActiveCategory.ConcreteTension,
         (true, true) => ActiveCategory.RebarCompression,
         (true, false) => ActiveCategory.RebarTension,
      };
   }
}
