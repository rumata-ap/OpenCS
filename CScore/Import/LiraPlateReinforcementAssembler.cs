using CScore.PlateRebar;

namespace CScore.Import;

/// <summary>Суммарное армирование одного направления у одной грани КЭ.</summary>
/// <param name="AreaPerMeterCm2">Площадь на 1 п. м, см²/м.</param>
/// <param name="CentroidDistanceCm">Расстояние от грани до ц. т. арматуры, см (среднее по площади).</param>
/// <param name="DiameterMm">Эквивалентный диаметр стержней Σn·d²/Σn·d (п. 8.2.17 СП 63), мм;
/// 0 — слой задан суммарной площадью, диаметр неизвестен.</param>
public sealed record LiraPlateSlotReinforcement(double AreaPerMeterCm2, double CentroidDistanceCm, double DiameterMm = 0);

/// <summary>Армирование пластинчатого КЭ, собранное из назначенных ему ТЗА.</summary>
public sealed class LiraPlateElementReinforcement
{
   /// <summary>Армирование по слотам XT/XB/YT/YB (отсутствующие слоты — нет арматуры).</summary>
   public IReadOnlyDictionary<LiraPlateRebarSlot, LiraPlateSlotReinforcement> Slots { get; init; } =
      new Dictionary<LiraPlateRebarSlot, LiraPlateSlotReinforcement>();
   /// <summary>Номера ТЗА, которые не найдены среди пластинчатых (брусовые, отсутствующие в файле).</summary>
   public IReadOnlyList<int> MissingTypeIds { get; init; } = [];

   /// <summary>Нет ни одного слоя.</summary>
   public bool IsEmpty => Slots.Count == 0;

   /// <summary>
   /// Перевести в два слоя плитного сечения OpenCS: у грани Z+ (XT/YT) и у грани Z− (XB/YB).
   /// Площади — м²/м, <c>Zs</c> — м от срединной плоскости (плюс — к грани Z+).
   /// Слой без арматуры в обоих направлениях не создаётся.
   /// </summary>
   /// <param name="thicknessM">Толщина пластины, м.</param>
   /// <param name="topName">Имя слоя у грани Z+.</param>
   /// <param name="bottomName">Имя слоя у грани Z−.</param>
   public List<PlateRebarLayer> ToPlateRebarLayers(double thicknessM, string topName = "Z+", string bottomName = "Z-")
   {
      if (thicknessM <= 0) throw new ArgumentOutOfRangeException(nameof(thicknessM));
      var result = new List<PlateRebarLayer>();
      var top = MakeLayer(LiraPlateRebarSlot.XT, LiraPlateRebarSlot.YT, +1, thicknessM, topName, RebarFace.PlusN);
      if (top != null) result.Add(top);
      var bottom = MakeLayer(LiraPlateRebarSlot.XB, LiraPlateRebarSlot.YB, -1, thicknessM, bottomName, RebarFace.MinusN);
      if (bottom != null) result.Add(bottom);
      return result;
   }

   PlateRebarLayer? MakeLayer(LiraPlateRebarSlot sx, LiraPlateRebarSlot sy, int sign, double h, string name, RebarFace face)
   {
      Slots.TryGetValue(sx, out var x);
      Slots.TryGetValue(sy, out var y);
      if (x == null && y == null) return null;
      double half = h / 2.0;
      return new PlateRebarLayer
      {
         Name = name,
         InputMode = "direct",
         Asx = (x?.AreaPerMeterCm2 ?? 0) * 1e-4,
         Asy = (y?.AreaPerMeterCm2 ?? 0) * 1e-4,
         // Отсутствующее направление ставим на ту же отметку, что и имеющееся: площадь всё равно 0
         Zsx = sign * (half - (x ?? y)!.CentroidDistanceCm / 100.0),
         Zsy = sign * (half - (y ?? x)!.CentroidDistanceCm / 100.0),
         DiameterX = (x?.DiameterMm ?? 0) / 1000.0,
         DiameterY = (y?.DiameterMm ?? 0) / 1000.0,
         Face = face,
      };
   }
}

