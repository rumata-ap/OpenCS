using System;

namespace CScore
{
   /// <summary>
   /// Геометрические характеристики поперечного сечения: площадь, статические моменты,
   /// моменты инерции и приведённые (с учётом модулей упругости) характеристики.
   /// Вычисляются как для контуров (полигонов), так и для дискретных волокон.
   /// Поддерживает арифметические операции сложения, вычитания и умножения на скаляр.
   /// </summary>
   [Serializable]
   public class GeoProps
   {
      /// <summary>
      /// Определитель центральных моментов инерции.
      /// </summary>
      public double D;

      /// <summary>
      /// Площадь сечения [м²].
      /// </summary>
      public double A { get; set; }

      /// <summary>
      /// Статический момент площади относительно оси X (Sx = ∫y·dA).
      /// </summary>
      public double Sx { get; set; }

      /// <summary>
      /// Статический момент площади относительно оси Y (Sy = ∫x·dA).
      /// </summary>
      public double Sy { get; set; }

      /// <summary>
      /// Момент инерции относительно оси X (Ix = ∫y²·dA).
      /// </summary>
      public double Ix { get; set; }

      /// <summary>
      /// Момент инерции относительно оси Y (Iy = ∫x²·dA).
      /// </summary>
      public double Iy { get; set; }

      /// <summary>
      /// Центробежный момент инерции (Ixy = ∫x·y·dA).
      /// </summary>
      public double Ixy { get; set; }

      /// <summary>
      /// Приведённая площадь сечения (EA = A · E).
      /// </summary>
      public double EA { get; set; }

      /// <summary>
      /// Приведённый статический момент относительно оси X (ESx = Sx · E).
      /// </summary>
      public double ESx { get; set; }

      /// <summary>
      /// Приведённый статический момент относительно оси Y (ESy = Sy · E).
      /// </summary>
      public double ESy { get; set; }

      /// <summary>
      /// Приведённый момент инерции относительно оси X (EIx = Ix · E).
      /// </summary>
      public double EIx { get; set; }

      /// <summary>
      /// Приведённый момент инерции относительно оси Y (EIy = Iy · E).
      /// </summary>
      public double EIy { get; set; }

      /// <summary>
      /// Приведённый центробежный момент инерции (EIxy = Ixy · E).
      /// </summary>
      public double EIxy { get; set; }

      /// <summary>
      /// Координаты центра тяжести сечения (Xc = ESy/EA, Yc = ESx/EA).
      /// </summary>
      public XY Centroid { get; set; }

      /// <summary>
      /// Тип геометрических характеристик (первый или второй модуль упругости).
      /// </summary>
      public GeoPropsType Type { get; set; }

      /// <summary>
      /// Конструктор по умолчанию.
      /// </summary>
      public GeoProps()
      {

      }

      /// <summary>
      /// Вычисляет геометрические характеристики замкнутого контура по формулам
      /// Грина. Площадь, статические моменты и моменты инерции вычисляются
      /// интегрированием по контуру.
      /// </summary>
      /// <param name="contour">Замкнутый контур сечения.</param>
      /// <param name="e">Модуль упругости для расчёта приведённых характеристик (0 — без приведения).</param>
      public GeoProps(Contour contour, double e=0)
      {
         if (contour == null) return;
         contour.PointsToXYs();
         var X = contour.X;   var Y = contour.Y;
         double temp = 0;
         for (int i = 0; i < X.Count - 1; i++)
         {
            temp += 0.5 * (X[i] * Y[i + 1] - X[i + 1] * Y[i]);
         }
         A = Math.Abs(temp);

         XY s = new XY();
         for (int i = 0; i < X.Count - 1; i++)
         {
            s.X += 1 / 6 * (X[i] + X[i + 1]) * (X[i] * Y[i + 1] - Y[i] * X[i + 1]);
            s.Y += 1 / 6 * (Y[i] + Y[i + 1]) * (X[i] * Y[i + 1] - Y[i] * X[i + 1]);
         }
         Sy = s.X; Sx = s.Y;

         double tempX = 0;
         double tempY = 0;
         for (int i = 0; i < X.Count - 1; i++)
         {
            tempX += (Math.Pow(X[i], 2) + X[i] * X[i + 1] +
               Math.Pow(X[i + 1], 2)) * (X[i] * Y[i + 1] - Y[i] * X[i + 1]);
            tempY += (Math.Pow(Y[i], 2) + Y[i] * Y[i + 1] +
               Math.Pow(Y[i + 1], 2)) * (X[i] * Y[i + 1] - Y[i] * X[i + 1]);
         }
         Iy = Math.Abs(tempX / 12);
         Ix = Math.Abs(tempY / 12);

         EA = A * e;
         ESx = Sx * e;
         ESy = Sy * e;
         EIx = Ix * e;
         EIy = Iy * e;

         Centroid = new XY(Sy / A, Sx / A);
      }

