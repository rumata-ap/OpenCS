using CScore.LimitSurface;

namespace OpenCS.LimitSurface;

public sealed partial class LimitSurfaceScene
{
   /// <summary>В сетке есть углы вершин и известна базовая разбивка — можно рисовать меридианы и параллели.</summary>
   public bool HasGridLines => Mesh.Angles.Count == Mesh.Vertices.Count && Data.InitialMeridians > 0 && Data.ThetaIntervals > 0;

   /// <summary>
   /// Базовые меридианы (φ = 2πk/n₀) от полюса растяжения к полюсу сжатия через все свои узлы и параллели
   /// θ = πj/n через узел этого θ на каждом меридиане (базовые θ есть на любом меридиане). Разбивка — как у
   /// построителя (не меньше 4). Без углов — пусто.
   /// </summary>
   public SurfaceGridLines GridLines()
   {
      if (!HasGridLines) return new([], []);
      var v = Mesh.Vertices;
      var byMeridian = Enumerable.Range(0, Mesh.MeridianCount).Select(_ => new List<int>()).ToList();
      for (int i = 0; i < v.Count; i++)
         if (Mesh.MeridianOf[i] >= 0) byMeridian[Mesh.MeridianOf[i]].Add(i);

      int n0 = Math.Max(Data.InitialMeridians, 4), n = Math.Max(Data.ThetaIntervals, 4);
      var meridians = byMeridian
         .Where(m => m.Count > 0 && IsMultiple(Mesh.Angles[m[0]].Phi, 2 * Math.PI / n0))
         .Select(m => (IReadOnlyList<ForcePoint>)m.Select(i => v[i]).Prepend(v[0]).Append(v[1]).ToList())
         .ToList();

      var parallels = new List<IReadOnlyList<ForcePoint>>();
      for (int j = 1; j < n; j++)
      {
         double theta = Math.PI * j / n;
         var loop = new List<ForcePoint>();
         foreach (var m in byMeridian)
         {
            int at = m.FindIndex(i => Math.Abs(Mesh.Angles[i].Theta - theta) < 1e-12);
            if (at >= 0) loop.Add(v[m[at]]);
         }
         if (loop.Count < 2) continue;
         loop.Add(loop[0]);
         parallels.Add(loop);
      }
      return new(meridians, parallels);
   }

   static bool IsMultiple(double x, double step)
   {
      double k = Math.Round(x / step);
      return Math.Abs(x - k * step) < 1e-9;
   }
}
