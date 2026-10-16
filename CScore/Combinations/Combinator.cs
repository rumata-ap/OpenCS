using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore.Combinations
{
   /// <summary>
   /// Генератор огибающей расчётных комбинаций усилий по СП 20.13330 (жадный алгоритм).
   ///
   /// Основное сочетание — один проход по формуле (6.1): постоянные с γf, длительные по рангу степени влияния
   /// с ψ 1,0/0,95, кратковременные с ψ 1,0/0,9/0,7 (<see cref="CombRules.Psi"/>).
   /// Особое сочетание — постоянные и длительные с γf = 1, одна особая, кратковременные с γf = 0,5 и ψ = 0,8.
   ///
   /// Взаимоисключение: из загружений с одинаковым Group в комбинацию попадает только одно.
   /// </summary>
   public class Combinator
   {
      readonly List<Loading> _permanent;
      readonly List<Loading> _longTerm;
      readonly List<Loading> _shortTerm;
      readonly List<Loading> _accidental;
      readonly CombRules     _rules;
      readonly int           _nSections;
      readonly int           _nComponents;
      readonly string[]      _componentNames;

      public IReadOnlyList<Loading> Loadings { get; }

      public Combinator(IList<Loading> loadings, CombRules rules)
      {
         if (loadings == null || loadings.Count == 0)
            throw new ArgumentException("Список загружений не может быть пустым.");
         ValidateCompatibility(loadings);

         Loadings        = loadings.ToList();
         _rules          = rules;
         _nSections      = loadings[0].NSections;
         _nComponents    = loadings[0].NComponents;
         _componentNames = loadings[0].ComponentNames;

         _permanent  = loadings.Where(l => l.LoadType == NormLoadType.Permanent).ToList();
         _longTerm   = loadings.Where(l => l.LoadType == NormLoadType.LongTerm).ToList();
         _shortTerm  = loadings.Where(l => l.LoadType == NormLoadType.ShortTerm).ToList();
         _accidental = loadings.Where(l => l.LoadType == NormLoadType.Accidental).ToList();
      }

      // ----------------------------------------------------------------
      // Публичные методы
      // ----------------------------------------------------------------

      /// <summary>Вычислить огибающую расчётных комбинаций.</summary>
      public Envelope FullEnvelope()
      {
         int ns = _nSections, nc = _nComponents;

         var maxVal = NewMatrix(nc, ns, double.NegativeInfinity);
         var minVal = NewMatrix(nc, ns, double.PositiveInfinity);
         var maxF   = new double[nc, ns, nc];
         var minF   = new double[nc, ns, nc];
         var maxAct = new Dictionary<string, double>?[nc, ns];
         var minAct = new Dictionary<string, double>?[nc, ns];

         var combType = _rules.CombType;
         {
            for (int s = 0; s < ns; s++)
               for (int k = 0; k < nc; k++)
                  foreach (int sign in new[] { +1, -1 })
                  {
                     var (f, act, roles) = Greedy(s, k, sign, combType);
                     double val = f[k];
                     if (sign == +1)
                     {
                        if (val > maxVal[k, s])
                        {
                           maxVal[k, s] = val;
                           for (int j = 0; j < nc; j++) maxF[k, s, j] = f[j];
                           maxAct[k, s] = act;
                        }
                     }
                     else
                     {
                        if (val < minVal[k, s])
                        {
                           minVal[k, s] = val;
                           for (int j = 0; j < nc; j++) minF[k, s, j] = f[j];
                           minAct[k, s] = act;
                        }
                     }
                  }
         }

         return new Envelope(_componentNames, ns, maxVal, minVal, maxF, minF, maxAct, minAct);
      }

      /// <summary>Вычислить огибающую и вернуть список всех сгенерированных комбинаций (алгоритм fast).</summary>
      public (Envelope Envelope, List<GeneratedCase> Cases) FullEnvelopeWithCases()
      {
         int ns = _nSections, nc = _nComponents;

         var maxVal = NewMatrix(nc, ns, double.NegativeInfinity);
         var minVal = NewMatrix(nc, ns, double.PositiveInfinity);
         var maxF   = new double[nc, ns, nc];
         var minF   = new double[nc, ns, nc];
         var maxAct = new Dictionary<string, double>?[nc, ns];
         var minAct = new Dictionary<string, double>?[nc, ns];
         var cases  = new List<GeneratedCase>();

         var combType = _rules.CombType;
         {
            for (int s = 0; s < ns; s++)
               for (int k = 0; k < nc; k++)
                  foreach (int sign in new[] { +1, -1 })
                  {
                     var (f, act, roles) = Greedy(s, k, sign, combType);

                     cases.Add(new GeneratedCase
                     {
                        CombType    = combType,
                        LeadingType = NormLoadType.ShortTerm,
                        Section     = s,
                        Component   = _componentNames[k],
                        Sign        = sign,
                        Forces      = (double[])f.Clone(),
                        Active      = new Dictionary<string, double>(act),
                        Roles       = new Dictionary<string, Sp20TermRole>(roles)
                     });

                     double val = f[k];
                     if (sign == +1)
                     {
                        if (val > maxVal[k, s])
                        {
                           maxVal[k, s] = val;
                           for (int j = 0; j < nc; j++) maxF[k, s, j] = f[j];
                           maxAct[k, s] = act;
                        }
                     }
                     else
                     {
                        if (val < minVal[k, s])
                        {
                           minVal[k, s] = val;
                           for (int j = 0; j < nc; j++) minF[k, s, j] = f[j];
                           minAct[k, s] = act;
                        }
                     }
                  }
         }

         var env = new Envelope(_componentNames, ns, maxVal, minVal, maxF, minF, maxAct, minAct);
         return (env, cases);
      }

      // ----------------------------------------------------------------
      // Жадный алгоритм для одного (s, k, sign, combType)
      // ----------------------------------------------------------------

      (double[] f, Dictionary<string, double> active, Dictionary<string, Sp20TermRole> roles) Greedy(
         int s, int k, int sign, CombType combType)
      {
         double[] f     = new double[_nComponents];
         var active     = new Dictionary<string, double>();
         var roles      = new Dictionary<string, Sp20TermRole>();

         // 1. Постоянные нагрузки
         foreach (var ld in _permanent)
         {
            // СП 20.13330 п. 4.2в: γf = 1,0 в особом сочетании; иначе пониженный γf (п. 7.3), если вес разгружает
            bool reduced = combType == CombType.Fundamental && ld.Forces[s, k] * sign < 0.0;
            double gf = combType == CombType.Accidental ? 1.0 : reduced ? ld.GammaFFav : ld.GammaFUnfav;
            AddScaled(f, gf, ld.Forces, s);
            active[ld.Name] = gf;
            roles[ld.Name] = reduced ? Sp20TermRole.PermanentReduced : Sp20TermRole.Permanent;
         }

         if (combType == CombType.Accidental)
         {
            // 2. Одна особая нагрузка с наибольшим вкладом
            Loading? specLd = null;
            double best = 0.0;
            foreach (var ld in _accidental)
            {
               double c = ld.Forces[s, k] * sign;
               if (c > best) { best = c; specLd = ld; }
            }
            if (specLd != null)
            {
               AddScaled(f, 1.0, specLd.Forces, s);
               active[specLd.Name] = 1.0;
               roles[specLd.Name] = Sp20TermRole.Accidental;
            }

            // Длительные: γf = 1,0 (п. 4.2в), ψ по рангу (п. 6.3); кратковременные: γf = 0,5, ψ = 0,8 (п. 6.5)
            PickRanked(_longTerm, s, k, sign, 1.0, NormLoadType.LongTerm, combType, f, active, roles);
            PickRanked(_shortTerm, s, k, sign, CombRules.AccidentalShortTermGamma, NormLoadType.ShortTerm, combType,
               f, active, roles);
            return (f, active, roles);
         }

         // 3. Основное сочетание по (6.1): длительные по рангу 1,0/0,95 (п. 6.3), кратковременные 1,0/0,9/0,7 (п. 6.4)
         PickRanked(_longTerm, s, k, sign, null, NormLoadType.LongTerm, combType, f, active, roles);
         PickRanked(_shortTerm, s, k, sign, null, NormLoadType.ShortTerm, combType, f, active, roles);
         return (f, active, roles);
      }

      // ----------------------------------------------------------------
      // Набор временных нагрузок по рангу степени влияния
      // ----------------------------------------------------------------

      void PickRanked(
         List<Loading> pool, int s, int k, int sign, double? gammaOverride, NormLoadType type, CombType combType,
         double[] f, Dictionary<string, double> active, Dictionary<string, Sp20TermRole> roles)
      {
         // 1. Сжать взаимоисключающие группы: из группы — нагрузка с наибольшим вкладом
         var candidates = new List<Loading>();
         var byGroup = new Dictionary<string, List<Loading>>();

         foreach (var ld in pool)
         {
            if (ld.Group != null)
            {
               if (!byGroup.TryGetValue(ld.Group, out var grp))
                  byGroup[ld.Group] = grp = [];
               grp.Add(ld);
            }
            else
               candidates.Add(ld);
         }

         foreach (var (_, members) in byGroup)
         {
            var best = members.OrderByDescending(l =>
               (gammaOverride ?? l.GammaFUnfav) * l.Forces[s, k] * sign).First();
            candidates.Add(best);
         }

         // 2. Оставить полезные и отсортировать по вкладу
         var scored = new List<(double contrib, Loading ld)>();
         foreach (var ld in candidates)
         {
            double gamma = gammaOverride ?? ld.GammaFUnfav;
            double contrib = gamma * ld.Forces[s, k] * sign;
            if (contrib > 0) scored.Add((contrib, ld));
         }
         scored.Sort((a, b) => b.contrib.CompareTo(a.contrib));

         // 3. Назначить ψ по рангу и суммировать
         for (int idx = 0; idx < scored.Count; idx++)
         {
            var (_, ld) = scored[idx];
            var role = CombRules.RoleOfRank(idx);
            double factor = (gammaOverride ?? ld.GammaFUnfav) * CombRules.Psi(type, role, combType);
            AddScaled(f, factor, ld.Forces, s);
            active[ld.Name] = factor;
            roles[ld.Name] = role;
         }
      }

      // ----------------------------------------------------------------
      // Вспомогательные
      // ----------------------------------------------------------------

      static void AddScaled(double[] acc, double factor, double[,] forces, int row)
      {
         int nc = acc.Length;
         for (int j = 0; j < nc; j++) acc[j] += factor * forces[row, j];
      }

      static double[,] NewMatrix(int rows, int cols, double fill)
      {
         var m = new double[rows, cols];
         for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) m[i, j] = fill;
         return m;
      }

      static void ValidateCompatibility(IList<Loading> loadings)
      {
         var ref0 = loadings[0];
         foreach (var ld in loadings.Skip(1))
         {
            if (ld.NSections != ref0.NSections)
               throw new ArgumentException(
                  $"Загружение '{ld.Name}': NSections={ld.NSections} " +
                  $"не совпадает с '{ref0.Name}': NSections={ref0.NSections}.");
            if (!ld.ComponentNames.SequenceEqual(ref0.ComponentNames))
               throw new ArgumentException(
                  $"Загружение '{ld.Name}': ComponentNames не совпадают с '{ref0.Name}'.");
         }
         var names = loadings.Select(l => l.Name).ToList();
         if (names.Count != names.Distinct().Count())
            throw new ArgumentException("Имена загружений должны быть уникальными.");
      }
   }
}
