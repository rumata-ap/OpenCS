using NetTopologySuite.Geometries;

using Newtonsoft.Json;

namespace CScore
{
   /// <summary>
   /// Область железобетонного сечения — расширяет <see cref="FiberRegion"/>,
   /// добавляя коллекцию групп арматуры (<see cref="ReBarGroup"/>).
   /// Учитывает совместную работу бетона и арматуры при вычислении внутренних усилий:
   /// из усилий в бетоне вычитается вклад арматуры с модульом упругости бетона,
   /// затем прибавляется вклад арматуры с собственным модулем упругости.
   /// </summary>
   [Serializable]
   public class RCFiberRegion: FiberRegion
   {
      /// <summary>
      /// Коллекция групп арматурных стержней в сечении.
      /// Каждая группа имеет свой материал и диаграмму работы.
      /// </summary>
      public List<ReBarGroup> ReBarGroups { get; set; } = [];

      /// <summary>
      /// Геометрические характеристики сечения с учётом арматуры.
      /// Перекрывает свойство <see cref="Region.Props"/> базового класса.
      /// </summary>
      public new GeoProps Props { get => new(this); }

      /// <inheritdoc/>
      public override string ToString()
      {
         if (Material == null)
            return $"{Num:D3}#RCfiberRegion : {Tag} | <No Material>";
         else return $"{Num:D3}#RCfiberRegion : {Tag} | <{Material.Tag}>";
      }

      /// <summary>
      /// Конструктор по умолчанию.
      /// </summary>
      public RCFiberRegion()
      {

      }

      /// <summary>
      /// Создаёт железобетонную область из существующей области и коллекции волокон.
      /// Копирует тег, Hull, материал, контуры, высоту и WKT.
      /// </summary>
      /// <param name="region">Исходная область сечения.</param>
      /// <param name="finiteAreas">Коллекция волокон (может быть null).</param>
      public RCFiberRegion(Region region, IEnumerable<Fiber> finiteAreas)
      {
         Tag = region.Tag;
         Hull = region.Hull;
         Material = region.Material;
         Contours = region.Contours;
         H = region.H;
         WKT = region.WKT;
         if (finiteAreas != null)
            Fibers = finiteAreas.ToList();
      }

      /// <summary>
      /// Создаёт железобетонную область из контура (внешнего), набора отверстий
      /// и коллекции волокон.
      /// </summary>
      /// <param name="contour">Внешний контур сечения.</param>
      /// <param name="holes">Отверстия в сечении (может быть null).</param>
      /// <param name="finiteAreas">Коллекция волокон (может быть null).</param>
      public RCFiberRegion(Contour contour, IEnumerable<Contour> holes = null, IEnumerable<Fiber> finiteAreas = null)
      {
         Tag = contour.Tag;
         Hull = contour;

         if(finiteAreas != null)
            Fibers = finiteAreas.ToList();

         if (holes != null)
         {
            foreach (Contour item in holes)
               item.Type = ContourType.Hole;
            Contours = new List<Contour>(holes);
         };

         Polygon poly = holes != null ? new Polygon(Hull.LinearRing) :
            new Polygon(Hull.LinearRing, (from h in Contours select h.LinearRing).ToArray());

         WKT = poly.ToText();
         double ymin = poly.Envelope.Coordinates[0].Y;
         double ymax = poly.Envelope.Coordinates[1].Y;
         H = ymax - ymin;
      }

      /// <summary>
      /// Сдвигает железобетонную область к центральным координатам.
      /// Помимо Hull, контуров и волокон, сдвигает также все группы арматуры.
      /// </summary>
      /// <param name="centr">Координаты нового центра (вычитаются из текущих).</param>
      public override void ToCentr(XY centr)
      {
         Hull -= centr;

         if (Contours != null)
            for (int i = 0; i < Contours.Count; i++)
               Contours[i] -= centr;

         GetPolystring();

         for (int i = 0; i < Fibers.Count; i++)
         {
            Fibers[i].X -= centr.X;
            Fibers[i].Y -= centr.Y;
         }
         if (ReBarGroups != null)
            foreach (ReBarGroup g in ReBarGroups)
               g.ToCentr(centr);
      }

      /// <summary>
      /// Возвращает железобетонную область из центральных координат к исходным.
      /// Обратная операция к <see cref="ToCentr"/>.
      /// </summary>
      /// <param name="centr">Координаты центра (прибавляются к текущим).</param>
      public override void ToStart(XY centr)
      {
         Hull += centr;

         if (Contours != null)
            for (int i = 0; i < Contours.Count; i++)
               Contours[i] += centr;

         GetPolystring();

         for (int i = 0; i < Fibers.Count; i++)
         {
            Fibers[i].X += centr.X;
            Fibers[i].Y += centr.Y;
         }
         if (ReBarGroups != null)
            foreach (ReBarGroup g in ReBarGroups)
               g.ToStart(centr);
      }

