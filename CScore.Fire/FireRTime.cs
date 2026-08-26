using CScore;

namespace CScore.Fire;

/// <summary>Одна точка sweep по времени.</summary>
public sealed record FireRTimeRow(
   int SnapshotIndex, double TimeMin, double Factor, string Governing, bool Converged);

/// <summary>Результат поиска собственного предела огнестойкости.</summary>
/// <param name="RMin">Момент потери несущей способности, мин; null, если предел не достигнут.</param>
/// <param name="RMinLowerBound">Нижняя граница предела, если он не достигнут за время расчёта.</param>
/// <param name="LimitNotReached">Условие (8.1) выполнялось во всех доступных снимках.</param>
/// <param name="FailedAtStart">Условие (8.1) нарушено уже в первом снимке.</param>
/// <param name="NonMonotone">После первого отказа factor где-то снова поднялся выше единицы.</param>
/// <param name="UnreliableSnapshots">Индексы снимков, где решатель не сошёлся.</param>
/// <param name="Refinement">Способ уточнения момента: none или linear_between_snapshots.</param>
/// <param name="BracketMin">Левая граница интервала уточнения, мин.</param>
/// <param name="BracketMax">Правая граница интервала уточнения, мин.</param>
/// <param name="Rows">Полная таблица sweep для графика.</param>
public sealed record FireRTimeResult(
   double? RMin,
   double? RMinLowerBound,
   bool LimitNotReached,
   bool FailedAtStart,
   bool NonMonotone,
   IReadOnlyList<int> UnreliableSnapshots,
   string Refinement,
   double? BracketMin,
   double? BracketMax,
   IReadOnlyList<FireRTimeRow> Rows)
{
   /// <summary>Есть хотя бы один снимок без надёжного результата решателя.</summary>
   public bool HasUnreliableSnapshots => UnreliableSnapshots.Count != 0;

   /// <summary>Есть два или более подряд снимка без надёжного результата.</summary>
   public bool HasConsecutiveUnreliableSnapshots
   {
      get
      {
         for (int i = 1; i < UnreliableSnapshots.Count; i++)
            if (UnreliableSnapshots[i] == UnreliableSnapshots[i - 1] + 1)
               return true;
         return false;
      }
   }
}

/// <summary>
/// Собственный предел огнестойкости по п. 8.5 СП 468: последовательные приближения
/// по заданным длительностям стандартного режима пожара.
/// </summary>
public static class FireRTime
{
   /// <summary>Полный sweep по снимкам теплового расчёта.</summary>
   public static FireRTimeResult Run(
      FireThermalResult thermal,
      CrossSection section,
      double n,
      double mx,
      double my,
      CalcType calc = CalcType.C,
      bool refine = true,
      double sp63EtaMin = 0.85,
      bool rebarDifferentialDiagram = true,
      IReadOnlyList<Diagramm>? diagramPool = null,
      double ekbEtaMin = 0.05)
   {
      ArgumentNullException.ThrowIfNull(thermal);
      ArgumentNullException.ThrowIfNull(section);
      if (thermal.Snapshots.Length == 0)
         throw new InvalidOperationException("В тепловом результате нет снимков температуры.");
      if (thermal.TimesMin.Length != thermal.Snapshots.Length)
         throw new ArgumentException("Число временных отметок не совпадает с числом температурных снимков.", nameof(thermal));

      section.ResolveAndBuildDiagramms(sp63EtaMin, diagramPool, rebarDifferentialDiagram, ekbEtaMin);

      // Сечение строится один раз: между снимками меняются только температурные
      // коэффициенты, геометрия и привязка арматуры остаются теми же.
      var fiber = FireFiberSection.FromThermalResult(thermal, section, 0);

      var rows = new List<FireRTimeRow>(thermal.Snapshots.Length);
      for (int i = 0; i < thermal.Snapshots.Length; i++)
      {
         fiber.SetSnapshot(i);

         double factor;
         string governing;
         bool converged;
         try
         {
            var solver = new LimitForceSolver(fiber, fiber.SourceSection, calc);
            LimitForceResult res = solver.AllFactor(n, mx, my);
            factor = res.Factor;
            governing = res.Governing ?? "";
            converged = res.Converged && double.IsFinite(factor);
         }
         catch (Exception)
         {
            factor = double.NaN;
            governing = "";
            converged = false;
         }

         rows.Add(new FireRTimeRow(i, thermal.TimesMin[i], factor, governing, converged));
      }

      return Analyse(rows, refine);
   }

   /// <summary>Разбор готовой таблицы factor(t); используется тестами и внутренним кодом.</summary>
   public static FireRTimeResult FromFactors(double[] timesMin, double[] factors, bool refine)
   {
      ArgumentNullException.ThrowIfNull(timesMin);
      ArgumentNullException.ThrowIfNull(factors);
      if (timesMin.Length != factors.Length)
         throw new ArgumentException("Массивы времени и factor должны иметь одинаковую длину.");
      if (timesMin.Length == 0)
         throw new InvalidOperationException("Пустая история значений factor(t).");

      var rows = new List<FireRTimeRow>(timesMin.Length);
      for (int i = 0; i < timesMin.Length; i++)
         rows.Add(new FireRTimeRow(i, timesMin[i], factors[i], "", double.IsFinite(factors[i])));

      return Analyse(rows, refine);
   }

   static FireRTimeResult Analyse(List<FireRTimeRow> rows, bool refine)
   {
      var unreliable = rows.Where(r => !r.Converged).Select(r => r.SnapshotIndex).ToList();
      var usable = rows.Where(r => r.Converged).ToList();

      if (usable.Count == 0)
         return new FireRTimeResult(null, null, false, false, false, unreliable, "none", null, null, rows);

      // Начальная проверка относится именно к начальному снимку. Если он
      // ненадёжен, это не даёт права объявлять отказ при начальной температуре.
      if (rows[0].Converged && rows[0].Factor < 1.0)
      {
         return new FireRTimeResult(
            0.0, null, false, true, false, unreliable, "none", null, null, rows);
      }

      int crossing = -1;
      for (int i = 1; i < usable.Count; i++)
      {
         if (usable[i].Factor < 1.0) { crossing = i; break; }
      }

      if (crossing < 0)
      {
         return new FireRTimeResult(
            null, usable[^1].TimeMin, true, false, false, unreliable, "none", null, null, rows);
      }

      // Немонотонность: после первого отказа factor где-то снова поднялся выше единицы.
      bool nonMonotone = usable.Skip(crossing + 1).Any(r => r.Factor >= 1.0);

      var before = usable[crossing - 1];
      var after = usable[crossing];

      if (!refine || after.TimeMin <= before.TimeMin)
      {
         return new FireRTimeResult(
            after.TimeMin, null, false, false, nonMonotone, unreliable,
            "none", before.TimeMin, after.TimeMin, rows);
      }

      // Линейная интерполяция factor(t) внутри интервала — инженерная оценка
      // между дискретными снимками, а не следствие п. 8.5.
      double df = before.Factor - after.Factor;
      double t = df > 0.0
         ? before.TimeMin + (before.Factor - 1.0) / df * (after.TimeMin - before.TimeMin)
         : after.TimeMin;
      t = Math.Round(Math.Clamp(t, before.TimeMin, after.TimeMin), 1, MidpointRounding.AwayFromZero);

      return new FireRTimeResult(
         t, null, false, false, nonMonotone, unreliable,
         "linear_between_snapshots", before.TimeMin, after.TimeMin, rows);
   }
}
