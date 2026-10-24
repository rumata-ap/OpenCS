using CScore.LimitSurface;

namespace OpenCS.LimitSurface;

public sealed partial class LimitSurfaceScene
{
   /// <summary>Контуры сечения поверхности плоскостью среза для каждого уровня, в 2D-координатах среза.</summary>
   public IReadOnlyList<SliceCurve> Slice(SliceKind kind, IReadOnlyList<double> levels) => levels.Select(level =>
   {
      var loops = kind switch
      {
         SliceKind.AtN => Mesh.SliceAtN(level),
         SliceKind.AtMx => Mesh.SliceAtMx(level),
         SliceKind.AtMy => Mesh.SliceAtMy(level),
         _ => Mesh.SliceNM(level * Math.PI / 180),
      };
      return new SliceCurve(level, loops
         .Select(l => (IReadOnlyList<(double X, double Y)>)l.Select(p => Project(kind, level, p)).ToList())
         .ToList());
   }).ToList();

   /// <summary>
   /// Проекция точки на плоскость среза: AtN — (Mx, My); AtMx — (My, N); AtMy — (Mx, N);
   /// NM — (M вдоль направления α, N). Уровень NM — α в градусах.
   /// </summary>
   public (double X, double Y) Project(SliceKind kind, double level, ForcePoint p) => kind switch
   {
      SliceKind.AtN => (p.Mx, p.My),
      SliceKind.AtMx => (p.My, p.N),
      SliceKind.AtMy => (p.Mx, p.N),
      _ => (p.Mx * Math.Cos(level * Math.PI / 180) + p.My * Math.Sin(level * Math.PI / 180), p.N),
   };

   /// <summary>Уровень среза, проходящего через точку набора (NM — α = atan2(My, Mx) в градусах).</summary>
   public static double LevelThrough(SliceKind kind, LimitSurfacePointRecord p) => kind switch
   {
      SliceKind.AtN => p.N,
      SliceKind.AtMx => p.Mx,
      SliceKind.AtMy => p.My,
      _ => Math.Atan2(p.My, p.Mx) * 180 / Math.PI,
   };

   /// <summary>Ключи подписей осей 2D-графика среза.</summary>
   public static (string X, string Y) SliceAxisKeys(SliceKind kind) => kind switch
   {
      SliceKind.AtN => ("LimitSurfaceAxisMx", "LimitSurfaceAxisMy"),
      SliceKind.AtMx => ("LimitSurfaceAxisMy", "LimitSurfaceAxisN"),
      SliceKind.AtMy => ("LimitSurfaceAxisMx", "LimitSurfaceAxisN"),
      _ => ("LimitSurfaceAxisM", "LimitSurfaceAxisN"),
   };

   /// <summary>Диапазон уровня для ползунка: габарит сетки по оси уровня; NM — −180…180°.</summary>
   public (double Min, double Max) LevelRange(SliceKind kind) => kind switch
   {
      SliceKind.AtN => (Min.N, Max.N),
      SliceKind.AtMx => (Min.Mx, Max.Mx),
      SliceKind.AtMy => (Min.My, Max.My),
      _ => (-180, 180),
   };
}