      /// <summary>
      /// Создаёт глубокую копию железобетонной области, включая волокна и группы арматуры.
      /// </summary>
      /// <returns>Новый объект RCFiberRegion с копиями всех данных.</returns>
      public override RCFiberRegion Clone()
      {
         List<Fiber> fas = new List<Fiber>(Fibers.Count);
         for (int i = 0; i < Fibers.Count; i++) fas.Add(Fibers[i].Clone());
         Region reg = this;

         RCFiberRegion res = new RCFiberRegion(reg.Clone(), fas);
         if (ReBarGroups != null)
         {
            List<ReBarGroup> news = new List<ReBarGroup>(ReBarGroups.Count);
            for (int i = 0; i < ReBarGroups.Count; i++)
            {
               news.Add(ReBarGroups[i].Clone());
            }
            res.ReBarGroups = news;
         }
         return res;
      }

      /// <summary>
      /// Вычисляет деформации и напряжения во всех волокнах и арматурных стержнях
      /// по заданной кривизне плоскости деформаций. После вычисления деформаций
      /// в бетоне вызывает диаграмму для волокон, затем вычисляет деформации
      /// и напряжения в арматуре.
      /// </summary>
      /// <param name="kykze0">Кривизна плоскости деформаций (e₀, k_y, k_z).</param>
      /// <param name="calc">Тип расчёта (C, CL, N, NL).</param>
      /// <param name="ten">Учитывать работу на растяжение (по умолчанию true).</param>
      /// <param name="ca">Учитывать работу на сжатие (по умолчанию true).</param>
      public override void SetEps(Kurvature kykze0, CalcType calc, bool ten = true, bool ca = true)
      {
         for (int i = 0; i < Fibers.Count; i++)
         {
            Fibers[i].Eps = kykze0.e0 + kykze0.ky * Fibers[i].Y + kykze0.kz * Fibers[i].X;
         }

         Diagramm dgr = Diagramms[calc];
         dgr.Sig(this, ten, ca);

         if (ReBarGroups != null)
            foreach (ReBarGroup group in ReBarGroups)
               group.SetEps(kykze0, calc, ca);

         for (int i = 0; i < Hull.Points.Count; i++)
         {
            Hull.Points[i].Eps = kykze0.e0 + kykze0.ky * Hull.Points[i].Y + kykze0.kz * Hull.Points[i].X;
            dgr.Sig(Hull.Points[i], ten, ca);
         }
      }

      /// <summary>
      /// Вычисляет внутренние усилия от предварительного напряжения (преддеформации)
      /// для заданного типа расчёта, включая вклад арматуры.
      /// </summary>
      /// <param name="calc">Тип расчёта (C, CL, N, NL).</param>
      /// <param name="tb">Учитывать работу на растяжение.</param>
      /// <param name="ca">Учитывать работу на сжатие.</param>
      /// <returns>Нагрузка <see cref="Load"/> с усилиями от преднапряжения.</returns>
      public new Load GetPreLoad(CalcType calc, bool tb, bool ca)
      {
         Diagramm dia = Diagramms[calc];

         Load res = new Load() { Calc = calc };
         for (int i = 0; i < Fibers.Count; i++)
         {
            double s = dia.Sig(Fibers[i].Eps_p, out double E2, tb, ca);
            res.N_ps += Fibers[i].Area * s;
            res.My_ps += Fibers[i].Area * s * Fibers[i].Y;
            res.Mz_ps += Fibers[i].Area * s * Fibers[i].X;
         }
         if (ReBarGroups != null)
            foreach (ReBarGroup rbg in ReBarGroups)
               res += rbg.GetPreLoad(calc,tb, ca);

         return res;
      }

