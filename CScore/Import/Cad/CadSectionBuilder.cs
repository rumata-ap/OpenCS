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
   public static (CrossSection? Section, string? Reason) Build(CadSectionCandidate candidate,
      Func<string, CadLayerRole, Material?> materialOf, DiagrammType diagramm = DiagrammType.L2)
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
      // Не section.ResolveAndBuildDiagramms(): у несохранённых областей Id = 0, поиск носителя по Id
      // нашёл бы первую область. HostAreaId переписывается при сохранении (по HostArea.Id).
      return (section, null);
   }

   static Contour Ring(List<CadPoint> pts, string tag, ContourType type)
   {
      var xs = pts.Select(p => p.X).Append(pts[0].X);
      var ys = pts.Select(p => p.Y).Append(pts[0].Y);
      return new Contour(xs, ys, tag) { Type = type };
   }
}