      /// <summary>
      /// Вычисляет геометрические характеристики области (внешний контур минус отверстия).
      /// Приведённые характеристики вычисляются с использованием модуля упругости
      /// материала области.
      /// </summary>
      /// <param name="region">Область сечения с Hull и возможными отверстиями.</param>
      public GeoProps(Region region)
      {
         double E = 0;
         if (region.Material != null) E = region.Material.E;
         var ocp = new GeoProps(region.Hull, E);
         if (region.Holes != null && region.Holes.Count > 0)
         {
            foreach (Contour item in region.Holes)
               ocp -= new GeoProps(item, E);
         }

         A = ocp.A;
         Sy = ocp.Sx;
         Sx = ocp.Sy;
         Iy = ocp.Iy;
         Ix = ocp.Ix;
         Ixy = ocp.Ixy;
         EA = A * E;
         ESy = Sy * E;
         ESx = Sx * E;
         EIy = Iy * E;
         EIx = Ix * E;
         EIxy = Ixy * E;
         Centroid = ocp.Centroid;
      }

      /// <summary>
      /// Вычисляет геометрические характеристики области волокон путём суммирования
      /// вкладов каждого волокна. Использует секущий (First) или касательный (Second)
      /// модуль упругости в зависимости от <paramref name="propsType"/>.
      /// </summary>
      /// <param name="fiberRegion">Область волокон.</param>
      /// <param name="propsType">Тип модуля упругости: First — секущий, Second — касательный.</param>
      public GeoProps(FiberRegion fiberRegion, GeoPropsType propsType = GeoPropsType.First)
      {
         double E, a, sx, sy, ix, iy, ixy;
         Fiber f = null;
         for (int i = 0; i < fiberRegion.Fibers.Count; i++)
         {
            f = fiberRegion.Fibers[i];
            E = propsType == GeoPropsType.First ? f.E : f.E2;
            a = f.Area;
            sy = f.Area * f.X;
            sx = f.Area * f.Y;
            iy = sy * f.X;
            ix = sx * f.Y;
            ixy = a * f.X * f.Y;
            A += a;
            Sy += sy;
            Sx += sx;
            Iy += iy;
            Ix += ix;
            Ixy += ixy;
            EA += a * E;
            ESy += sy * E;
            ESx += sx * E;
            EIy += iy * E;
            EIx += ix * E;
            EIxy += ixy * E;
            Centroid = new XY(ESy / EA, ESx / EA);
         }
      }

      /// <summary>
      /// Вычисляет геометрические характеристики железобетонного сечения:
      /// суммирует вклады волокон бетона и арматурных стержней.
      /// </summary>
      /// <param name="fiberRegion">Железобетонная область сечения.</param>
      /// <param name="propsType">Тип модуля упругости: First — секущий, Second — касательный.</param>
      public GeoProps(RCFiberRegion fiberRegion, GeoPropsType propsType = GeoPropsType.First)
      {
         double E, a, sx, sy, ix, iy, ixy;
         Fiber f = null;
         ReBar rb = null;
         for (int i = 0; i < fiberRegion.Fibers.Count; i++)
         {
            f = fiberRegion.Fibers[i];
            E = propsType == GeoPropsType.First ? f.E : f.E2;
            a = f.Area;
            sy = f.Area * f.X;
            sx = f.Area * f.Y;
            iy = sy * f.X;
            ix = sx * f.Y;
            ixy = a * f.X * f.Y;
            A += a;
            Sy += sy;
            Sx += sx;
            Iy += iy;
            Ix += ix;
            Ixy += ixy;
            EA += a * E;
            ESy += sy * E;
            ESx += sx * E;
            EIy += iy * E;
            EIx += ix * E;
            EIxy += ixy * E;
         }
         if(fiberRegion.ReBarGroups.Count > 0)
         {
            foreach (var group in fiberRegion.ReBarGroups)
            {
               for (int i = 0; i < group.ReBars.Count; i++)
               {
                  rb = group.ReBars[i];
                  E = propsType == GeoPropsType.First ? group.ReBars[i].E : group.ReBars[i].E2;
                  E = group.ReBars[i].Eps == 0 ? group.Material.E : E;
                  a = rb.Area;
                  sy = rb.Area * rb.X;
                  sx = rb.Area * rb.Y;
                  iy = sy * rb.X;
                  ix = sx * rb.Y;
                  ixy = a * rb.X * rb.Y;
                  Sy += sy;
                  Sx += sx;
                  Iy += iy;
                  Ix += ix;
                  Ixy += ixy;
                  EA += a * E;
                  ESy += sy * E;
                  ESx += sx * E;
                  EIy += iy * E;
                  EIx += ix * E;
                  EIxy += ixy * E;
               }
            }
         }

         Centroid = new XY(ESy / EA, ESx / EA);
      }

