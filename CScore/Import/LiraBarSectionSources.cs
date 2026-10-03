using System.Globalization;
using CScore.Fem;

namespace CScore.Import;

/// <summary>Стержень арматуры в осях сечения OpenCS: x — вдоль местной оси Y1 стержня ЛИРЫ, y — вдоль Z1.</summary>
/// <param name="X">Абсцисса, м.</param>
/// <param name="Y">Ордината, м.</param>
/// <param name="AreaM2">Площадь, м².</param>
/// <param name="DiameterM">Диаметр (у распределённой арматуры — эквивалентный), м.</param>
public readonly record struct LiraBarPoint(double X, double Y, double AreaM2, double DiameterM);

/// <summary>Размеры и материалы сечения стержневого КЭ по данным ЛИРЫ.</summary>
/// <param name="StiffnessNum">Номер жёсткости КЭ.</param>
/// <param name="WidthM">Ширина B (вдоль Y1), м.</param>
/// <param name="HeightM">Высота H (вдоль Z1), м.</param>
/// <param name="Concrete">Бетон.</param>
/// <param name="Rebar">Продольная арматура.</param>
public sealed record LiraBarProfile(int StiffnessNum, double WidthM, double HeightM, Material Concrete, Material Rebar)
{
   /// <summary>Сечение по профилю жёсткости <paramref name="profile"/> и материалам.</summary>
   public static LiraBarProfile From(ImportedBarProfile profile, Material concrete, Material rebar) =>
      new(profile.StiffnessNum, profile.WidthM, profile.HeightM, concrete, rebar);
}

/// <summary>
/// Общие данные источников сечения стержня по данным ЛИРЫ: размеры — из жёсткости КЭ (таблица «Жёсткости»),
/// материалы — по классам из подбора (*.asp) среди материалов проекта, а у КЭ без подбора — материалы
/// сечения, назначенного ему в проекте.
/// </summary>
/// <param name="stiffnesses">Жёсткости схемы по номеру.</param>
/// <param name="asp">Файл подбора ЛИРЫ; null — не приложен.</param>
/// <param name="concreteByClass">Бетон проекта по классу («B25»); null — такого нет.</param>
/// <param name="rebarByClass">Арматура проекта по классу («A500»); null — такой нет.</param>
/// <param name="projectSection">Сечение, назначенное КЭ в проекте (своё, элемента или цели).</param>
public sealed class LiraBarSectionContext(
   IReadOnlyDictionary<int, LiraStiffnessRecord> stiffnesses,
   LiraAspFile? asp,
   Func<string, Material?> concreteByClass,
   Func<string, Material?> rebarByClass,
   Func<FemCheckScopeElement, CrossSection?>? projectSection = null)
{
   readonly Dictionary<(int Stiffness, string Concrete, string Rebar), (LiraBarProfile? Profile, string? Reason)> _cache = [];

   /// <summary>Файл подбора ЛИРЫ.</summary>
   public LiraAspFile? Asp => asp;

   /// <summary>Подбор стержня в файле ASP; null — файла нет или КЭ в нём нет.</summary>
   public LiraAspBar? AspBar(FemCheckScopeElement element) =>
      asp != null && element.ElemNum is int num && asp.Bars.TryGetValue(num, out var bar) ? bar : null;

   /// <summary>Размеры и материалы сечения КЭ либо причина, по которой их нет.</summary>
   public (LiraBarProfile? Profile, string? Reason) Profile(FemCheckScopeElement element)
   {
      if (element.Element.StiffnessNum is not int num)
         return (null, "у КЭ нет номера жёсткости (меню схемы «Обновить жёсткости элементов из ЛИРЫ (API)»)");

      var bar = AspBar(element);
      if (bar != null && bar.ConcreteClass.Length > 0 && bar.RebarClass.Length > 0)
      {
         var key = (num, bar.ConcreteClass, bar.RebarClass);
         if (!_cache.TryGetValue(key, out var cached))
            _cache[key] = cached = Build(num, bar.ConcreteClass, bar.RebarClass);
         return cached;
      }

      // КЭ без подбора: материалы сечения, назначенного ему в проекте.
      var (shape, shapeReason) = Shape(num);
      if (shape == null) return (null, shapeReason);
      var own = projectSection?.Invoke(element);
      var concrete = own?.Areas.FirstOrDefault(a => a.Category == AreaCategory.Region && a.Material?.Type == MatType.Concrete)?.Material;
      var rebar = own?.Areas.FirstOrDefault(a => a.Category == AreaCategory.RebarGroup && a.Material != null)?.Material;
      if (concrete == null || rebar == null)
         return (null, "классы материалов неизвестны: КЭ нет в файле ASP, а у цели нет сечения с бетоном и арматурой");
      return (LiraBarProfile.From(shape, concrete, rebar), null);
   }

   (ImportedBarProfile? Shape, string? Reason) Shape(int num) =>
      ImportedBarProfiles.Resolve(stiffnesses, num, scad: false);

   (LiraBarProfile?, string?) Build(int num, string concreteClass, string rebarClass)
   {
      var (shape, shapeReason) = Shape(num);
      if (shape == null) return (null, shapeReason);
      if (concreteByClass(concreteClass) is not { } concrete)
         return (null, $"в проекте нет бетона {concreteClass} (кнопка «Создать материалы по данным ЛИРЫ» в диалоге проверки)");
      if (rebarByClass(rebarClass) is not { } rebar)
         return (null, $"в проекте нет арматуры {rebarClass} (кнопка «Создать материалы по данным ЛИРЫ» в диалоге проверки)");
      return (LiraBarProfile.From(shape, concrete, rebar), null);
   }
}

