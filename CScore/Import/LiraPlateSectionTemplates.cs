using System.Globalization;
using CScore.Fem;

namespace CScore.Import;

/// <summary>Сочетание «толщина + класс бетона + класс арматуры» пластин в подборе ЛИРЫ (*.asp).</summary>
/// <param name="ThicknessM">Толщина, м.</param>
/// <param name="ConcreteClass">Класс бетона, как записан в файле (B25 …).</param>
/// <param name="RebarClass">Класс продольной арматуры (A500 …).</param>
public sealed record LiraPlateCombo(double ThicknessM, string ConcreteClass, string RebarClass)
{
   /// <summary>Подпись сочетания: «h200 B25 A500».</summary>
   public string Label =>
      $"h{(ThicknessM * 1000).ToString("0.#", CultureInfo.InvariantCulture)} {ConcreteClass} {RebarClass}";
}

/// <summary>Арматура для граней без фонового ТЗА: привязка до ц. т. и диаметр.</summary>
/// <param name="CoverM">Расстояние от грани до ц. т. арматуры, м.</param>
/// <param name="DiameterM">Диаметр стержней, м.</param>
public sealed record LiraPlateNominalRebar(double CoverM, double DiameterM)
{
   /// <summary>Шаг стержней условной арматуры, м.</summary>
   public const double SpacingM = 0.2;
}

/// <summary>Сечение-шаблон цели, собранное по данным ЛИРЫ.</summary>
/// <param name="Combo">Преобладающее сочетание «толщина + классы» КЭ цели.</param>
/// <param name="Layers">Слои армирования шаблона: фоновые ТЗА и/или условная арматура.</param>
/// <param name="Tag">Имя сечения: «ЛИРА h200 B25 A500 · ТЗА 1».</param>
/// <param name="BackgroundTypeIds">ТЗА, назначенные каждому КЭ цели (фон); пусто — фона нет.</param>
/// <param name="NominalFaces">Грани с условной арматурой («Z+», «Z−»).</param>
/// <param name="Elements">КЭ цели, найденных в файле ASP.</param>
/// <param name="OtherCombos">Прочие сочетания в цели и число КЭ с ними.</param>
public sealed record LiraPlateTemplate(
   LiraPlateCombo Combo,
   List<PlateRebarLayer> Layers,
   string Tag,
   IReadOnlyList<int> BackgroundTypeIds,
   IReadOnlyList<string> NominalFaces,
   int Elements,
   IReadOnlyList<(LiraPlateCombo Combo, int Count)> OtherCombos);

/// <summary>Итог сборки шаблона: сам шаблон либо причина, по которой его нет.</summary>
/// <param name="Template">Шаблон; null — собрать не удалось.</param>
/// <param name="NeedsNominal">У грани нет фонового ТЗА — нужны привязка и диаметр от пользователя.</param>
/// <param name="Suggested">Значения для запроса: от фона другой грани либо типовые.</param>
/// <param name="Problem">Почему шаблона нет (когда дело не в условной арматуре).</param>
public sealed record LiraPlateTemplateResult(
   LiraPlateTemplate? Template, bool NeedsNominal, LiraPlateNominalRebar Suggested, string? Problem);

/// <summary>
/// Сечение-шаблон пластинчатой цели по данным ЛИРЫ: толщина и классы материалов — из подбора (*.asp),
/// фоновая арматура — ТЗА, общие для всех КЭ цели (*.RBT). Шаблон даёт проверке бетон, арматурную сталь
/// и привязку слоёв; армирование отдельного КЭ подставляют источники <see cref="LiraAssignedPlateSectionSource"/>
/// и <see cref="LiraSelectedPlateSectionSource"/>.
/// </summary>
public static class LiraPlateSectionTemplates
{
   /// <summary>Типовые привязка и диаметр для запроса, когда взять их неоткуда.</summary>
   public static readonly LiraPlateNominalRebar DefaultNominal = new(0.04, 0.01);

   /// <summary>Толщина пластины из ASP, м: в файле — float32, округляется до 0,1 мм.</summary>
   public static double Thickness(LiraAspPlate plate) => Math.Round(plate.ThicknessM, 4);

