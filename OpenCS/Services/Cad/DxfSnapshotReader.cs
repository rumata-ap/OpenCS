using System.IO;
using CScore.Import.Cad;
using netDxf;
using netDxf.Entities;
using netDxf.Units;

namespace OpenCS.Services.Cad;

/// <summary>
/// Чтение DXF (файл или выгрузка из AutoCAD/nanoCAD) в <see cref="CadSnapshot"/> через netDxf: полилинии с дугами,
/// отрезки и дуги (сшиваются в контуры), окружности, эллипсы, тексты; вхождения блоков раскрываются рекурсивно,
/// объекты блока на слое «0» получают слой вставки. Поддерживаются DXF от R2000 (AC1015).
/// </summary>
public static class DxfSnapshotReader
{
   /// <summary>Наибольшая глубина вложенности блоков.</summary>
   const int MaxDepth = 16;

   /// <param name="arcs">Дискретизация дуг в единицах чертежа.</param>
   /// <param name="tolerance">Допуск совпадения концов при сшивке, в единицах чертежа.</param>
   public static CadSnapshot Read(string path, CadArcDiscretization arcs, double tolerance)
   {
      var doc = DxfDocument.Load(path)
         ?? throw new InvalidOperationException($"Не удалось прочитать DXF «{Path.GetFileName(path)}» (поддерживаются версии от AutoCAD 2000).");
      return Read(doc, arcs, tolerance, Path.GetFileName(path));
   }

   public static CadSnapshot Read(DxfDocument doc, CadArcDiscretization arcs, double tolerance, string source)
   {
      var b = new CadSnapshotBuilder(arcs, tolerance);
      int skipped = 0;
      foreach (var e in doc.Entities.All)
         Add(b, e, null, 0, ref skipped);
      var s = b.Build();
      s.Source = source;
      s.MetersPerUnit = MetersPerUnit(doc.DrawingVariables.InsUnits);
      if (skipped > 0) s.Warnings.Add($"не поддерживаемых объектов пропущено: {skipped}");
      return s;
   }

   /// <summary>Метров в единице чертежа по $INSUNITS; null — не задано.</summary>
   public static double? MetersPerUnit(DrawingUnits u) => u switch
   {
      DrawingUnits.Millimeters => 0.001,
      DrawingUnits.Centimeters => 0.01,
      DrawingUnits.Decimeters => 0.1,
      DrawingUnits.Meters => 1,
      DrawingUnits.Inches => 0.0254,
      DrawingUnits.Feet => 0.3048,
      _ => null,
   };

   static void Add(CadSnapshotBuilder b, EntityObject e, string? insertLayer, int depth, ref int skipped)
   {
      string layer = e.Layer?.Name ?? "0";
      if (insertLayer != null && layer == "0") layer = insertLayer;
      bool mirrored = e.Normal.Z < 0; // OCS с нормалью −Z: координаты X в OCS зеркальны

      switch (e)
      {
         case Polyline2D p:
         {
            var v = p.Vertexes.Select(x => mirrored
               ? new CadVertex(-x.Position.X, x.Position.Y, -x.Bulge)
               : new CadVertex(x.Position.X, x.Position.Y, x.Bulge)).ToList();
            b.AddPolyline(layer, v, p.IsClosed);
            break;
         }
         case Line l:
            b.AddLine(layer, l.StartPoint.X, l.StartPoint.Y, l.EndPoint.X, l.EndPoint.Y);
            break;
         case Arc a:
         {
            double s = a.StartAngle * Math.PI / 180, en = a.EndAngle * Math.PI / 180;
            if (mirrored) (s, en) = (Math.PI - en, Math.PI - s);
            b.AddArc(layer, a.Center.X, a.Center.Y, a.Radius, s, en);
            break;
         }
         case Circle c:
            b.AddCircle(layer, c.Center.X, c.Center.Y, c.Radius);
            break;
         case Ellipse el:
         {
            double r = Math.Max(el.MajorAxis, el.MinorAxis) / 2;
            int n = Math.Max(8, b.Arcs.Segments(r, 2 * Math.PI));
            var pl = el.ToPolyline2D(n);
            var pts = pl.Vertexes.Select(x => mirrored ? new CadPoint(-x.Position.X, x.Position.Y) : new CadPoint(x.Position.X, x.Position.Y)).ToList();
            b.AddEllipse(layer, pts, el.IsFullEllipse);
            break;
         }
         case Text t:
            b.AddText(layer, t.Position.X, t.Position.Y, t.Height, t.Value);
            break;
         case MText m:
            b.AddText(layer, m.Position.X, m.Position.Y, m.Height, m.PlainText());
            break;
         case Insert ins when depth < MaxDepth:
            foreach (var x in ins.Explode())
               Add(b, x, layer, depth + 1, ref skipped);
            break;
         case Hatch or Dimension or Leader or Point or Solid or Wipeout or Image or Viewport or XLine or Ray:
            break; // оформление чертежа — не геометрия сечения
         default:
            skipped++;
            break;
      }
   }
}