/// <summary>
/// Сборка расчётного сечения стержня по данным ЛИРЫ: прямоугольный бетон и точечная арматура.
/// Оси сечения: x — вдоль местной оси Y1 стержня (ширина B), y — вдоль Z1 (высота H); «нижняя» грань
/// ЛИРЫ — y &lt; 0, «левая» — x &lt; 0. В этих осях импорт пишет усилия: <c>Mx</c> — момент My ЛИРЫ,
/// <c>My</c> — момент Mz (<see cref="LiraForceMapper.MapBar"/>).
/// </summary>
public static class LiraBarSectionBuilder
{
   /// <summary>Число точек, которыми заменяется арматура, распределённая вдоль грани (AS1..AS4 подбора).</summary>
   public const int DistributedPoints = 9;

   /// <summary>Сечение: бетон <paramref name="profile"/> и арматура <paramref name="bars"/>.</summary>
   public static CrossSection Build(string tag, LiraBarProfile profile, IEnumerable<LiraBarPoint> bars)
   {
      var points = TemplatePoints.RectPoints(profile.WidthM, profile.HeightM);
      points.Add(points[0]);
      var concrete = new MaterialArea { Tag = "Бетон", Category = AreaCategory.Region };
      concrete.Hull = new Contour(points.Select(p => p.X), points.Select(p => p.Y), "контур бетона") { Type = ContourType.Hull };
      concrete.SetWKT();
      concrete.SetMaterial(profile.Concrete, DiagrammType.L2);

      var section = new CrossSection { Tag = tag, Areas = [concrete] };
      var rebar = new MaterialArea
      {
         Tag = "Арматура", Category = AreaCategory.RebarGroup, HostArea = concrete, HostAreaId = concrete.Id,
      };
      foreach (var b in bars)
         if (b.AreaM2 > 1e-12)
            rebar.Fibers.Add(new Fiber(b.X, b.Y) { TypeFiber = FiberType.point, Area = b.AreaM2, Diameter = b.DiameterM });
      if (rebar.Fibers.Count > 0)
      {
         rebar.SetMaterial(profile.Rebar, DiagrammType.L2);
         section.Areas.Add(rebar);
      }
      section.ResolveAndBuildDiagramms();
      return section;
   }

   /// <summary>
   /// Раскладка подобранной арматуры: AU1..AU4 — в углах (лев. нижн., прав. нижн., лев. верхн., прав. верхн.),
   /// AS1 и AS2 — вдоль нижней и верхней грани между угловыми, AS3 и AS4 — вдоль левой и правой грани.
   /// Привязки до ц. т.: a1 — нижней, a2 — верхней, a3 — боковой арматуры (материалы ЛИРЫ, см).
   /// Сверено с подбором ЛИРЫ на схеме 1-lin: с полной арматурой НДС находится у всех 438 сечений балок,
   /// при 0,9 от неё — не находится у 73.
   /// </summary>
   /// <returns>Стержни либо причина, по которой раскладка невозможна.</returns>
   public static (List<LiraBarPoint>? Bars, string? Reason) SelectedLayout(
      LiraAspBarAreas areas, LiraBarProfile profile, (double A1, double A2, double A3) coversCm)
   {
      if (areas.FailureCode is int code)
         return (null, $"подбор ЛИРЫ не выполнен (код {code})");

      double b = profile.WidthM, h = profile.HeightM;
      double a1 = coversCm.A1 / 100, a2 = coversCm.A2 / 100, a3 = coversCm.A3 / 100;
      if (!(a1 > 0 && a2 > 0 && a3 > 0) || a1 + a2 >= h || 2 * a3 >= b)
         return (null, string.Format(CultureInfo.InvariantCulture,
            "привязки арматуры {0:0.#}/{1:0.#}/{2:0.#} см не помещаются в сечение {3:0.#}×{4:0.#} см",
            coversCm.A1, coversCm.A2, coversCm.A3, b * 100, h * 100));

      double left = -b / 2 + a3, right = b / 2 - a3, bottom = -h / 2 + a1, top = h / 2 - a2;
      var bars = new List<LiraBarPoint>(4 + 4 * DistributedPoints);
      void Corner(double x, double y, double cm2) { if (cm2 > 0) bars.Add(Point(x, y, cm2 * 1e-4)); }
      Corner(left, bottom, areas.Au1);
      Corner(right, bottom, areas.Au2);
      Corner(left, top, areas.Au3);
      Corner(right, top, areas.Au4);

      for (int i = 1; i <= DistributedPoints; i++)
      {
         double t = (double)i / (DistributedPoints + 1);
         double x = left + t * (right - left), y = bottom + t * (top - bottom);
         void Spread(double px, double py, double cm2) { if (cm2 > 0) bars.Add(Point(px, py, cm2 * 1e-4 / DistributedPoints)); }
         Spread(x, bottom, areas.As1);
         Spread(x, top, areas.As2);
         Spread(left, y, areas.As3);
         Spread(right, y, areas.As4);
      }
      return (bars, null);
   }

