using System.Globalization;
using CScore.PlateRebar;

namespace CScore.Import;

/// <summary>Подобранное ЛИРОЙ армирование пластин из *.asp как источник мозаики (тег КЭ = номер КЭ ЛИРЫ).</summary>
public sealed class LiraAspPlateRebarSource(LiraAspFile asp) : IPlateRebarFieldSource
{
   /// <inheritdoc/>
   public bool Supports(PlateRebarMosaicComponent component) => true;

   /// <inheritdoc/>
   public PlateRebarValue Get(string elemTag, PlateRebarMosaicComponent component)
   {
      if (!int.TryParse(elemTag.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
          || !asp.Plates.TryGetValue(id, out var p))
         return PlateRebarValue.Missing;
      double v = component switch
      {
         PlateRebarMosaicComponent.BottomX => p.As1,
         PlateRebarMosaicComponent.TopX => p.As2,
         PlateRebarMosaicComponent.BottomY => p.As3,
         PlateRebarMosaicComponent.TopY => p.As4,
         _ => p.Asw,
      };
      // ЛИРА пишет код «подбор не выполнен» в поле площади со знаком минус.
      return v < 0 ? PlateRebarValue.Failure((int)Math.Round(-v)) : PlateRebarValue.Of(v);
   }
}

/// <summary>
/// Заданное армирование пластин ЛИРЫ: ТЗА КЭ (ячейка «Элементы - ТЗА») + описания ТЗА из .RBT.
/// КЭ без ТЗА или с ТЗА, которых нет в файле, — «нет данных»; слот, которого нет в ТЗА, — 0.
/// </summary>
public sealed class LiraRbtPlateRebarSource : IPlateRebarFieldSource
{
   readonly Dictionary<string, LiraPlateElementReinforcement?> _byTag = new(StringComparer.Ordinal);

   /// <param name="rbt">Описания ТЗА.</param>
   /// <param name="typeIdsByTag">Номера ТЗА КЭ по тегу КЭ («1 2 4»).</param>
   public LiraRbtPlateRebarSource(LiraRbtFile rbt, IEnumerable<KeyValuePair<string, string?>> typeIdsByTag)
   {
      // Одинаковые наборы ТЗА собираются один раз: на реальной схеме наборов единицы, КЭ тысячи.
      var cache = new Dictionary<string, LiraPlateElementReinforcement?>(StringComparer.Ordinal);
      foreach (var (tag, cell) in typeIdsByTag)
      {
         string key = cell?.Trim() ?? "";
         if (!cache.TryGetValue(key, out var r))
         {
            r = Assemble(key, rbt);
            cache[key] = r;
         }
         _byTag[tag.Trim()] = r;
      }
   }

   static LiraPlateElementReinforcement? Assemble(string cell, LiraRbtFile rbt)
   {
      IReadOnlyList<int> ids;
      try { ids = LiraPlateReinforcementAssembler.ParseTypeIds(cell); }
      catch (FormatException) { return null; }
      if (ids.Count == 0) return null;
      try
      {
         var r = LiraPlateReinforcementAssembler.Assemble(ids, rbt.PlateTypes);
         return r.MissingTypeIds.Count > 0 ? null : r;
      }
      catch (InvalidOperationException) { return null; }
   }

   /// <inheritdoc/>
   public bool Supports(PlateRebarMosaicComponent component) => component != PlateRebarMosaicComponent.Transverse;

   /// <inheritdoc/>
   public PlateRebarValue Get(string elemTag, PlateRebarMosaicComponent component)
   {
      if (!Supports(component) || !_byTag.TryGetValue(elemTag.Trim(), out var r) || r == null)
         return PlateRebarValue.Missing;
      var slot = component switch
      {
         PlateRebarMosaicComponent.BottomX => LiraPlateRebarSlot.XB,
         PlateRebarMosaicComponent.TopX => LiraPlateRebarSlot.XT,
         PlateRebarMosaicComponent.BottomY => LiraPlateRebarSlot.YB,
         _ => LiraPlateRebarSlot.YT,
      };
      return PlateRebarValue.Of(r.Slots.TryGetValue(slot, out var s) ? s.AreaPerMeterCm2 : 0);
   }
}
