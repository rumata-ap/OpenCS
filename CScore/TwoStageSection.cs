using System;
using System.Text.Json.Serialization;

namespace CScore
{
   /// <summary>
   /// Двухэтапное поперечное сечение — для сборно-монолитных конструкций
   /// и усиления ЖБ сечений.
   /// Этап 1: замороженная кривизна плоскости деформаций Stage1Kurvature.
   /// Деформации волокон этапа 1: ε_total = ε_current + ε_stage1.
   /// Этап 2 (Areas из базового CrossSection): ε_total = ε_current.
   /// </summary>
   [Serializable]
   public class TwoStageSection : CrossSection
   {
      /// <summary>Сечение первого этапа (до усиления / омоноличивания).</summary>
      public CrossSection Stage1 { get; set; } = new();

      /// <summary>Id сечения первого этапа в БД.</summary>
      public int Stage1SectionId { get; set; }

      /// <summary>
      /// Замороженная кривизна плоскости деформаций от нагрузки первого этапа.
      /// Задаётся после предварительного расчёта сечения первого этапа.
      /// </summary>
      public Kurvature Stage1Kurvature { get; set; }

      public TwoStageSection() { }

      /// <inheritdoc/>
      public override void SetEps(Kurvature k, CalcType calc,
                                   bool ten = true, bool ca = true)
      {
         Kurvature k1 = k + Stage1Kurvature;
         foreach (var area in Stage1.Areas)
            area.SetEps(k1, calc, ten, ca);
         foreach (var area in Areas)
            area.SetEps(k, calc, ten, ca);
      }

      /// <inheritdoc/>
      public override Load Integral(Kurvature k, CalcType calc = CalcType.C,
                                     bool ten = true, bool ca = true)
      {
         double N = 0, Mx = 0, My = 0;

         // Этап 1: ε_total = ε_current + ε_stage1 (замороженная маска)
         Kurvature k1 = k + Stage1Kurvature;
         foreach (var area in Stage1.Areas)
         {
            area.SetEps(k1, calc, ten, ca);
            foreach (var f in area.Fibers)
            { N += f.N; Mx += f.Mx; My += f.My; }
         }

         // Этап 2: ε_total = ε_current
         foreach (var area in Areas)
         {
            area.SetEps(k, calc, ten, ca);
            foreach (var f in area.Fibers)
            { N += f.N; Mx += f.Mx; My += f.My; }
         }

         return new Load { Calc = calc, N = N, Mx = Mx, My = My };
      }
   }
}
