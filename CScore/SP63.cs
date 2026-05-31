using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CScore
{
   internal class SP63
   {
      /// <summary>
      /// Строит восходящую ветвь диаграммы сжатия бетона.
      /// </summary>
      /// <param name="beton">Характеристики материала бетона.</param>
      /// <param name="lambda">Коэффициент, влияющий на форму диаграммы (по умолчанию 1).</param>
      /// <returns>Массив списков, содержащий значения деформаций, напряжений и касательных модулей деформаций.</returns>
      /// <remarks>
      /// Метод рассчитывает значения деформаций, напряжений и жесткостей для восходящей ветви диаграммы сжатия бетона
      /// на основе характеристик материала и коэффициента lambda. Возвращаемый массив содержит три списка:
      /// - Список деформаций (e)
      /// - Список напряжений (s)
      /// - Список касательных модулей деформаций (d)
      /// </remarks>
      internal static List<double>[] UpBranch(MaterialChars beton, double lambda = 1)
      {
         double E = beton.E; // кПа
         double Rb = Math.Abs(beton.Fc); // кПа
         double e0 = Math.Abs(beton.Ec0);
         if (beton.Class > 0 && beton.TypeCalc != CalcType.NL)
         {
            double B = beton.Class;
            e0 = (B / (E / 1000.0)) * lambda * ((1 + 0.75 * lambda * B / 60 + 0.2 * lambda / B) / (0.12 + B / 60 + 0.2 / B));
         }
         double v0 = 1;
         double vb_ = Rb / (e0 * E);
         double w1 = 2 - 2.5 * vb_;
         double w2 = 1 - w1;
         List<double> s = new List<double>(4);
         List<double> e = new List<double>(4);
         List<double> d = new List<double>(4);
         double[] nu = { 0, 0.25, 0.5, 0.75 };
         for (int i = 0; i < 4; i++)
         {
            double vb = vb_ + (v0 - vb_) * Math.Sqrt(1 - w1 * nu[i] - w2 * Math.Pow(nu[i], 2));
            double sig = nu[i] * Rb;
            double eps = sig / (E * vb);
            double vb_k = 1 / (1 / vb + (sig * (v0 - vb_) * (w1 + 2 * w2 * nu[i])) /
               (2 * vb * vb * Rb * Math.Sqrt(1 - w1 * nu[i] - w2 * Math.Pow(nu[i], 2))));
            s.Add(-sig);
            e.Add(-eps);
            d.Add(vb_k * E);
         }
         s.Add(-Rb);
         e.Add(-e0);
         d.Add(0);
         return new List<double>[] { e, s, d };
      }

      internal static List<double>[] DownBranch(MaterialChars beton, double lambda = 1)
      {
         double E = beton.E; // кПа
         double Rb = Math.Abs(beton.Fc); // кПа
         double e0 = Math.Abs(beton.Ec0);
         if (beton.Class > 0 && beton.TypeCalc != CalcType.NL)
         {
            double B = beton.Class;
            e0 = (B / (E / 1000.0)) * lambda * ((1 + 0.75 * lambda * B / 60 + 0.2 * lambda / B) / (0.12 + B / 60 + 0.2 / B));
         }
         double vb_ = Rb / (e0 * E);
         double v0 = 2.05 * vb_;
         double w1 = 1.95 * vb_ - 0.138;
         double w2 = 1 - w1;
         List<double> s = new List<double>(6);
         List<double> e = new List<double>(6);
         List<double> d = new List<double>(6);
         double[] nu = { 0.95, 0.9, 0.85, 0.8, 0.5, 0.2 };
         for (int i = 0; i < 6; i++)
         {
            double vb = vb_ - (v0 - vb_) * Math.Sqrt(1 - w1 * nu[i] - w2 * Math.Pow(nu[i], 2));
            double sig = nu[i] * Rb;
            double eps = sig / (E * vb);
            double vb_k = 1 / (1 / vb - sig * (v0 - vb_) * (w1 + 2 * w2 * nu[i]) /
               (2 * vb * vb * Rb * Math.Sqrt(1 - w1 * nu[i] - w2 * Math.Pow(nu[i], 2))));
            s.Add(-sig);
            e.Add(-eps);
            d.Add(vb_k * E);
         }

         return new List<double>[] { e, s, d };
      }
   }
}
