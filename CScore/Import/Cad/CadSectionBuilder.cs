namespace CScore.Import.Cad;

/// <summary>
/// Сборка расчётного сечения из распознанного на чертеже: области бетона и стали с отверстиями,
/// стержни — группами по слою и области-носителю (разностная диаграмма относительно бетона-носителя).
/// Оси — местные, начало в центре тяжести областей (x = 0, y = 0).
/// </summary>
public static class CadSectionBuilder
{
   /// <summary>Сечение либо причина, по которой его не собрать.</summary>
   /// <param name="materialOf">Материал слоя заданной роли; null — материал не назначен.</param>
   /// <param name="stirrupMaterial">Материал хомута с учётом класса из подписи; null (или результат null) —
   /// материал слоя хомутов.</param>
   public static (CrossSection? Section, string? Reason) Build(CadSectionCandidate candidate,
      Func<string, CadLayerRole, Material?> materialOf, DiagrammType diagramm = DiagrammType.L2,
      Func<CadSectionStirrup, Material?>? stirrupMaterial = null)
   {
      var section = new CrossSection { Tag = candidate.Name };
      if (candidate.Count > 1)
         section.Description = $"Из чертежа: {string.Join(", ", candidate.Names)}";

      var areas = new List<MaterialArea>();
      foreach (var r in candidate.Regions)
      {
         if (materialOf(r.Layer, r.Role) is not { } m)
            return (null, $"слою «{r.Layer}» не назначен материал");
         string kind = r.Role == CadLayerRole.Steel ? "Сталь" : "Бетон";
         var area = new MaterialArea { Tag = $"{kind}: {r.Layer}", Category = AreaCategory.Region };
         area.Hull = Ring(r.Outer, $"контур: {r.Layer}", ContourType.Hull);
         foreach (var h in r.Holes)
            area.Contours.Add(Ring(h, $"Отв.: {r.Layer}", ContourType.Hole));
         area.SetWKT();
         area.SetMaterial(m, diagramm);
         areas.Add(area);
      }
      section.Areas.AddRange(areas);

      foreach (var grp in candidate.Bars.GroupBy(b => (Layer: b.Layer.ToLowerInvariant(), b.Host)))
      {
         var first = grp.First();
         if (materialOf(first.Layer, CadLayerRole.Rebar) is not { } m)
            return (null, $"слою «{first.Layer}» не назначен материал");
         var host = grp.Key.Host is int h && candidate.Regions[h].Role == CadLayerRole.Concrete ? areas[h] : null;
         var rebar = new MaterialArea
         {
            Tag = host != null ? $"Арм.: {first.Layer}" : $"Арм.: {first.Layer} (вне бетона)",
            Category = AreaCategory.RebarGroup,
            HostArea = host,
            HostAreaId = host?.Id,
         };
         foreach (var b in grp)
            rebar.Fibers.Add(new Fiber(b.X, b.Y)
            {
               TypeFiber = FiberType.point, Diameter = b.DiameterM, Area = Math.PI * b.DiameterM * b.DiameterM / 4,
            });
         rebar.SetMaterial(m, diagramm);
         rebar.ResolveAndBuildDiagramms(); // разностная — по ссылке на носитель
         section.Areas.Add(rebar);
      }
      if (AddStirrups(section, candidate, materialOf, stirrupMaterial, diagramm) is { } why)
         return (null, why);
      // Не section.ResolveAndBuildDiagramms(): у несохранённых областей Id = 0, поиск носителя по Id
      // нашёл бы первую область. HostAreaId переписывается при сохранении (по HostArea.Id).
      return (section, null);
   }

   /// <summary>
   /// Хомуты и шпильки: область поперечного армирования «Хомуты: слой» на каждую пару (слой, материал),
   /// в ней группы по шагу. Элементы без шага пропускаются (предупреждение дало распознавание).
   /// </summary>
   static string? AddStirrups(CrossSection section, CadSectionCandidate candidate,
      Func<string, CadLayerRole, Material?> materialOf, Func<CadSectionStirrup, Material?>? stirrupMaterial,
      DiagrammType diagramm)
   {
      var items = new List<(CadSectionStirrup St, Material M)>();
      foreach (var st in candidate.Stirrups.Where(s => s.SpacingM is > 0))
      {
         if ((stirrupMaterial?.Invoke(st) ?? materialOf(st.Layer, CadLayerRole.Stirrups)) is not { } m)
            return $"слою «{st.Layer}» не назначен материал";
         items.Add((st, m));
      }
      var byArea = items.GroupBy(x => (Layer: x.St.Layer.ToLowerInvariant(), x.M)).ToList();
      foreach (var grp in byArea)
      {
         string layer = grp.First().St.Layer;
         bool several = byArea.Count(g => g.Key.Layer == grp.Key.Layer) > 1;
         var area = new MaterialArea
         {
            Tag = several ? $"Хомуты: {layer} ({grp.Key.M.Tag})" : $"Хомуты: {layer}",
            Category = AreaCategory.Stirrups,
         };
         area.SetMaterial(grp.Key.M, diagramm);
         foreach (var bySpacing in grp.GroupBy(x => Math.Round(x.St.SpacingM!.Value, 6)).OrderBy(g => g.Key))
         {
            var group = new StirrupGroup { MaterialId = grp.Key.M.Id, SpacingM = bySpacing.Key };
            foreach (var (st, _) in bySpacing)
               group.Elements.Add(new StirrupElement
               {
                  CenterlineContour = Axis(st),
                  BarDiameterM = st.DiameterM,
                  BarAreaM2 = Math.PI * st.DiameterM * st.DiameterM / 4,
               });
            area.Stirrups.Add(group);
         }
         section.Areas.Add(area);
      }
      return null;
   }

   /// <summary>Осевая линия: замкнутая — с повтором первой вершины, шпилька — открытая полилиния.</summary>
   static Contour Axis(CadSectionStirrup st)
   {
      string tag = st.Closed ? "хомут" : "шпилька";
      if (!st.Closed)
         return Contour.Polyline(st.Axis.Select(p => p.X), st.Axis.Select(p => p.Y), tag);
      var c = new Contour
      {
         Tag = tag,
         X = [.. st.Axis.Select(p => p.X).Append(st.Axis[0].X)],
         Y = [.. st.Axis.Select(p => p.Y).Append(st.Axis[0].Y)],
      };
      c.SetWKT();
      c.Points = c.XYsToPoints();
      return c;
   }

   static Contour Ring(List<CadPoint> pts, string tag, ContourType type)
   {
      var xs = pts.Select(p => p.X).Append(pts[0].X);
      var ys = pts.Select(p => p.Y).Append(pts[0].Y);
      return new Contour(xs, ys, tag) { Type = type };
   }
}
