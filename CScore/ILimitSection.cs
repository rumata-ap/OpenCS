namespace CScore;

/// <summary>
/// Контракт сечения для проверки предельных деформаций.
/// </summary>
public interface ILimitSection
{
   /// <summary>Интеграл усилий при плоскости деформаций.</summary>
   Load Integral(Kurvature k, CalcType calc, bool ten = true);

   /// <summary>Вершины контура (внешний + отверстия) для проверки ε_cu.</summary>
   IEnumerable<(double X, double Y)> ContourVertices { get; }

   /// <summary>
   /// Арматура: координаты, предельная растягивающая деформация ε_su и начальная деформация
   /// преднапряжения ε_p. ε_su берётся у материала СВОЕГО стержня (физический предел текучести
   /// — 0.025, условный — 0.015), поэтому сравнивать деформацию можно только со «своим» ε_su.
   /// Проверять следует ПОЛНУЮ деформацию ε_плоскости + ε_p — именно её видит диаграмма.
   /// </summary>
   IEnumerable<(double X, double Y, double EpsSu, double EpsP)> RebarPoints { get; }

   /// <summary>Предельная сжимаемость бетона контура (отрицательное число).</summary>
   double EpsCu { get; }

   /// <summary>
   /// Вершины контура бетонного (неармированного) сечения с деформационными параметрами
   /// растяжения εbt0, εbt2 своего бетона — для проверки εbt,max ≤ εbt,ult (п. 8.1.29,
   /// формулы 8.52, 8.54 СП 63). Пусто, если в сечении есть арматура: там растянутый бетон
   /// за εbt2 трескается, и это не предельное состояние.
   /// </summary>
   IEnumerable<(double X, double Y, double EpsBt0, double EpsBt2)> PlainConcreteTensionVertices => [];
}

/// <summary>Проверка растянутого бетона бетонного сечения по п. 8.1.29–8.1.30 СП 63.</summary>
public static class PlainConcreteTension
{
   /// <summary>
   /// Наибольшее использование εbt / εbt,ult по вершинам; ≤ 1 — условие (8.52) выполнено.
   /// При двузначной эпюре εbt,ult = εbt2, при растяжении одного знака — по формуле (8.54)
   /// от отношения деформаций противоположных граней ε1/ε2 (|ε2| ≥ |ε1|).
   /// Возвращает 0, если растянутых вершин нет.
   /// </summary>
   public static double Utilization(IReadOnlyList<(double X, double Y, double EpsBt0, double EpsBt2)> vertices,
      Kurvature k, out double epsMax, out double epsUlt)
   {
      epsMax = 0; epsUlt = 0;
      if (vertices.Count == 0) return 0;

      double eMin = double.MaxValue, eMax = double.MinValue;
      foreach (var v in vertices)
      {
         double e = k.e0 + k.ky * v.Y + k.kz * v.X;
         if (e < eMin) eMin = e;
         if (e > eMax) eMax = e;
      }
      if (eMax <= 0) return 0;

      // Однозначная (только растяжение) эпюра — εbt,ult по (8.54).
      double ratio = eMin > 0 ? eMin / eMax : 0.0;
      double best = 0;
      foreach (var v in vertices)
      {
         double e = k.e0 + k.ky * v.Y + k.kz * v.X;
         if (e <= 0 || v.EpsBt2 <= 0) continue;
         double ult = v.EpsBt2 - (v.EpsBt2 - v.EpsBt0) * ratio;
         double u = e / ult;
         if (u > best) { best = u; epsMax = e; epsUlt = ult; }
      }
      return best;
   }
}
