using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CScore
{
   /// <summary>
   /// Поперечное сечение — контейнер материальных областей с единым интегралом.
   /// Заменяет RCFiberRegion как контейнер. Минимум одна MaterialArea.
   /// </summary>
   [Serializable]
   public class CrossSection
   {
      public int Id { get; set; }
      public int Num { get; set; }
      public string Tag { get; set; } = "";
      public string? Description { get; set; }

      public List<MaterialArea> Areas { get; set; } = [];

      public CrossSection() { }

      public override string ToString() => $"{Num:D3}#CrossSection : {Tag}";

      /// <summary>
      /// Вычисляет деформации и напряжения во всех областях по кривизне.
      /// </summary>
      public virtual void SetEps(Kurvature k, CalcType calc,
                                  bool ten = true, bool ca = true)
      {
         foreach (var area in Areas)
            area.SetEps(k, calc, ten, ca);
      }

      /// <summary>
      /// Единый интеграл по всем областям (бетон + арматура с разностными диаграммами).
      /// Если у области нет сеточных фибр и задан Hull — используется контурный путь (теорема Грина).
      /// Иначе — фибровое суммирование.
      /// </summary>
      public virtual Load Integral(Kurvature k, CalcType calc = CalcType.C,
                                    bool ten = true, bool ca = true)
      {
         double N = 0, Mx = 0, My = 0;
         foreach (var area in Areas)
         {
            bool hasMeshFibers = area.Fibers.Any(f => f.TypeFiber != FiberType.point);

            if (!hasMeshFibers && area.Hull != null && area.Diagramms.ContainsKey(calc))
            {
               // Контурный путь для полигонной части
               var (n, mx, my) = area.ContourIntegral(k, calc, ten, ca);
               N += n; Mx += mx; My += my;

               // Точечные фибры в этой области (если есть) — через SetEps
               if (area.Fibers.Count > 0)
               {
                  area.SetEps(k, calc, ten, ca);
                  foreach (var f in area.Fibers)
                  { N += f.N; Mx += f.Mx; My += f.My; }
               }
            }
            else
            {
               // Фибровый путь — SetEps обрабатывает все фибры (mesh + point)
               area.SetEps(k, calc, ten, ca);
               foreach (var f in area.Fibers)
               { N += f.N; Mx += f.Mx; My += f.My; }
            }
         }
         return new Load { Calc = calc, N = N, Mx = Mx, My = My };
      }

      /// <summary>Интеграл + геометрические характеристики.</summary>
      public virtual Load Integral(Kurvature k, out GeoProps props,
                                    CalcType calc = CalcType.C,
                                    bool ten = true, bool ca = true)
      {
         var load = Integral(k, calc, ten, ca);
         props = new GeoProps(this);
         return load;
      }

      /// <summary>
      /// Начальное приближение кривизны (упругая стадия).
      /// Для областей без сеточных фибр использует геометрию контура Hull,
      /// чтобы не занижать жёсткость сечения.
      /// </summary>
      public Kurvature Guess(Load load)
      {
         var pr = new GeoProps();
         foreach (var area in Areas)
         {
            bool hasMeshFibers = area.Fibers.Any(f => f.TypeFiber != FiberType.point);
            if (!hasMeshFibers && area.Hull != null && area.Material != null)
            {
               double E = area.Material.E;
               // Внешний контур
               var ap = new GeoProps(area.Hull, E);
               pr = pr + ap;
               // Вычесть отверстия
               foreach (var hole in area.Holes)
                  pr = pr - new GeoProps(hole, E);
            }
            // Фибровый вклад (арматурные точки, сгенерированная сетка)
            pr = pr + new GeoProps(area);
         }

         // Load.Mx = ∫σ·y·dA → ky (dε/dy), жёсткость EIx = E·∫y²dA
         // Load.My = ∫σ·x·dA → kz (dε/dx), жёсткость EIy = E·∫x²dA
         double ea  = pr.EA  > 1e-10 ? pr.EA  : 1.0;
         double eix = pr.EIx > 1e-10 ? pr.EIx : 1.0;
         double eiy = pr.EIy > 1e-10 ? pr.EIy : 1.0;

         return new Kurvature
         {
            e0 = load.N  / ea,
            ky = load.Mx / eix,
            kz = load.My / eiy
         };
      }

      /// <summary>
      /// Строит диаграммы для всех областей после загрузки из БД.
      /// Сначала — области без HostArea (бетонные), затем — с HostArea (арматурные).
      /// </summary>
      public void ResolveAndBuildDiagramms()
      {
         foreach (var area in Areas)
            if (area.HostAreaId == null)
               area.ResolveAndBuildDiagramms();

         foreach (var area in Areas)
            if (area.HostAreaId != null)
            {
               area.HostArea = Areas.Find(a => a.Id == area.HostAreaId);
               area.ResolveAndBuildDiagramms();
            }
      }
   }
}
