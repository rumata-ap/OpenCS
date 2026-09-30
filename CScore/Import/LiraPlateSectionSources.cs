using CScore.Fem;

namespace CScore.Import;

/// <summary>
/// Источник <see cref="FemCheckRebarSource.Assigned"/>: сечение КЭ из назначенных ему ТЗА ЛИРЫ
/// (ячейка «Элементы - ТЗА» + описания ТЗА из .RBT). Слои — площадь и привязка из ТЗА.
/// Толщина: своя толщина КЭ → толщина из подбора ЛИРЫ (если файл приложен) → толщина шаблона.
/// </summary>
public sealed class LiraAssignedPlateSectionSource(PlateSection template, LiraRbtFile rbt, LiraAspFile? asp = null)
   : IPlateElementSectionSource
{
   readonly PlateElementSectionFactory _factory = new(template);
   // Наборов ТЗА на схеме единицы, КЭ тысячи: сборка — один раз на набор.
   readonly Dictionary<string, (LiraPlateElementReinforcement? Rebar, string? Reason)> _byCell = new(StringComparer.Ordinal);

   /// <inheritdoc/>
   public string Key => FemCheckRebarSource.Assigned;

   /// <inheritdoc/>
   public PlateElementSection Resolve(FemCheckScopeElement element)
   {
      string cell = element.Element.ReinforcementTypeIds?.Trim() ?? "";
      if (cell.Length == 0)
         return PlateElementSection.Missing("КЭ не назначены ТЗА");

      string label = "ТЗА " + cell;
      if (!_byCell.TryGetValue(cell, out var assembled))
         _byCell[cell] = assembled = Assemble(cell);
      if (assembled.Rebar == null)
         return PlateElementSection.Missing(assembled.Reason!, label);

      double h = element.Element.ThicknessM is > 0 and var own ? own
               : element.ElemNum is int num && asp?.Plates.TryGetValue(num, out var p) == true && p.ThicknessM > 0
                  ? LiraPlateSectionTemplates.Thickness(p)
               : _factory.Template.H;
      var layers = assembled.Rebar.ToPlateRebarLayers(h);
      foreach (var layer in layers)
      {
         // Слой ТЗА задан суммарной площадью — диаметр (нужен для трещин) берётся от шаблона.
         var face = layer.Zsx >= 0 ? _factory.Top : _factory.Bottom;
         if (layer.DiameterX <= 0) layer.DiameterX = face?.DiameterX ?? 0;
         if (layer.DiameterY <= 0) layer.DiameterY = face?.DiameterY ?? 0;
      }
      var (section, key) = _factory.Get(h, layers);
      return new PlateElementSection(section, label, key, null);
   }

   (LiraPlateElementReinforcement?, string?) Assemble(string cell)
   {
      try
      {
         var ids = LiraPlateReinforcementAssembler.ParseTypeIds(cell);
         var r = LiraPlateReinforcementAssembler.Assemble(ids, rbt.PlateTypes);
         if (r.MissingTypeIds.Count > 0)
            return (null, $"ТЗА {string.Join(", ", r.MissingTypeIds)} нет среди пластинчатых ТЗА файла RBT");
         return r.IsEmpty ? (null, "в ТЗА нет арматуры") : (r, null);
      }
      catch (Exception ex) when (ex is FormatException or InvalidOperationException)
      {
         return (null, ex.Message);
      }
   }
}

/// <summary>
/// Источник <see cref="FemCheckRebarSource.Selected"/>: сечение КЭ из подобранной ЛИРОЙ арматуры (*.asp).
/// Площади AS1..AS4 — из файла; положение по толщине и диаметры — от слоёв шаблона той же грани
/// (расстояние от грани сохраняется при другой толщине КЭ).
/// </summary>
public sealed class LiraSelectedPlateSectionSource : IPlateElementSectionSource
{
   const string Label = "ASP";

   readonly PlateElementSectionFactory _factory;
   readonly LiraAspFile _asp;
   readonly string? _templateProblem;

   /// <param name="template">Сечение-шаблон цели.</param>
   /// <param name="asp">Файл подбора ЛИРЫ.</param>
   public LiraSelectedPlateSectionSource(PlateSection template, LiraAspFile asp)
   {
      _factory = new PlateElementSectionFactory(template);
      _asp = asp;
      if (_factory.Top == null)
         _templateProblem = "в сечении цели нет слоя арматуры у грани Z+ — привязку подобранной арматуры взять неоткуда";
      else if (_factory.Bottom == null)
         _templateProblem = "в сечении цели нет слоя арматуры у грани Z− — привязку подобранной арматуры взять неоткуда";
   }