   /// <summary>Собрать шаблон цели.</summary>
   /// <param name="plates">Пластинчатые КЭ цели.</param>
   /// <param name="asp">Файл подбора ЛИРЫ.</param>
   /// <param name="rbt">Файл ТЗА; null — не приложен.</param>
   /// <param name="nominal">Арматура для граней без фона; null — не запрошена.</param>
   public static LiraPlateTemplateResult Build(
      IReadOnlyList<FemCheckScopeElement> plates, LiraAspFile asp, LiraRbtFile? rbt, LiraPlateNominalRebar? nominal)
   {
      var combos = new Dictionary<LiraPlateCombo, int>();
      foreach (var e in plates)
         if (e.ElemNum is int num && asp.Plates.TryGetValue(num, out var p) && p.ThicknessM > 0)
         {
            var combo = new LiraPlateCombo(Thickness(p), p.ConcreteClass.Trim(), p.RebarClass.Trim());
            combos[combo] = combos.GetValueOrDefault(combo) + 1;
         }
      if (combos.Count == 0)
         return new(null, false, DefaultNominal, "КЭ цели нет в файле подбора ЛИРЫ (ASP)");

      var ordered = combos.OrderByDescending(c => c.Value).ThenBy(c => c.Key.ThicknessM).ToList();
      var main = ordered[0].Key;
      if (main.ConcreteClass.Length == 0 || main.RebarClass.Length == 0)
         return new(null, false, DefaultNominal, "в файле подбора ЛИРЫ (ASP) не указаны классы материалов");

      var background = BackgroundTypeIds(plates, rbt);
      var layers = new List<PlateRebarLayer>();
      if (background.Count > 0)
      {
         var assembled = LiraPlateReinforcementAssembler.Assemble(background, rbt!.PlateTypes);
         if (!assembled.IsEmpty)
            layers = assembled.ToPlateRebarLayers(main.ThicknessM);
      }

      var top = layers.FirstOrDefault(l => l.Zsx > 0);
      var bottom = layers.FirstOrDefault(l => l.Zsx < 0);
      var suggested = Suggest(top ?? bottom, main.ThicknessM);
      var nominalFaces = new List<string>();
      if (top == null || bottom == null)
      {
         if (nominal == null)
            return new(null, true, suggested, null);
         if (nominal.CoverM <= 0 || nominal.CoverM >= main.ThicknessM / 2 || nominal.DiameterM <= 0)
            return new(null, true, suggested, "привязка должна быть больше нуля и меньше половины толщины, диаметр — больше нуля");
         if (top == null) { layers.Insert(0, NominalLayer("Z+", +1, main.ThicknessM, nominal)); nominalFaces.Add("Z+"); }
         if (bottom == null) { layers.Add(NominalLayer("Z-", -1, main.ThicknessM, nominal)); nominalFaces.Add("Z−"); }
      }

      var inv = CultureInfo.InvariantCulture;
      var parts = new List<string>(2);
      if (background.Count > 0 && nominalFaces.Count < 2)
         parts.Add("ТЗА " + string.Join(' ', background));
      if (nominalFaces.Count > 0)
         parts.Add($"d{(nominal!.DiameterM * 1000).ToString("0.#", inv)} a{(nominal.CoverM * 1000).ToString("0.#", inv)}");
      string tag = $"ЛИРА {main.Label} · {string.Join(" + ", parts)}";

      return new(new LiraPlateTemplate(main, layers, tag, nominalFaces.Count < 2 ? background : [], nominalFaces,
         combos.Values.Sum(), [.. ordered.Skip(1).Select(c => (c.Key, c.Value))]), false, suggested, null);
   }

   /// <summary>
   /// Фон: ТЗА, назначенные каждому КЭ цели, у которого ТЗА есть вообще, и описанные в файле RBT.
   /// </summary>
   static List<int> BackgroundTypeIds(IReadOnlyList<FemCheckScopeElement> plates, LiraRbtFile? rbt)
   {
      if (rbt == null) return [];
      HashSet<int>? common = null;
      // Наборов ТЗА на схеме десятки, КЭ тысячи — разбираем каждую ячейку один раз.
      foreach (string cell in plates.Select(e => e.Element.ReinforcementTypeIds?.Trim() ?? "").Distinct(StringComparer.Ordinal))
      {
         if (cell.Length == 0) continue;
         IReadOnlyList<int> ids;
         try { ids = LiraPlateReinforcementAssembler.ParseTypeIds(cell); }
         catch (FormatException) { continue; }
         if (common == null) common = [.. ids];
         else common.IntersectWith(ids);
         if (common.Count == 0) break;
      }
      return common == null ? [] : [.. common.Where(rbt.PlateTypes.ContainsKey).Order()];
   }

   /// <summary>Значения для запроса условной арматуры: как у фона другой грани, иначе типовые.</summary>
   static LiraPlateNominalRebar Suggest(PlateRebarLayer? other, double thicknessM)
   {
      if (other == null) return DefaultNominal;
      double cover = thicknessM / 2 - Math.Abs(other.Asx > 0 ? other.Zsx : other.Zsy);
      double d = other.DiameterX > 0 ? other.DiameterX : other.DiameterY;
      return new(cover > 0 ? Math.Round(cover, 4) : DefaultNominal.CoverM, d > 0 ? d : DefaultNominal.DiameterM);
   }

   static PlateRebarLayer NominalLayer(string name, int sign, double h, LiraPlateNominalRebar nominal)
   {
      double z = sign * (h / 2 - nominal.CoverM);
      var layer = new PlateRebarLayer
      {
         Name = name,
         InputMode = "diameter_spacing",
         DiameterX = nominal.DiameterM, DiameterY = nominal.DiameterM,
         SpacingX = LiraPlateNominalRebar.SpacingM, SpacingY = LiraPlateNominalRebar.SpacingM,
         Zsx = z, Zsy = z,
         Face = sign > 0 ? PlateRebar.RebarFace.PlusN : PlateRebar.RebarFace.MinusN,
      };
      layer.RecalcArea();
      return layer;
   }
}
