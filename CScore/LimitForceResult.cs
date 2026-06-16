namespace CScore;

/// <summary>
/// Результат поиска предельного коэффициента нагружения.
/// </summary>
public sealed class LimitForceResult
{
   /// <summary>Коэффициент масштаба k для вектора нагрузок.</summary>
   public double Factor { get; set; }

   /// <summary>Коэффициент использования 1 / k.</summary>
   public double Utilization { get; set; }

   /// <summary>Признак достижения критерия остановки бисекции.</summary>
   public bool Converged { get; set; }

   /// <summary>Число шагов бисекции.</summary>
   public int Iterations { get; set; }

   /// <summary>Суммарное число итераций Ньютона во всех вызовах решателя.</summary>
   public int NewtonIterations { get; set; }

   /// <summary>Найденная плоскость деформаций для предельной точки.</summary>
   public Kurvature? StrainPlane { get; set; }

   /// <summary>Предельная продольная сила, кН.</summary>
   public double NLimit { get; set; }

   /// <summary>Предельный изгибающий момент относительно оси X, кН·м.</summary>
   public double MxLimit { get; set; }

   /// <summary>Предельный изгибающий момент относительно оси Y, кН·м.</summary>
   public double MyLimit { get; set; }

   /// <summary>Минимальная деформация по вершинам контура (наиболее сжатая).</summary>
   public double EpsContourMin { get; set; }

   /// <summary>Предельная сжимаемость бетона контура.</summary>
   public double EpsCu { get; set; }

   /// <summary>Максимальная деформация арматуры (наиболее растянутый стержень).</summary>
   public double? EpsRebarMax { get; set; }

   /// <summary>Предельная растяжимость арматуры.</summary>
   public double? EpsSu { get; set; }

   /// <summary>Управляющий критерий: concrete | rebar | both | none.</summary>
   public string Governing { get; set; } = "none";
}