   /// <inheritdoc/>
   public string Key => FemCheckRebarSource.Selected;

   /// <inheritdoc/>
   public PlateElementSection Resolve(FemCheckScopeElement element)
   {
      if (_templateProblem != null)
         return PlateElementSection.Missing(_templateProblem, Label);
      if (element.ElemNum is not int num || !_asp.Plates.TryGetValue(num, out var p))
         return PlateElementSection.Missing("КЭ нет в файле ASP", Label);

      // ЛИРА пишет код «подбор не выполнен» в поле площади со знаком минус.
      foreach (double v in (double[])[p.As1, p.As2, p.As3, p.As4])
         if (v < 0)
            return PlateElementSection.Missing($"подбор ЛИРЫ не выполнен (код {(int)Math.Round(-v)})", Label);

      double h = element.Element.ThicknessM is > 0 and var own ? own
               : p.ThicknessM > 0 ? LiraPlateSectionTemplates.Thickness(p) : _factory.Template.H;
      var layers = new List<PlateRebarLayer>(2);
      AddLayer(layers, "Z+", +1, h, _factory.Top!, p.As2, p.As4, PlateRebar.RebarFace.PlusN);
      AddLayer(layers, "Z-", -1, h, _factory.Bottom!, p.As1, p.As3, PlateRebar.RebarFace.MinusN);
      var (section, key) = _factory.Get(h, layers);
      return new PlateElementSection(section, Label, key, null);
   }

   static void AddLayer(List<PlateRebarLayer> layers, string name, int sign, double h, PlateTemplateFace face,
      double asxCm2, double asyCm2, PlateRebar.RebarFace rebarFace)
   {
      if (asxCm2 <= 0 && asyCm2 <= 0) return;
      layers.Add(new PlateRebarLayer
      {
         Name = name,
         InputMode = "direct",
         Asx = asxCm2 * 1e-4,
         Asy = asyCm2 * 1e-4,
         Zsx = sign * (h / 2.0 - face.CoverX),
         Zsy = sign * (h / 2.0 - face.CoverY),
         DiameterX = face.DiameterX,
         DiameterY = face.DiameterY,
         Face = rebarFace,
      });
   }

   /// <inheritdoc/>
   public IReadOnlyList<string> Warnings(IReadOnlyList<FemCheckScopeElement> elements, Material? concrete, Material? rebar)
   {
      var concreteClasses = new SortedSet<string>(StringComparer.Ordinal);
      var rebarClasses = new SortedSet<string>(StringComparer.Ordinal);
      foreach (var e in elements)
      {
         if (e.ElemNum is not int num || !_asp.Plates.TryGetValue(num, out var p)) continue;
         if (p.ConcreteClass.Length > 0) concreteClasses.Add(p.ConcreteClass);
         if (p.RebarClass.Length > 0) rebarClasses.Add(p.RebarClass);
      }

      var warnings = new List<string>();
      AddMismatch(warnings, "бетона", concreteClasses, concrete);
      AddMismatch(warnings, "арматуры", rebarClasses, rebar);
      return warnings;
   }

   static void AddMismatch(List<string> warnings, string what, SortedSet<string> classes, Material? material)
   {
      if (material == null) return;
      string tag = Normalize(material.Tag);
      var other = classes.Where(c => !tag.Contains(Normalize(c), StringComparison.Ordinal)).ToList();
      if (other.Count > 0)
         warnings.Add($"Класс {what} в подборе ЛИРЫ ({string.Join(", ", other)}) отличается от материала сечения цели " +
                      $"«{material.Tag}» — расчёт выполнен по материалу сечения.");
   }

   /// <summary>Имя класса без пробелов, в верхнем регистре, кириллические буквы-двойники — латиницей.</summary>
   static string Normalize(string s)
   {
      const string cyr = "АВСЕКМНОРТХ", lat = "ABCEKMHOPTX";
      var chars = new List<char>(s.Length);
      foreach (char c in s.ToUpperInvariant())
      {
         if (char.IsWhiteSpace(c)) continue;
         int i = cyr.IndexOf(c);
         chars.Add(i >= 0 ? lat[i] : c);
      }
      return new string([.. chars]);
   }
}