      /// <summary>
      /// Вычисляет геометрические характеристики материальной области
      /// суммированием по всем волокнам (полигональным и точечным).
      /// </summary>
      public GeoProps(MaterialArea area, GeoPropsType propsType = GeoPropsType.First)
      {
         double E;
         foreach (var f in area.Fibers)
         {
            E = propsType == GeoPropsType.First ? f.E : f.E2;
            if (E == 0 && area.Material != null) E = area.Material.E;
            double a   = f.Area;
            double sy  = f.Area * f.X;
            double sx  = f.Area * f.Y;
            double iy  = sy * f.X;
            double ix  = sx * f.Y;
            double ixy = a * f.X * f.Y;
            A    += a;    Sy   += sy;   Sx   += sx;
            Iy   += iy;   Ix   += ix;   Ixy  += ixy;
            EA   += a * E;   ESy  += sy * E;  ESx  += sx * E;
            EIy  += iy * E;  EIx  += ix * E;  EIxy += ixy * E;
         }
         if (EA > 0) Centroid = new XY(ESy / EA, ESx / EA);
      }

      /// <summary>
      /// Вычисляет геометрические характеристики составного поперечного сечения.
      /// </summary>
      public GeoProps(CrossSection section, GeoPropsType propsType = GeoPropsType.First)
      {
         foreach (var area in section.Areas)
         {
            var ap = new GeoProps(area, propsType);
            A    += ap.A;    Sy   += ap.Sy;   Sx   += ap.Sx;
            Iy   += ap.Iy;   Ix   += ap.Ix;   Ixy  += ap.Ixy;
            EA   += ap.EA;   ESy  += ap.ESy;  ESx  += ap.ESx;
            EIy  += ap.EIy;  EIx  += ap.EIx;  EIxy += ap.EIxy;
         }
         if (EA > 0) Centroid = new XY(ESy / EA, ESx / EA);
      }

      /// <summary>
      /// Вычисляет геометрические характеристики группы арматурных стержней
      /// путём суммирования вкладов каждого стержня.
      /// </summary>
      /// <param name="group">Группа арматурных стержней.</param>
      /// <param name="propsType">Тип модуля упругости: First — секущий, Second — касательный.</param>
      public GeoProps(ReBarGroup group, GeoPropsType propsType = GeoPropsType.First)
      {
         double E, a, sx, sy, ix, iy, ixy;
         ReBar f = null;
         for (int i = 0; i < group.ReBars.Count; i++)
         {
            f = group.ReBars[i];
            E = propsType == GeoPropsType.First ? f.E : f.E2;
            a = f.Area;
            sy = f.Area * f.X;
            sx = f.Area * f.Y;
            iy = sy * f.X;
            ix = sx * f.Y;
            ixy = a * f.X * f.Y;
            A += a;
            Sy += sy;
            Sx += sx;
            Iy += iy;
            Ix += ix;
            Ixy += ixy;
            EA += a * E;
            ESy += sy * E;
            ESx += sx * E;
            EIy += iy * E;
            EIx += ix * E;
            EIxy += ixy * E;
            Centroid = new XY(ESy / EA, ESx / EA);
         }
      }

      /// <summary>
      /// Складывает геометрические характеристики двух сечений.
      /// Центр тяжести пересчитывается по приведённым характеристикам.
      /// </summary>
      public static GeoProps operator +(GeoProps l1, GeoProps l2)
      {
         GeoProps res = new GeoProps()
         {
            A = l1.A + l2.A,
            Sx = l1.Sx + l2.Sx,
            Sy = l1.Sy + l2.Sy,
            Ix = l1.Ix + l2.Ix,
            Iy = l1.Iy + l2.Iy,
            Ixy = l1.Ixy + l2.Ixy,
            EA = l1.EA + l2.EA,
            ESx = l1.ESx + l2.ESx,
            ESy = l1.ESy + l2.ESy,
            EIx = l1.EIx + l2.EIx,
            EIy = l1.EIy + l2.EIy,
            EIxy = l1.EIxy + l2.EIxy,
         };
         res.D = res.A * (res.Ix * res.Iy - res.Ixy * res.Ixy) +
            res.Sx * (res.Sy * res.Ixy - res.Sx * res.Iy) +
            res.Sy * (res.Sx * res.Ixy - res.Sy * res.Ix);
         res.Centroid = new XY(res.ESy / res.EA, res.ESx / res.EA);

         return res;
      }

