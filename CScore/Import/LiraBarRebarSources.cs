using System.Globalization;
using CScore.Fem;
using CScore.PlateRebar;

namespace CScore.Import;

/// <summary>
/// Заданное армирование стержней ЛИРЫ: ТЗА КЭ (ячейка «Элементы - ТЗА») + брусовые ТЗА из .RBT
/// (ряды у нижней и верхней грани; у AS_spec низ — стержни с y &lt; 0, верх — с y &gt; 0, на оси — пополам).
/// Площади рядов одной грани из разных ТЗА складываются.
/// КЭ без ТЗА или с ТЗА, которых среди разобранных брусовых нет, — «нет данных»; грань без ряда — 0.
/// По длине КЭ заданное армирование постоянно.
/// </summary>
public sealed class LiraRbtBarRebarSource : IBarRebarFieldSource
{
   readonly Dictionary<string, (double Bottom, double Top)?> _byTag = new(StringComparer.Ordinal);

   /// <param name="rbt">Описания ТЗА.</param>
   /// <param name="typeIdsByTag">Номера ТЗА КЭ по тегу КЭ («16 20»).</param>
   public LiraRbtBarRebarSource(LiraRbtFile rbt, IEnumerable<KeyValuePair<string, string?>> typeIdsByTag)
   {
      var cache = new Dictionary<string, (double, double)?>(StringComparer.Ordinal);
      foreach (var (tag, cell) in typeIdsByTag)
      {
         string key = cell?.Trim() ?? "";
         if (!cache.TryGetValue(key, out var areas))
            cache[key] = areas = Assemble(key, rbt);
         _byTag[tag.Trim()] = areas;
      }
   }

   /// <summary>Хотя бы у одного КЭ армирование собрано.</summary>
   public bool HasBars => _byTag.Values.Any(v => v != null);

   static (double, double)? Assemble(string cell, LiraRbtFile rbt)
   {
      if (cell.Length == 0) return null;
      IReadOnlyList<int> ids;
      try { ids = LiraPlateReinforcementAssembler.ParseTypeIds(cell); }
      catch (FormatException) { return null; }
      if (ids.Count == 0) return null;

      double bottom = 0, top = 0;
      foreach (int id in ids.Distinct())
      {
         if (!rbt.BarTypes.TryGetValue(id, out var type)) return null;
         if (type.IsSpec)
         {
            // AS_spec: стержни ниже оси — низ, выше — верх, на оси — пополам.
            foreach (var s in type.Bars)
               if (s.YCm < 0) bottom += s.AreaCm2;
               else if (s.YCm > 0) top += s.AreaCm2;
               else { bottom += s.AreaCm2 / 2; top += s.AreaCm2 / 2; }
         }
         else if (type.Face == LiraBarRebarFace.Bottom) bottom += type.AreaCm2;
         else top += type.AreaCm2;
      }
      return (bottom, top);
   }

   /// <inheritdoc/>
   public bool Supports(BarRebarComponent component) =>
      component is BarRebarComponent.LongitudinalSum or BarRebarComponent.Bottom or BarRebarComponent.Top;

   /// <inheritdoc/>
   public PlateRebarValue Get(string elemTag, BarRebarComponent component)
   {
      if (!Supports(component) || !_byTag.TryGetValue(elemTag.Trim(), out var areas) || areas is not { } a)
         return PlateRebarValue.Missing;
      return PlateRebarValue.Of(component switch
      {
         BarRebarComponent.Bottom => a.Bottom,
         BarRebarComponent.Top => a.Top,
         _ => a.Bottom + a.Top,
      });
   }

   /// <inheritdoc/>
   public IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component) =>
      Get(elemTag, component) is { IsMissing: false } v ? [v] : [];
}

/// <summary>Подобранное ЛИРОЙ армирование стержней из *.asp как источник мозаики и эпюры (тег КЭ = номер КЭ ЛИРЫ).</summary>
public sealed class LiraAspBarRebarSource(LiraAspFile asp) : IBarRebarFieldSource
{
   /// <summary>В файле есть стержни.</summary>
   public bool HasBars => asp.Bars.Count > 0;

   /// <inheritdoc/>
   public bool Supports(BarRebarComponent component) =>
      component is not (BarRebarComponent.Bottom or BarRebarComponent.Top);

   /// <inheritdoc/>
   public PlateRebarValue Get(string elemTag, BarRebarComponent component)
   {
      if (!Supports(component) || Find(elemTag) is not { } bar) return PlateRebarValue.Missing;
      // Огибающая ЛИРЫ кодов «подбор не выполнен» не содержит — они только в сечениях.
      foreach (var section in bar.Sections)
         if (Value(section.Areas, component) is { FailureCode: not null } failure)
            return failure;
      return Value(bar.Envelope, component);
   }

   /// <inheritdoc/>
   public IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component) =>
      Supports(component) && Find(elemTag) is { } bar
         ? bar.Sections.Select(s => Value(s.Areas, component)).ToList() : [];

   LiraAspBar? Find(string elemTag) =>
      int.TryParse(elemTag.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
      && asp.Bars.TryGetValue(id, out var bar) ? bar : null;

   // ЛИРА пишет код «подбор не выполнен» в поле площади со знаком минус.
   static PlateRebarValue Value(LiraAspBarAreas a, BarRebarComponent component)
   {
      if (component == BarRebarComponent.LongitudinalSum)
      {
         foreach (double part in (double[])[a.Au1, a.Au2, a.Au3, a.Au4, a.As1, a.As2, a.As3, a.As4])
            if (part < 0) return PlateRebarValue.Failure((int)Math.Round(-part));
         return PlateRebarValue.Of(a.LongitudinalSum);
      }
      double v = component switch
      {
         BarRebarComponent.Au1 => a.Au1,
         BarRebarComponent.Au2 => a.Au2,
         BarRebarComponent.Au3 => a.Au3,
         BarRebarComponent.Au4 => a.Au4,
         BarRebarComponent.As1 => a.As1,
         BarRebarComponent.As2 => a.As2,
         BarRebarComponent.As3 => a.As3,
         BarRebarComponent.As4 => a.As4,
         BarRebarComponent.Asw1 => a.Asw1,
         BarRebarComponent.Asw2 => a.Asw2,
         _ => a.Percent,
      };
      return v < 0 ? PlateRebarValue.Failure((int)Math.Round(-v)) : PlateRebarValue.Of(v);
   }
}