   /// <summary>
   /// Раскладка заданной арматуры: ряды простых брусовых ТЗА у нижней и верхней грани, стержни равномерно
   /// между боковыми привязками (один стержень — по оси сечения).
   /// </summary>
   /// <returns>Стержни либо причина, по которой раскладка невозможна.</returns>
   public static (List<LiraBarPoint>? Bars, string? Reason) AssignedLayout(
      IEnumerable<LiraBarReinforcementType> types, LiraBarProfile profile)
   {
      double b = profile.WidthM, h = profile.HeightM;
      var bars = new List<LiraBarPoint>();
      foreach (var type in types)
      {
         if (type.Count < 1 || !(type.BarAreaCm2 > 0)) continue;
         double d = type.DiameterMm / 1000;
         // Привязка «защитный слой» — до грани стержня, прочие — до его центра.
         double extra = type.Binding == LiraRebarBinding.Cover ? d / 2 : 0;
         double a = type.A / 100 + extra, side = type.ASide / 100 + extra;
         if (!(a > 0) || a >= h || !(side > 0) || 2 * side > b)
            return (null, $"ТЗА {type.Id}: привязки арматуры не помещаются в сечение");

         double y = type.Face == LiraBarRebarFace.Bottom ? -h / 2 + a : h / 2 - a;
         double left = -b / 2 + side, right = b / 2 - side;
         for (int i = 0; i < type.Count; i++)
         {
            double x = type.Count == 1 ? 0 : left + (right - left) * i / (type.Count - 1);
            bars.Add(new LiraBarPoint(x, y, type.BarAreaCm2 * 1e-4, d));
         }
      }
      return bars.Count > 0 ? (bars, null) : (null, "в ТЗА нет арматуры");
   }

   static LiraBarPoint Point(double x, double y, double areaM2) =>
      new(x, y, areaM2, Math.Sqrt(4 * areaM2 / Math.PI));

   /// <summary>Подпись размеров: «200×400».</summary>
   public static string SizeLabel(LiraBarProfile profile) => string.Format(CultureInfo.InvariantCulture,
      "{0:0.#}×{1:0.#}", profile.WidthM * 1000, profile.HeightM * 1000);
}

/// <summary>
/// Источник <see cref="FemCheckRebarSource.Selected"/> для стержней: сечение КЭ из подобранной ЛИРОЙ
/// арматуры (*.asp) — своё для каждого сечения КЭ; у строки без номера сечения — огибающая по сечениям.
/// </summary>
public sealed class LiraSelectedBarSectionSource(LiraBarSectionContext context) : IBarElementSectionSource
{
   const string Label = "ASP";

   readonly Dictionary<(int Elem, int Section), BarElementSection> _cache = [];

   /// <inheritdoc/>
   public string Key => FemCheckRebarSource.Selected;

   /// <inheritdoc/>
   public bool PerSection => true;

   /// <inheritdoc/>
   public string? MissingReason(FemCheckScopeElement element) =>
      context.AspBar(element) == null ? "КЭ нет в файле ASP" : context.Profile(element).Reason;

   /// <inheritdoc/>
   public BarElementSection Resolve(FemCheckScopeElement element, int? sectionNum)
   {
      if (context.AspBar(element) is not { } bar)
         return BarElementSection.Missing("КЭ нет в файле ASP", Label);
      var key = (bar.ElementId, sectionNum ?? 0);
      if (!_cache.TryGetValue(key, out var result))
         _cache[key] = result = Build(element, bar, sectionNum);
      return result;
   }

