using CScore.LimitSurface;

namespace OpenCS.LimitSurface;

public sealed partial class LimitSurfaceScene
{
   static readonly string[] NBands =
      ["#313695", "#4575B4", "#74ADD1", "#ABD9E9", "#E0F3F8", "#FEE090", "#FDAE61", "#F46D43", "#D73027", "#A50026"];

   static string ActiveColor(ActiveCategory c) => c switch
   {
      ActiveCategory.ConcreteCompression => "#4472C4",
      ActiveCategory.ConcreteTension => "#A5A5A5",
      ActiveCategory.RebarCompression => "#70AD47",
      ActiveCategory.RebarTension => "#ED7D31",
      ActiveCategory.AcrcLong => "#7030A0",
      ActiveCategory.AcrcShort => "#C55A11",
      ActiveCategory.SteelStress => "#FFC000",
      ActiveCategory.CrackBody => "#A5A5A5",
      _ => "#BFBFBF",
   };

   /// <summary>Поверхность образования трещин (ПС2).</summary>
   public bool IsCrack => Data.Criterion == LimitSurfaceResultData.CriterionCrack;

   /// <summary>Поверхность ширины раскрытия трещин (ПС2).</summary>
   public bool IsAcrc => Data.Criterion == LimitSurfaceResultData.CriterionAcrc;

   /// <summary>Раскраска «однозначная эпюра»: (8.53) у ПС1, (8.53)/(8.54) у ПС2.</summary>
   public bool SupportsSingleSign => Data.Criterion is LimitSurfaceResultData.CriterionUls or LimitSurfaceResultData.CriterionCrack;

   /// <summary>Ключ строки пункта раскраски (у ПС2 «однозначная эпюра» охватывает и (8.54)).</summary>
   public string ColoringKey(SurfaceColoring c) =>
      c == SurfaceColoring.SingleSign && IsCrack ? "LimitSurfaceColoring_SingleSign_Crack" : $"LimitSurfaceColoring_{c}";

   /// <summary>Ключ подписи категории активного ограничения; у ПС2 растяжение бетона — это трещина.</summary>
   public string ActiveLegendKey(ActiveCategory c) =>
      c == ActiveCategory.ConcreteTension && IsCrack ? "LimitSurfaceLegend_ConcreteTension_Crack"
      : c == ActiveCategory.ConcreteCompression && IsAcrc ? "LimitSurfaceLegend_ConcreteCompression_Acrc"
      : $"LimitSurfaceLegend_{c}";

   /// <summary>Пометка правила однозначной эпюры к подписи ограничения: растяжение — (8.54), иначе (8.53).</summary>
   public string SingleSignNote(ActiveCategory c, bool singleSign) =>
      !singleSign ? "" : c == ActiveCategory.ConcreteTension ? " (8.54)" : " (8.53)";

   /// <summary>Треугольники сетки, разбитые на группы одного цвета (каждый треугольник — ровно в одной группе).</summary>
   public IReadOnlyList<ColorGroup> SurfaceGroups(SurfaceColoring coloring)
   {
      var tris = Mesh.Triangles;
      bool hasActive = Mesh.Active.Count == Mesh.Vertices.Count;
      switch (coloring)
      {
         case SurfaceColoring.Active when hasActive:
            return Enumerable.Range(0, tris.Count)
               .GroupBy(t => Majority(tris[t], v => CategoryOf(Mesh.Active[v])))
               .OrderBy(g => g.Key)
               .Select(g => new ColorGroup(ActiveColor(g.Key), ActiveLegendKey(g.Key), null, g.ToList()))
               .ToList();
         case SurfaceColoring.SingleSign when hasActive:
            return Enumerable.Range(0, tris.Count)
               .GroupBy(t => Majority(tris[t], v => Mesh.Active[v].SingleSign))
               .OrderBy(g => g.Key)
               .Select(g => g.Key
                  ? new ColorGroup("#C00000", IsCrack ? "LimitSurfaceLegend_SingleSign_Crack" : "LimitSurfaceLegend_SingleSign", null, g.ToList())
                  : new ColorGroup("#9DC3E6", "LimitSurfaceLegend_TwoSign", null, g.ToList()))
               .ToList();
         case SurfaceColoring.ByN:
         {
            double lo = Min.N, step = (Max.N - Min.N) / NBands.Length;
            return Enumerable.Range(0, tris.Count)
               .GroupBy(t =>
               {
                  var (a, b, c) = tris[t];
                  double n = (Mesh.Vertices[a].N + Mesh.Vertices[b].N + Mesh.Vertices[c].N) / 3;
                  return step > 0 ? Math.Clamp((int)((n - lo) / step), 0, NBands.Length - 1) : 0;
               })
               .OrderBy(g => g.Key)
               .Select(g => new ColorGroup(NBands[g.Key], "",
                  $"{lo + g.Key * step:F0}…{lo + (g.Key + 1) * step:F0}", g.ToList()))
               .ToList();
         }
         default:
            return [new ColorGroup("#8FAADC", "LimitSurfaceLegend_Mono", null, Enumerable.Range(0, tris.Count).ToList())];
      }
   }

   /// <summary>Значение, общее хотя бы для двух вершин треугольника, иначе — значение вершины A.</summary>
   internal static T Majority<T>((int A, int B, int C) t, Func<int, T> f) where T : notnull
   {
      T a = f(t.A), b = f(t.B), c = f(t.C);
      return a.Equals(b) || a.Equals(c) ? a : b.Equals(c) ? b : a;
   }
}
