using CScore;
using OpenCS.Services;

using netDxf;
using netDxf.Entities;

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class DxfPlot : UserControl
   {
      private static readonly List<string> hcolors = ["#318CE7", "#9457EB", "#CC397B", "#F07427", "#F4CA16", "#20B2AA"];
      public DxfDocument? Dxf { get; set; }

      public DxfPlot(DxfDocument dxfDocument)
      {
         InitializeComponent();
         if (dxfDocument == null) return;

         Dxf = dxfDocument;
         var plotService = new WpfPlotService(ViewPl);

         List<Polyline2D> plines = Dxf.Entities.Polylines2D.ToList();
         List<Circle> circles = Dxf.Entities.Circles.ToList();
         List<string> layers = [];

         foreach (var item in plines)
         {
            if (!layers.Contains(item.Layer.Name))
               layers.Add(item.Layer.Name);
         }
         foreach (var item in circles)
         {
            if (!layers.Contains(item.Layer.Name))
               layers.Add(item.Layer.Name);
         }

         int j = 0;
         foreach (var item in layers)
         {
            var plines_lay = from p in plines where p.Layer.Name == item select p;
            foreach (var p in plines_lay)
            {
               var pts = PolylineToPoints(p);
               var xs = pts.Select(pt => pt.X).ToArray();
               var ys = pts.Select(pt => pt.Y).ToArray();
               plotService.AddScatter(xs, ys, lineWidth: 2, color: hcolors[j % hcolors.Count]);
            }
            j++;
         }
         j = 0;
         foreach (var item in layers)
         {
            var circles_lay = from c in circles where c.Layer.Name == item select c;
            foreach (var c in circles_lay)
            {
               plotService.AddCircle(c.Center.X, c.Center.Y, c.Radius,
                  lineColor: hcolors[j % hcolors.Count], lineWidth: 4);
            }
            j++;
         }

         plotService.EnableSquareAxes();
         plotService.AutoScale();
         plotService.Refresh();
      }

      private static List<System.Windows.Point> PolylineToPoints(Polyline2D pline)
      {
         List<System.Windows.Point> points = new(pline.Vertexes.Count + 1);
         foreach (var item in pline.Vertexes)
         {
            points.Add(new System.Windows.Point(item.Position.X, item.Position.Y));
         }
         var first = pline.Vertexes.First().Position;
         var last = pline.Vertexes.Last().Position;
         if (pline.IsClosed && !first.Equals(last, 1e-4))
            points.Add(new System.Windows.Point(first.X, first.Y));

         return points;
      }
   }
}