   BarElementSection Build(FemCheckScopeElement element, LiraAspBar bar, int? sectionNum)
   {
      var (profile, reason) = context.Profile(element);
      if (profile == null) return BarElementSection.Missing(reason!, Label);

      LiraAspBarAreas areas;
      if (sectionNum is int n)
      {
         if (n < 1 || n > bar.Sections.Count)
            return BarElementSection.Missing($"в файле ASP у КЭ нет сечения {n} (сечений: {bar.Sections.Count})", Label);
         areas = bar.Sections[n - 1].Areas;
      }
      else
      {
         // Огибающая ЛИРЫ кодов «подбор не выполнен» не содержит — они только в сечениях.
         areas = bar.Sections.Select(s => s.Areas).FirstOrDefault(a => a.FailureCode != null) ?? bar.Envelope;
      }

      var (bars, layoutReason) = LiraBarSectionBuilder.SelectedLayout(areas, profile,
         (bar.Covers.A1, bar.Covers.A2, bar.Covers.A3));
      if (bars == null) return BarElementSection.Missing(layoutReason!, Label);

      string tag = sectionNum is int s
         ? $"ASP {LiraBarSectionBuilder.SizeLabel(profile)} э.{bar.ElementId} с{s}"
         : $"ASP {LiraBarSectionBuilder.SizeLabel(profile)} э.{bar.ElementId}";
      return new BarElementSection(LiraBarSectionBuilder.Build(tag, profile, bars), Label, null);
   }
}

/// <summary>
/// Источник <see cref="FemCheckRebarSource.Assigned"/> для стержней: сечение КЭ из назначенных ему
/// простых брусовых ТЗА ЛИРЫ (ряды у нижней и верхней грани, .RBT). По длине КЭ армирование постоянно.
/// </summary>
public sealed class LiraAssignedBarSectionSource(LiraBarSectionContext context, LiraRbtFile rbt) : IBarElementSectionSource
{
   readonly Dictionary<(string Cell, LiraBarProfile Profile), BarElementSection> _cache = [];
   readonly Dictionary<string, (List<LiraBarReinforcementType>? Types, string? Reason)> _byCell = new(StringComparer.Ordinal);

   /// <inheritdoc/>
   public string Key => FemCheckRebarSource.Assigned;

   /// <inheritdoc/>
   public string? MissingReason(FemCheckScopeElement element) =>
      Types(element).Reason ?? context.Profile(element).Reason;

   /// <inheritdoc/>
   public BarElementSection Resolve(FemCheckScopeElement element, int? sectionNum)
   {
      var (types, reason) = Types(element);
      string cell = element.Element.ReinforcementTypeIds?.Trim() ?? "";
      string label = cell.Length > 0 ? "ТЗА " + cell : "";
      if (types == null) return BarElementSection.Missing(reason!, label);

      var (profile, profileReason) = context.Profile(element);
      if (profile == null) return BarElementSection.Missing(profileReason!, label);

      if (!_cache.TryGetValue((cell, profile), out var result))
      {
         var (bars, layoutReason) = LiraBarSectionBuilder.AssignedLayout(types, profile);
         result = bars == null
            ? BarElementSection.Missing(layoutReason!, label)
            : new BarElementSection(
               LiraBarSectionBuilder.Build($"{label} {LiraBarSectionBuilder.SizeLabel(profile)}", profile, bars), label, null);
         _cache[(cell, profile)] = result;
      }
      return result;
   }

   (List<LiraBarReinforcementType>? Types, string? Reason) Types(FemCheckScopeElement element)
   {
      string cell = element.Element.ReinforcementTypeIds?.Trim() ?? "";
      if (cell.Length == 0) return (null, "КЭ не назначены ТЗА");
      if (!_byCell.TryGetValue(cell, out var parsed))
         _byCell[cell] = parsed = Parse(cell);
      return parsed;
   }

   (List<LiraBarReinforcementType>?, string?) Parse(string cell)
   {
      IReadOnlyList<int> ids;
      try { ids = LiraPlateReinforcementAssembler.ParseTypeIds(cell); }
      catch (FormatException ex) { return (null, ex.Message); }

      var missing = ids.Where(id => !rbt.BarTypes.ContainsKey(id)).Distinct().Order().ToList();
      if (missing.Count > 0)
         return (null, $"ТЗА {string.Join(", ", missing)} нет среди разобранных брусовых ТЗА файла RBT (разбираются ряды у нижней и верхней грани)");
      var types = ids.Distinct().Select(id => rbt.BarTypes[id]).ToList();
      return types.Count > 0 ? (types, null) : (null, "КЭ не назначены ТЗА");
   }
}
