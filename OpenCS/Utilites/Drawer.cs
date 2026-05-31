using CScore;

using OpenCS.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenCS.Utilites
{
   public static class Drawer
   {
      public static readonly double scale = 1000;
      public static readonly double t = 30;
      public static Point ToWpfCoords(XY cartesianPoint, double centerX, double centerY)
      {
         // xWpf = x + centerX
         double xWpf = cartesianPoint.X * scale+ centerX * scale;

         // yWpf = centerY - y
         double yWpf = centerY * scale - cartesianPoint.Y * scale;

         return new Point(xWpf, yWpf);
      }

      public static DrawingImage Draw(this Contour cnt)
      {
         List<Point> points = new(cnt.Points.Count - 1);
         var ordx = from c in cnt.Points orderby c.X select c.X;
         var ordy = from c in cnt.Points orderby c.Y select c.Y;
         double xmax = ordx.Last();
         double ymax = ordy.Last();
         double xmin = ordx.First();
         double ymin = ordy.First();
         double b = xmax - xmin;
         double h = ymax - ymin;
         double tot = Math.Max(b, h);

         for (int i = 0; i < cnt.Points.Count-1; i++)
         {
            points.Add(ToWpfCoords(cnt.Points[i], 0.5 * b, 0.5 * h));
         }
         // 1. Создаем PathGeometry
         var geometry = new PathGeometry();
         var figure = new PathFigure
         {
            StartPoint = points[0], // Начало отсчета в верхнем левом углу
            IsClosed = true,
         };
         for (int i = 1; i < points.Count; i++)
         {
            figure.Segments.Add(new LineSegment(points[i], true));
         }
         geometry.Figures.Add(figure);

         // 2. Создаем GeometryDrawing для отображения фигуры
         var geometryDrawing = new GeometryDrawing
         {
            Geometry = geometry,
            Brush = Brushes.WhiteSmoke, // Заливка
            Pen = new Pen(Brushes.DarkBlue, tot * t) // Рамка
         };

         // 3. Создаем DrawingImage
         var drawingImage = new DrawingImage(geometryDrawing);
         // Опционально: замораживаем для повышения производительности
         drawingImage.Freeze();

         return drawingImage;
      }
   }
}