      /// <summary>
      /// Вычисляет внутренние усилия (N, My, Mz) в железобетонном сечении.
      /// Алгоритм:
      /// 1. Вычисляет деформации и напряжения в волокнах бетона и арматуре.
      /// 2. Вычитает из усилий бетона вклад арматуры с модулем упругости бетона
      ///    (приведение к однородному сечению).
      /// 3. Прибавляет вклад арматуры с собственным модулем упругости.
      /// </summary>
      /// <param name="k">Кривизна плоскости деформаций.</param>
      /// <param name="calc">Тип расчёта (по умолчанию C).</param>
      /// <param name="ten">Учитывать работу на растяжение (по умолчанию true).</param>
      /// <param name="ca">Учитывать работу на сжатие (по умолчанию true).</param>
      /// <returns>Нагрузка <see cref="Load"/> с результирующими усилиями.</returns>
      public new Load Integral(Kurvature k, CalcType calc = CalcType.C, bool ten = true, bool ca = true)
      {
         SetEps(k, calc, ten, ca);
         double N = 0;
         double Mx = 0;
         double My = 0;
         for (int i = 0; i < Fibers.Count; i++)
         {
            N += Fibers[i].N;
            Mx += Fibers[i].My;
            My += Fibers[i].Mz;
         }

         if (ReBarGroups != null)
         {
            foreach (ReBarGroup group in ReBarGroups)
            {
               ReBarGroup group1 = group.Clone();
               group1.Diagramms = Diagramms;
               group1.SetEps(k, calc, ten);

               for (int i = 0; i < group.ReBars.Count; i++)
               {
                  N -= group1.ReBars[i].N;
                  Mx -= group1.ReBars[i].My;
                  My -= group1.ReBars[i].Mz;
               }
            }
            foreach (ReBarGroup group in ReBarGroups)
            {
               group.SetEps(k, calc, ca);
               for (int i = 0; i < group.ReBars.Count; i++)
               {
                  N += group.ReBars[i].N;
                  Mx += group.ReBars[i].My;
                  My += group.ReBars[i].Mz;
               }
            }
         }

         return new Load() { Calc = calc, N = N, My = Mx, Mz = My };
      }

      /// <summary>
      /// Вычисляет внутренние усилия и геометрические характеристики железобетонного сечения.
      /// Алгоритм приведения аналогичен <see cref="Integral(Kurvature, CalcType, bool, bool)"/>,
      /// но дополнительно вычисляет <see cref="GeoProps"/>.
      /// </summary>
      /// <param name="k">Кривизна плоскости деформаций.</param>
      /// <param name="props">Выходной параметр: геометрические характеристики сечения.</param>
      /// <param name="calc">Тип расчёта (по умолчанию C).</param>
      /// <param name="ten">Учитывать работу на растяжение (по умолчанию true).</param>
      /// <param name="ca">Учитывать работу на сжатие (по умолчанию true).</param>
      /// <returns>Нагрузка <see cref="Load"/> с результирующими усилиями.</returns>
      public override Load Integral(Kurvature k, out GeoProps props, CalcType calc = CalcType.C, bool ten = true, bool ca = true)
      {
         SetEps(k, calc, ten, ca);
         double N = 0;
         double Mx = 0;
         double My = 0;
         for (int i = 0; i < Fibers.Count; i++)
         {
            N += Fibers[i].N;
            Mx += Fibers[i].My;
            My += Fibers[i].Mz;
         }
         props = new GeoProps(this);

         if (ReBarGroups != null)
         {
            foreach (ReBarGroup group in ReBarGroups)
            {
               ReBarGroup group1 = group.Clone();
               group1.Diagramms = Diagramms;
               group1.SetEps(k, calc, ten);

               for (int i = 0; i < group.ReBars.Count; i++)
               {
                  N -= group1.ReBars[i].N;
                  Mx -= group1.ReBars[i].My;
                  My -= group1.ReBars[i].Mz;
               }
               props -= new GeoProps(group1);
            }
            foreach (ReBarGroup group in ReBarGroups)
            {
               group.SetEps(k, calc, ca);
               for (int i = 0; i < group.ReBars.Count; i++)
               {
                  N += group.ReBars[i].N;
                  Mx += group.ReBars[i].My;
                  My += group.ReBars[i].Mz;
               }
               props += new GeoProps(group);
            }
         }

         return new Load() { Calc = calc, N = N, My = Mx, Mz = My };
      }

      /// <summary>
      /// Суммирует текущие внутренние усилия (N, My, Mz) по волокнам бетона
      /// и арматурным стержням без пересчёта деформаций.
      /// </summary>
      /// <returns>Нагрузка <see cref="Load"/> с текущими усилиями.</returns>
      public new Load Integral()
      {
         double N = 0;
         double Mx = 0;
         double My = 0;
         for (int i = 0; i < Fibers.Count; i++)
         {
            N += Fibers[i].N;
            Mx += Fibers[i].My;
            My += Fibers[i].Mz;
         }

         if (ReBarGroups != null)
         {
            foreach (ReBarGroup group in ReBarGroups)
            {
               for (int i = 0; i < group.ReBars.Count; i++)
               {
                  N += group.ReBars[i].N;
                  Mx += group.ReBars[i].My;
                  My += group.ReBars[i].Mz;
               }
            }
         }

         return new Load() { N = N, My = Mx, Mz = My };
      }

