using System.Text.Json.Serialization;

using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore
{
   /// <summary>
   /// Область волокон — область сечения, разбитая на конечные элементы (волокна).
   /// Наследует <see cref="Region"/>, добавляя коллекцию волокон и диаграммы
   /// работы материала. Используется для вычисления внутренних усилий (N, My, Mz)
   /// методом интегрирования по волокнам.
   /// </summary>
   [Serializable]
   public class FiberRegion : Region
   {
      /// <summary>
      /// Количество участков деления по оси X (используется при нарезке).
      /// По умолчанию 21.
      /// </summary>
      public int NX { get; set; } = 21;

      /// <summary>
      /// Количество участков деления по оси Y (используется при нарезке).
      /// По умолчанию 21.
      /// </summary>
      public int NY { get; set; } = 21;

      /// <summary>
      /// Относительная площадь сечения арматуры (A_s / A_b).
      /// По умолчанию 0.25.
      /// </summary>
      public double Atr { get; set; } = 0.25;

      /// <summary>
      /// Площадь нетто (без учёта арматуры).
      /// По умолчанию 25.
      /// </summary>
      public double Antr { get; set; } = 25;

      /// <summary>
      /// Коллекция волокон (конечных элементов), на которые разбита область сечения.
      /// </summary>
      public List<Fiber> Fibers { get; set; } = [];

      /// <summary>
      /// Словарь диаграмм работы материала по типам расчёта.
      /// Ключ — тип расчёта (<see cref="CalcType"/>), значение — объект <see cref="Diagramm"/>.
      /// Не сохраняется в БД.
      /// </summary>
      public Dictionary<CalcType, Diagramm> Diagramms { get; set; } = [];

      /// <inheritdoc/>
      public override string ToString()
      {
         if (Material == null)
            return $"{Num:D3}#fiberRegion : {Tag} | <No Material>";
         else return $"{Num:D3}#fiberRegion : {Tag} | <{Material.Tag}>";
      }

      /// <summary>
      /// Конструктор по умолчанию.
      /// </summary>
      public FiberRegion() { }

      /// <summary>
      /// Создаёт область волокон из существующей области и коллекции волокон.
      /// Копирует тег, материал, контуры и WKT из исходной области.
      /// </summary>
      /// <param name="region">Исходная область сечения.</param>
      /// <param name="finiteAreas">Коллекция волокон (конечных элементов).</param>
      /// <param name="id">Порядковый номер области (по умолчанию -1).</param>
      public FiberRegion(Region region, IEnumerable<Fiber> finiteAreas, int id = -1)
      {
         Num = id;
         Tag = region.Tag;
         Material = region.Material;
         Contours = region.Contours;
         H = region.H;
         WKT = region.WKT;
         Fibers = finiteAreas.ToList();
      }

      /// <summary>
      /// Сдвигает область волокон к центральным координатам (перенос начала координат).
      /// Сдвигает Hull, контуры, волокна и обновляет WKT-представление.
      /// </summary>
      /// <param name="centr">Координаты нового центра (вычитаются из текущих).</param>
      public virtual void ToCentr(XY centr)
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
      }

      /// <summary>
      /// Возвращает область волокон из центральных координат к исходным
      /// (обратная операция к <see cref="ToCentr"/>).
      /// </summary>
      /// <param name="centr">Координаты центра (прибавляются к текущим).</param>
      public virtual void ToStart(XY centr)
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
      }

      /// <summary>
      /// Создаёт глубокую копию области волокон, включая клонирование всех волокон.
      /// </summary>
      /// <returns>Новый объект FiberRegion с копиями волокон.</returns>
      public override FiberRegion Clone()
      {
         List<Fiber> fas = new List<Fiber>(Fibers.Count);
         for (int i = 0; i < Fibers.Count; i++) fas.Add(Fibers[i].Clone());
         FiberRegion res = new FiberRegion(this, fas, Num++);
         return res;
      }

      /// <summary>
      /// Назначает материал и вычисляет диаграммы работы материала заданного типа.
      /// </summary>
      /// <param name="material">Материал области.</param>
      /// <param name="diagrammType">Тип диаграммы (L2, L3, SP63).</param>
      public void SetMaterial(Material material, DiagrammType diagrammType)
      {
         Material = material;
         Diagramms = material.GetDiagramms(diagrammType);
      }

      /// <summary>
      /// Пересчитывает диаграммы работы материала по заданному типу,
      /// используя уже назначенный материал. Если материал не назначен, ничего не делает.
      /// </summary>
      /// <param name="diagrammType">Тип диаграммы (L2, L3, SP63).</param>
      public void SetDiagramms(DiagrammType diagrammType)
      {
         if (Material != null)
            Diagramms = Material.GetDiagramms(diagrammType);
      }

      /// <summary>
      /// Вычисляет деформации и напряжения во всех волокнах по заданной кривизне
      /// плоскости деформаций (гипотеза Бернулли: ε = e₀ + k_y·y + k_z·x).
      /// После вычисления деформаций вызывает диаграмму материала для расчёта напряжений.
      /// </summary>
      /// <param name="kykze0">Кривизна плоскости деформаций (e₀, k_y, k_z).</param>
      /// <param name="calc">Тип расчёта (C, CL, N, NL).</param>
      /// <param name="ten">Учитывать работу на растяжение (для бетона).</param>
      /// <param name="ca">Учитывать работу на сжатие (для арматуры).</param>
      public virtual void SetEps(Kurvature kykze0, CalcType calc, bool ten, bool ca)
      {
         for (int i = 0; i < Fibers.Count; i++)
         {
            Fibers[i].Eps = kykze0.e0 + kykze0.ky * Fibers[i].Y + kykze0.kz * Fibers[i].X;
         }


         Diagramm dgr = Diagramms[calc];
         dgr.Sig(this, ten, ca);

         for (int i = 0; i < Hull.Points.Count; i++)
         {
            Hull.Points[i].Eps = kykze0.e0 + kykze0.ky * Hull.Points[i].Y + kykze0.kz * Hull.Points[i].X;
            dgr.Sig(Hull.Points[i], ten, ca);
         }
      }

      /// <summary>
      /// Вычисляет внутренние усилия от предварительного напряжения (преддеформации Eps_p)
      /// для заданного типа расчёта.
      /// </summary>
      /// <param name="calc">Тип расчёта (C, CL, N, NL).</param>
      /// <param name="tb">Учитывать работу на растяжение (для бетона).</param>
      /// <param name="ca">Учитывать работу на сжатие (для арматуры).</param>
      /// <returns>Нагрузка <see cref="Load"/> с усилиями от преднапряжения.</returns>
      public Load GetPreLoad(CalcType calc, bool tb, bool ca)
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

         return res;
      }

      /// <summary>
      /// Формирует объект <see cref="FiberRegionData"/> с массивами данных волокон
      /// для сериализации и обмена данными.
      /// </summary>
      /// <returns>Объект FiberRegionData, содержащий массивы координат, напряжений, деформаций и др.</returns>
      public FiberRegionData Data()
      {
         return new FiberRegionData(this);
      }

      /// <summary>
      /// Вычисляет внутренние усилия (N, My, Mz) в сечении путём интегрирования
      /// напряжений по волокнам. Задаёт деформации по кривизне, затем суммирует
      /// вклад каждого волокна.
      /// </summary>
      /// <param name="k">Кривизна плоскости деформаций.</param>
      /// <param name="calc">Тип расчёта (по умолчанию C).</param>
      /// <param name="ten">Учитывать работу на растяжение (по умолчанию true).</param>
      /// <param name="ca">Учитывать работу на сжатие (по умолчанию true).</param>
      /// <returns>Нагрузка <see cref="Load"/> с вычисленными усилиями.</returns>
      public virtual Load Integral(Kurvature k, CalcType calc = CalcType.C, bool ten = true, bool ca = true)
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
         return new Load() { Calc = calc, N = N, My = Mx, Mz = My };
      }

      /// <summary>
      /// Вычисляет внутренние усилия (N, My, Mz) и геометрические характеристики
      /// сечения с одновременным интегрированием напряжений по волокнам.
      /// </summary>
      /// <param name="k">Кривизна плоскости деформаций.</param>
      /// <param name="props">Выходной параметр: геометрические характеристики сечения.</param>
      /// <param name="calc">Тип расчёта (по умолчанию C).</param>
      /// <param name="ten">Учитывать работу на растяжение (по умолчанию true).</param>
      /// <param name="ca">Учитывать работу на сжатие (по умолчанию true).</param>
      /// <returns>Нагрузка <see cref="Load"/> с вычисленными усилиями.</returns>
      public virtual Load Integral(Kurvature k, out GeoProps props, CalcType calc = CalcType.C, bool ten = true, bool ca = true)
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
         return new Load() { Calc = calc, N = N, My = Mx, Mz = My };
      }

      /// <summary>
      /// Суммирует внутренние усилия (N, My, Mz) по всем волокнам без пересчёта
      /// деформаций. Используется, когда деформации и напряжения уже заданы.
      /// </summary>
      /// <returns>Нагрузка <see cref="Load"/> с текущими усилиями.</returns>
      public Load Integral()
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

         return new Load() { N = N, My = Mx, Mz = My };
      }

      /// <summary>
      /// Возвращает список волокон, у которых деформация положительна (растянутые волокна).
      /// </summary>
      /// <returns>Список растянутых волокон.</returns>
      public List<Fiber> TensileFibers()
      {
         if (Fibers == null) return new List<Fiber>();
         List<Fiber> s = new List<Fiber>();
         for (int i = 0; i < Fibers.Count; i++)
         {
            if (Fibers[i].Eps > 0) s.Add(Fibers[i]);
         }
         return s;
      }

      /// <summary>
      /// Вычисляет суммарную площадь растянутых волокон (деформация > 0).
      /// </summary>
      /// <returns>Суммарная площадь растянутых волокон. Возвращает 0, если волокон нет.</returns>
      public double AreaOfTensileFibers()
      {
         if (Fibers == null) return 0;
         List<Fiber> s = TensileFibers();
         double a = 0;
         for (int i = 0; i < s.Count; i++) a += s[i].Area;

         return a;
      }

      /// <summary>
      /// Сдвигает все волокна области на вектор смещения (прибавляет xy к координатам каждого волокна).
      /// </summary>
      public static FiberRegion operator +(FiberRegion fag, XY xy)
      {
         for (int i = 0; i < fag.Fibers.Count; i++)
         {
            fag.Fibers[i] += xy;
         }
         return fag;
      }

      /// <summary>
      /// Сдвигает все волокна области на вектор, обратный xy (вычитает xy из координат каждого волокна).
      /// </summary>
      public static FiberRegion operator -(FiberRegion fag, XY xy)
      {
         for (int i = 0; i < fag.Fibers.Count; i++)
         {
            fag.Fibers[i] -= xy;
         }
         return fag;
      }

      /// <summary>
      /// Масштабирует все волокна области на заданный коэффициент.
      /// </summary>
      public static FiberRegion operator *(FiberRegion fag, double xy)
      {
         for (int i = 0; i < fag.Fibers.Count; i++)
         {
            fag.Fibers[i] *= xy;
         }
         return fag;
      }

   }
}