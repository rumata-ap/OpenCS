namespace CScore;

/// <summary>
/// Адаптер <see cref="CrossSection"/> к контракту <see cref="ILimitSection"/>.
/// </summary>
public sealed class CrossSectionLimitAdapter : ILimitSection
{
   /// <summary>Исходное сечение.</summary>
   public CrossSection Section { get; }

   /// <inheritdoc/>
   public IEnumerable<(double X, double Y)> ContourVertices { get; }

   /// <inheritdoc/>
   public IEnumerable<(double X, double Y, double EpsSu)> RebarPoints { get; }

   /// <inheritdoc/>
   public double EpsCu { get; }

   /// <summary>
   /// Создаёт адаптер сечения для решателя предельных усилий.
   /// </summary>
   public CrossSectionLimitAdapter(CrossSection section, CalcType calc = CalcType.C)
   {
      Section = section ?? throw new ArgumentNullException(nameof(section));
      ContourVertices = CollectContourVertices(section);
      RebarPoints = CollectRebarPoints(section, calc);
      EpsCu = ResolveEpsCu(section, calc);
   }

   /// <inheritdoc/>
   public Load Integral(Kurvature k, CalcType calc, bool ten = true) => Section.Integral(k, calc, ten);

   static IEnumerable<(double X, double Y)> CollectContourVertices(CrossSection section)
   {
      var pts = new List<(double X, double Y)>();
      foreach (var area in section.Areas)
      {
         if (area.Hull is not null)
            pts.AddRange(area.Hull.X.Zip(area.Hull.Y, (x, y) => (x, y)));

         foreach (var hole in area.Holes)
            pts.AddRange(hole.X.Zip(hole.Y, (x, y) => (x, y)));
      }
      return pts;
   }

   static IEnumerable<(double X, double Y, double EpsSu)> CollectRebarPoints(CrossSection section, CalcType calc)
   {
      var res = new List<(double X, double Y, double EpsSu)>();
      foreach (var area in section.Areas)
      {
         foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
         {
            double epsSu = ResolveRebarEpsSu(area, calc);
            res.Add((f.X, f.Y, epsSu));
         }
      }
      return res;
   }

   static double ResolveEpsCu(CrossSection section, CalcType calc)
   {
      var candidates = new List<double>();
      foreach (var area in section.Areas)
      {
         if (area.Material?.Type != MatType.Concrete)
            continue;

         var ch = ResolveChars(area.Material, calc);
         if (ch is not null && double.IsFinite(ch.Ec2))
            candidates.Add(ch.Ec2);

         if (area.Diagramms.TryGetValue(calc, out var dgr) && dgr.Ic.X.Length > 0)
            candidates.Add(dgr.Ic.X.Min());
      }

      if (candidates.Count == 0)
         return -0.0035;

      double min = candidates.Min();
      return min < 0 ? min : -Math.Abs(min);
   }

   static double ResolveRebarEpsSu(MaterialArea area, CalcType calc)
   {
      var ch = area.Material is null ? null : ResolveChars(area.Material, calc);
      if (ch is not null && double.IsFinite(ch.Et2) && ch.Et2 > 0)
         return ch.Et2;

      if (area.Diagramms.TryGetValue(calc, out var dgr) && dgr.It.X.Length > 0)
      {
         double max = dgr.It.X.Max();
         if (double.IsFinite(max) && max > 0)
            return max;
      }

      return 0.025;
   }

   static MaterialChars? ResolveChars(Material material, CalcType calc)
      => calc switch
      {
         CalcType.C => material.C,
         CalcType.CL => material.CL,
         CalcType.N => material.N,
         CalcType.NL => material.NL,
         _ => material.C
      };
}