      /// <summary>
      /// Возвращает максимальную деформацию среди всех арматурных стержней.
      /// </summary>
      /// <returns>Максимальная деформация. Возвращает 0, если группы арматуры отсутствуют.</returns>
      public double MaxStrainInReBar()
      {
         if (ReBarGroups == null) return 0;
         List<double> s = new List<double>();
         foreach (ReBarGroup group in ReBarGroups)
         {
            for (int i = 0; i < group.ReBars.Count; i++)
            {
               s.Add(group.ReBars[i].Eps);
            }
         }
         return s.Max();
      }

      /// <summary>
      /// Возвращает список арматурных стержней, работающих на растяжение (Eps > 0).
      /// </summary>
      /// <returns>Список растянутых арматурных стержней.</returns>
      public List<ReBar> TensileReBars()
      {
         if (ReBarGroups == null) return new List<ReBar>();
         List<ReBar> s = new List<ReBar>();
         foreach (ReBarGroup group in ReBarGroups)
            for (int i = 0; i < group.ReBars.Count; i++)
               if (group.ReBars[i].Eps > 0)
                  s.Add((ReBar)group.ReBars[i]);

         return s;
      }

      /// <summary>
      /// Вычисляет приведённую площадь растянутой арматуры и возвращает
      /// максимальные напряжение и деформацию среди растянутых стержней.
      /// </summary>
      /// <param name="stress">Максимальное напряжение в растянутой арматуре [МПа].</param>
      /// <param name="strain">Максимальная деформация в растянутой арматуре.</param>
      /// <returns>Приведённая площадь растянутой арматуры [м²].</returns>
      public double AreaOfTensileReBars(out double stress, out double strain)
      {
         stress = 0;
         strain = 0;
         if (ReBarGroups == null) return 0;
         List<ReBar> s = TensileReBars();
         if (s.Count == 0) return 0;
         var res = from rb in s orderby rb.Eps descending select rb;
         s = res.ToList();
         strain = s[0].Eps; stress = s[0].Sig;
         double a = 0;
         for (int i = 0; i < s.Count; i++) a += s[i].Area * (s[i].Eps / strain);

         return a;
      }

      /// <summary>
      /// Добавляет слой арматуры по заданному диаметру, количеству стержней
      /// и расстоянию от края сечения. Арматура располагается рядом с верхней
      /// или нижней гранью сечения.
      /// </summary>
      /// <param name="id">Идентификатор группы.</param>
      /// <param name="tag">Метка (тег) слоя арматуры.</param>
      /// <param name="d_mm">Диаметр стержня [мм].</param>
      /// <param name="ns">Количество стержней в слое.</param>
      /// <param name="a">Расстояние от края сечения до центра арматуры [м].</param>
      /// <param name="pos">Положение слоя (верхнее или нижнее).</param>
      /// <param name="material">Материал арматуры.</param>
      public void AddReBarLayer(int id, string tag, double d_mm, int ns, double a,
                                 ReBarLayerPos pos, Material material)
      {
         ReBarLayer relayer = new ReBarLayer(d_mm, ns, a, pos, this) { Tag = tag};
         var lay = new ReBarGroup(relayer.Tag, material, new ReBar[] { relayer });
         ReBarGroups.Add(lay);
      }

      /// <summary>
      /// Добавляет слой арматуры по заданному диаметру, общей площади и расстоянию
      /// от края сечения. Количество стержней вычисляется автоматически.
      /// </summary>
      /// <param name="id">Идентификатор группы.</param>
      /// <param name="tag">Метка (тег) слоя арматуры.</param>
      /// <param name="d_mm">Диаметр стержня [мм].</param>
      /// <param name="As">Общая площадь арматуры [м²].</param>
      /// <param name="a">Расстояние от края сечения до центра арматуры [м].</param>
      /// <param name="pos">Положение слоя (верхнее или нижнее).</param>
      /// <param name="material">Материал арматуры.</param>
      public void AddReBarLayer(int id, string tag, double d_mm, double As, double a,
                                 ReBarLayerPos pos, Material material)
      {
         ReBarLayer relayer = new ReBarLayer(d_mm, As, a, pos, this) { Num = id, Tag = tag};
         var lay = new ReBarGroup(relayer.Tag, material, new ReBar[] { relayer });
         ReBarGroups.Add(lay);
      }

      /// <summary>
      /// Добавляет группу арматурных стержней в сечение.
      /// </summary>
      /// <param name="group">Группа арматурных стержней. Если null, ничего не делается.</param>
      public void AddReBarGroup(ReBarGroup group)
      {
         if (group == null) return;
         ReBarGroups.Add(group);
      }

      /// <summary>
      /// Формирует объект <see cref="FiberRegionData"/> с массивами данных волокон
      /// и арматурных стержней для сериализации.
      /// </summary>
      /// <returns>Объект FiberRegionData, содержащий данные бетона и арматуры.</returns>
      public new FiberRegionData Data()
      {
         return new FiberRegionData(this);
      }
   }
}