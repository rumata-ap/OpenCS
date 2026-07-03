using System;
using System.Collections.Generic;

namespace CScore
{
   /// <summary>
   /// Формулы Приложения Г СП 63.13330 — аналитические ветви диаграммы σ-ε бетона.
   /// Восходящая ветвь: η ∈ [0, 1], нисходящая: η ∈ [0.85, 1).
   /// Знаковая конвенция: сжатие ε &lt; 0, σ &lt; 0.
   /// </summary>
   internal static class SP63
   {
      const int N_ASC  = 50;   // точек на восходящей ветви  (η = 0..1)
      const int N_DESC = 20;   // точек на нисходящей ветви  (η = 0.85..1, без пика)

      // ε̂_b по формуле Г.2 (единицы B — МПа, E — кПа)
      static double GetEps0(MaterialChars b, double lam)
      {
         double e0 = Math.Abs(b.Ec0);
         if (b.Class > 0 && b.TypeCalc != CalcType.NL)
         {
            double B    = b.Class;
            double Empa = b.E / 1000.0;
            e0 = (B / Empa) * lam
                 * ((1.0 + 0.75 * lam * B / 60.0 + 0.2 * lam / B)
                    / (0.12 + B / 60.0 + 0.2 / B));
         }
         return e0;
      }

      /// <summary>
      /// Восходящая ветвь сжатия: η от 0 до 1, формулы Г.2, Г.5, Г.6.
      /// Возвращает три списка: деформации, напряжения, нули (d не используется).
      /// </summary>
      internal static List<double>[] UpBranch(MaterialChars beton, double lambda = 1)
      {
         double E      = beton.E;
         double Rb     = Math.Abs(beton.Fc);
         double e0     = GetEps0(beton, lambda);
         double nu_hat = Rb / (e0 * E);          // ν̂_b
         double w1     = 2.0 - 2.5 * nu_hat;    // ω₁ (восходящая)
         double w2     = 1.0 - w1;               // ω₂

         var eps = new List<double>(N_ASC + 1);
         var sig = new List<double>(N_ASC + 1);
         var dum = new List<double>(N_ASC + 1);

         for (int i = 0; i <= N_ASC; i++)
         {
            double eta = i / (double)N_ASC;
            if (i == 0) { eps.Add(0); sig.Add(0); dum.Add(0); continue; }

            // inner = (1-η)(1+ω₂·η) — гарантированно ≥ 0 при η∈[0,1]
            double inner = (1.0 - eta) * (1.0 + w2 * eta);
            double nu    = nu_hat + (1.0 - nu_hat) * Math.Sqrt(inner);
            nu = Math.Max(nu, 1e-12);

            double s = eta * Rb;
            double e = s / (E * nu);
            eps.Add(-e);
            sig.Add(-s);
            dum.Add(0);
         }
         return new[] { eps, sig, dum };
      }

      /// <summary>
      /// Нисходящая ветвь сжатия: η от <paramref name="etaMin"/> до 1 (без точки η=1 — она есть в восходящей),
      /// формулы Г.2, Г.5, Г.7. СП 63.13330 п. Г.1: нисходящая ветвь допустима до η ≥ 0.85.
      /// </summary>
      /// <param name="etaMin">Нижняя граница нисходящей ветви (0 &lt; etaMin &lt; 1, рекомендуется 0.85 по СП 63).</param>
      internal static List<double>[] DownBranch(MaterialChars beton, double lambda = 1, double etaMin = 0.85)
      {
         double E      = beton.E;
         double Rb     = Math.Abs(beton.Fc);
         double e0     = GetEps0(beton, lambda);
         double nu_hat = Rb / (e0 * E);
         double nu_0   = 2.05 * nu_hat;          // ν₀ (нисходящая)
         double w1     = 1.95 * nu_hat - 0.138;  // ω₁ (нисходящая)
         double w2     = 1.0 - w1;

         etaMin = Math.Max(0.01, Math.Min(0.99, etaMin));

         var eps = new List<double>(N_DESC);
         var sig = new List<double>(N_DESC);
         var dum = new List<double>(N_DESC);

         // η от etaMin до (1 - шаг), без η=1 (пик уже в UpBranch)
         for (int i = 0; i < N_DESC; i++)
         {
            double eta   = etaMin + (1.0 - etaMin) * i / N_DESC;
            double inner = Math.Max(0.0, 1.0 - w1 * eta - w2 * eta * eta);
            double nu    = nu_hat - (nu_0 - nu_hat) * Math.Sqrt(inner);
            if (nu < 1e-9) continue;   // ν_m слишком мало

            double s = eta * Rb;
            double e = s / (E * nu);
            eps.Add(-e);
            sig.Add(-s);
            dum.Add(0);
         }
         return new[] { eps, sig, dum };
      }
   }
}