/// <summary>
/// Сборка армирования пластинчатого КЭ из списка ТЗА (таблица «Элементы - ТЗА»: несколько номеров
/// в одной ячейке через пробел — фон плюс усиления). Слои одного слота из разных ТЗА складываются
/// по площади, ц. т. — средневзвешенный по площади. Составные AS_mult раскрываются в составляющие.
/// </summary>
public static class LiraPlateReinforcementAssembler
{
   /// <summary>Разобрать ячейку таблицы «Элементы - ТЗА» (<c>"1 2 4"</c>) в список номеров.</summary>
   public static IReadOnlyList<int> ParseTypeIds(string? cell)
   {
      if (string.IsNullOrWhiteSpace(cell)) return [];
      var ids = new List<int>();
      foreach (var token in cell.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
      {
         if (!int.TryParse(token, out int id) || id <= 0)
            throw new FormatException($"Некорректный номер ТЗА «{token}» в ячейке «{cell}».");
         ids.Add(id);
      }
      return ids;
   }

   /// <summary>Собрать армирование КЭ из назначенных ему ТЗА.</summary>
   /// <param name="typeIds">Номера ТЗА КЭ.</param>
   /// <param name="types">Пластинчатые ТЗА из файла .RBT.</param>
   public static LiraPlateElementReinforcement Assemble(
      IEnumerable<int> typeIds, IReadOnlyDictionary<int, LiraPlateReinforcementType> types)
   {
      var area = new Dictionary<LiraPlateRebarSlot, double>();
      var moment = new Dictionary<LiraPlateRebarSlot, double>();
      var missing = new List<int>();
      var bars = new Dictionary<LiraPlateRebarSlot, (double Nd2, double Nd)>();

      foreach (int id in typeIds)
         Accumulate(id, types, area, moment, bars, missing, []);

      var slots = new Dictionary<LiraPlateRebarSlot, LiraPlateSlotReinforcement>();
      foreach (var (slot, a) in area)
         if (a > 0)
         {
            var b = bars.GetValueOrDefault(slot);
            slots[slot] = new LiraPlateSlotReinforcement(a, moment[slot] / a, b.Nd > 0 ? b.Nd2 / b.Nd : 0);
         }
      return new LiraPlateElementReinforcement { Slots = slots, MissingTypeIds = missing.Distinct().ToList() };
   }

   static void Accumulate(int id, IReadOnlyDictionary<int, LiraPlateReinforcementType> types,
      Dictionary<LiraPlateRebarSlot, double> area, Dictionary<LiraPlateRebarSlot, double> moment,
      Dictionary<LiraPlateRebarSlot, (double Nd2, double Nd)> bars, List<int> missing, HashSet<int> path)
   {
      if (!types.TryGetValue(id, out var type)) { missing.Add(id); return; }
      if (!path.Add(id)) throw new InvalidOperationException($"Циклическая ссылка в составном ТЗА {id}.");

      if (type.IsComposite)
         foreach (int c in type.ComponentIds)
            Accumulate(c, types, area, moment, bars, missing, path);

      foreach (var layer in type.Layers)
      {
         double a = layer.AreaPerMeterCm2;
         if (a <= 0) continue;
         area[layer.Slot] = area.GetValueOrDefault(layer.Slot) + a;
         moment[layer.Slot] = moment.GetValueOrDefault(layer.Slot) + a * layer.CentroidDistanceCm(type.Binding);
         if (layer.IsTotalArea) continue;
         var b = bars.GetValueOrDefault(layer.Slot);
         foreach (var t in layer.Terms)
         {
            // число стержней слагаемого на 1 п. м
            double n = t.SpacingMm > 0 ? t.Count * 1000.0 / t.SpacingMm : t.Count;
            b = (b.Nd2 + n * t.DiameterMm * t.DiameterMm, b.Nd + n * t.DiameterMm);
         }
         bars[layer.Slot] = b;
      }
      path.Remove(id);
   }
}