      /// <summary>
      /// Вычитает геометрические характеристики (для отверстий).
      /// Центр тяжести пересчитывается по приведённым характеристикам.
      /// </summary>
      public static GeoProps operator -(GeoProps l1, GeoProps l2)
      {
         GeoProps res = new GeoProps()
         {
            A = l1.A - l2.A,
            Sx = l1.Sx - l2.Sx,
            Sy = l1.Sy - l2.Sy,
            Ix = l1.Ix - l2.Ix,
            Iy = l1.Iy - l2.Iy,
            Ixy = l1.Ixy - l2.Ixy,
            EA = l1.EA - l2.EA,
            ESx = l1.ESx - l2.ESx,
            ESy = l1.ESy - l2.ESy,
            EIx = l1.EIx - l2.EIx,
            EIy = l1.EIy - l2.EIy,
            EIxy = l1.EIxy - l2.EIxy,
         };
         res.D = res.A * (res.Ix * res.Iy - res.Ixy * res.Ixy) +
            res.Sx * (res.Sy * res.Ixy - res.Sx * res.Iy) +
            res.Sy * (res.Sx * res.Ixy - res.Sy * res.Ix);
         res.Centroid = new XY(res.ESy / res.EA, res.ESx / res.EA);

         return res;
      }

      /// <summary>
      /// Умножает геометрические характеристики на скаляр.
      /// </summary>
      public static GeoProps operator *(GeoProps l1, double l2)
      {
         GeoProps res = new GeoProps()
         {
            A = l1.A * l2,
            Sx = l1.Sx * l2,
            Sy = l1.Sy * l2,
            Ix = l1.Ix * l2,
            Iy = l1.Iy * l2,
            Ixy = l1.Ixy * l2,
            EA = l1.EA * l2,
            ESx = l1.ESx * l2,
            ESy = l1.ESy * l2,
            EIx = l1.EIx * l2,
            EIy = l1.EIy * l2,
            EIxy = l1.EIxy * l2,
         };
         res.D = res.A * (res.Ix * res.Iy - res.Ixy * res.Ixy) +
            res.Sx * (res.Sy * res.Ixy - res.Sx * res.Iy) +
            res.Sy * (res.Sx * res.Ixy - res.Sy * res.Ix);
         res.Centroid = new XY(res.ESy / res.EA, res.ESx / res.EA);

         return res;
      }

      /// <summary>
      /// Делит геометрические характеристики на скаляр.
      /// </summary>
      public static GeoProps operator /(GeoProps l1, double l2)
      {
         GeoProps res = new GeoProps()
         {
            A = l1.A / l2,
            Sx = l1.Sx / l2,
            Sy = l1.Sy / l2,
            Ix = l1.Ix / l2,
            Iy = l1.Iy / l2,
            Ixy = l1.Ixy / l2,
            EA = l1.EA / l2,
            ESx = l1.ESx / l2,
            ESy = l1.ESy / l2,
            EIx = l1.EIx / l2,
            EIy = l1.EIy / l2,
            EIxy = l1.EIxy / l2,
         };
         res.D = res.A * (res.Ix * res.Iy - res.Ixy * res.Ixy) +
            res.Sx * (res.Sy * res.Ixy - res.Sx * res.Iy) +
            res.Sy * (res.Sx * res.Ixy - res.Sy * res.Ix);
         res.Centroid = new XY(res.ESy / res.EA, res.ESx / res.EA);

         return res;
      }

      /// <summary>
      /// Умножает геометрические характеристики на скаляр (коммутативная форма).
      /// </summary>
      public static GeoProps operator *(double l2, GeoProps l1)
      {
         GeoProps res = new GeoProps()
         {
            A = l1.A * l2,
            Sx = l1.Sx * l2,
            Sy = l1.Sy * l2,
            Ix = l1.Ix * l2,
            Iy = l1.Iy * l2,
            Ixy = l1.Ixy * l2,
            EA = l1.EA * l2,
            ESx = l1.ESx * l2,
            ESy = l1.ESy * l2,
            EIx = l1.EIx * l2,
            EIy = l1.EIy * l2,
            EIxy = l1.EIxy * l2,
         };
         res.D = res.A * (res.Ix * res.Iy - res.Ixy * res.Ixy) +
            res.Sx * (res.Sy * res.Ixy - res.Sx * res.Iy) +
            res.Sy * (res.Sx * res.Ixy - res.Sy * res.Ix);
         res.Centroid = new XY(res.ESy / res.EA, res.ESx / res.EA);

         return res;
      }
   }

   /// <summary>
   /// Тип модуля упругости для вычисления геометрических характеристик:
   /// First — секущий модуль E, Second — касательный модуль E₂.
   /// </summary>
   public enum GeoPropsType { First = 1, Second = 2 }
}