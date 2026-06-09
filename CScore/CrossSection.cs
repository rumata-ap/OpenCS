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
      /// </summary>
      public virtual Load Integral(Kurvature k, CalcType calc = CalcType.C,
                                    bool ten = true, bool ca = true)
      {
         SetEps(k, calc, ten, ca);
         double N = 0, Mx = 0, My = 0;
         foreach (var area in Areas)
            foreach (var f in area.Fibers)
            { N += f.N; Mx += f.My; My += f.Mz; }
         return new Load { Calc = calc, N = N, My = Mx, Mz = My };
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

      /// <summary>Начальное приближение кривизны (упругая стадия).</summary>
      public Kurvature Guess(Load load)
      {
         var pr = new GeoProps(this);
         return new Kurvature
         {
            e0 = load.N / pr.EA,
            ky = load.My / pr.EIy,
            kz = load.Mz / pr.EIx
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
