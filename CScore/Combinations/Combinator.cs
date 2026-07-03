using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore.Combinations
{
   /// <summary>
   /// Генератор огибающей расчётных комбинаций усилий по СП 20.13330 (жадный алгоритм).
   ///
   /// Pass A — ведущая кратковременная: одна кратковременная с ψ₁=1, остальные с ψ₂,
   ///          длительные с psi_long_acc.
   /// Pass B — ведущая длительная: одна длительная с ψ₁=1, остальные длительные с psi_long_acc,
   ///          кратковременные с psi_short_acc.
   /// Pass C — особое сочетание (если есть особые нагрузки).
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

         foreach (var (leadType, combType) in BuildPasses())
         {
            for (int s = 0; s < ns; s++)
               for (int k = 0; k < nc; k++)
                  foreach (int sign in new[] { +1, -1 })
                  {
                     var (f, act) = Greedy(s, k, sign, leadType, combType);
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

         foreach (var (leadType, combType) in BuildPasses())
         {
            for (int s = 0; s < ns; s++)
               for (int k = 0; k < nc; k++)
                  foreach (int sign in new[] { +1, -1 })
                  {
                     var (f, act) = Greedy(s, k, sign, leadType, combType);

                     cases.Add(new GeneratedCase
                     {
                        CombType    = combType,
                        LeadingType = leadType,
                        Section     = s,
                        Component   = _componentNames[k],
                        Sign        = sign,
                        Forces      = (double[])f.Clone(),
                        Active      = new Dictionary<string, double>(act)
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
      // Список проходов
      // ----------------------------------------------------------------

      List<(NormLoadType, CombType)> BuildPasses()
      {
         var passes = new List<(NormLoadType, CombType)>();

         if (_rules.CombType == CombType.Fundamental)
         {
            if (_shortTerm.Count > 0) passes.Add((NormLoadType.ShortTerm, CombType.Fundamental));
            if (_longTerm.Count  > 0) passes.Add((NormLoadType.LongTerm,  CombType.Fundamental));
            // Только постоянные — один формальный проход
            if (passes.Count == 0)
               passes.Add((NormLoadType.ShortTerm, CombType.Fundamental));
         }
         else // Accidental
         {
            passes.Add((NormLoadType.ShortTerm, CombType.Accidental));
         }
         return passes;
      }

      // ----------------------------------------------------------------
      // Жадный алгоритм для одного (s, k, sign, leadType, combType)
      // ----------------------------------------------------------------

      (double[] f, Dictionary<string, double> active) Greedy(
         int s, int k, int sign, NormLoadType leadingType, CombType combType)
      {
         double[] f     = new double[_nComponents];
         var active     = new Dictionary<string, double>();

         // 1. Постоянные нагрузки
         if (combType == CombType.Accidental)
         {
            // СП 20.13330 п. 4.3: γf=1.0 для всех в особом сочетании
            foreach (var ld in _permanent)
            {
               AddScaled(f, 1.0, ld.Forces, s);
               active[ld.Name] = 1.0;
            }
         }
         else
         {
            foreach (var ld in _permanent)
            {
               double gf = ld.Forces[s, k] * sign >= 0.0 ? ld.GammaFUnfav : ld.GammaFFav;
               AddScaled(f, gf, ld.Forces, s);
               active[ld.Name] = gf;
            }
         }

         // 2. Особое сочетание
         if (combType == CombType.Accidental)
         {
            // Выбираем одну особую нагрузку с наибольшим вкладом
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
            }

            // Длительные: γf=1.0, ψ=[1.0, 0.95, 0.95, ...]
            var psiLong = new double[] { 1.0 }.Concat(Repeat(0.95, 32)).ToArray();
            var (df1, da1) = PickRanked(_longTerm, s, k, sign, null, null, 1.0, psiLong);
            AddVec(f, df1); Merge(active, da1);

            // Кратковременные: γf=0.5, ψ=psiSpecialShort для всех
            var psiShort = Repeat(_rules.PsiSpecialShort, 64).ToArray();
            var (df2, da2) = PickRanked(_shortTerm, s, k, sign, null, null, 0.5, psiShort);
            AddVec(f, df2); Merge(active, da2);

            return (f, active);
         }

         // 3. Основное сочетание
         if (leadingType == NormLoadType.ShortTerm)
         {
            // Ведущая кратковременная: ψ=1.0
            var (leadLd, leadFactor) = PickBest(_shortTerm, s, k, sign, psi: 1.0);
            string? leadGroup = leadLd?.Group;
            if (leadLd != null)
            {
               AddScaled(f, leadFactor, leadLd.Forces, s);
               active[leadLd.Name] = leadFactor;
            }

            // Остальные кратковременные: ψ=0.9 для второй, 0.7 для остальных (п. 6.4)
            var psiST = new double[] { 0.9 }.Concat(Repeat(0.7, 64)).ToArray();
            var (df1, da1) = PickRanked(_shortTerm, s, k, sign, leadLd?.Name, leadGroup, null, psiST);
            AddVec(f, df1); Merge(active, da1);

            // Длительные: ψ=1.0 для первой, 0.95 для остальных (п. 6.3)
            var psiLT = new double[] { 1.0 }.Concat(Repeat(0.95, 64)).ToArray();
            var (df2, da2) = PickRanked(_longTerm, s, k, sign, null, null, null, psiLT);
            AddVec(f, df2); Merge(active, da2);

            return (f, active);
         }

         // Ведущая длительная: ψ=1.0
         {
            var (leadLd, leadFactor) = PickBest(_longTerm, s, k, sign, psi: 1.0);
            string? leadGroup = leadLd?.Group;
            if (leadLd != null)
            {
               AddScaled(f, leadFactor, leadLd.Forces, s);
               active[leadLd.Name] = leadFactor;
            }

            // Остальные длительные: ψ=0.95 (п. 6.3)
            var psiLT = Repeat(0.95, 64).ToArray();
            var (df1, da1) = PickRanked(_longTerm, s, k, sign, leadLd?.Name, leadGroup, null, psiLT);
            AddVec(f, df1); Merge(active, da1);

            // Кратковременные без ведущей: ψ=0.9 для первой, 0.7 для остальных (п. 6.4)
            var psiST = new double[] { 0.9 }.Concat(Repeat(0.7, 64)).ToArray();
            var (df2, da2) = PickRanked(_shortTerm, s, k, sign, null, null, null, psiST);
            AddVec(f, df2); Merge(active, da2);

            return (f, active);
         }
      }

      // ----------------------------------------------------------------
      // Выбор ведущей нагрузки
      // ----------------------------------------------------------------

      (Loading? lead, double factor) PickBest(
         List<Loading> pool, int s, int k, int sign, double psi)
      {
         double bestContrib = 0.0;
         Loading? bestLd = null;
         double bestFactor = 0.0;
         var seenGroups = new HashSet<string>();

         foreach (var ld in pool)
         {
            Loading candidate = ld;

            if (ld.Group != null)
            {
               if (seenGroups.Contains(ld.Group)) continue;
               // Лучший в группе
               candidate = pool
                  .Where(l => l.Group == ld.Group)
                  .OrderByDescending(l => l.GammaFUnfav * psi * l.Forces[s, k] * sign)
                  .First();
               seenGroups.Add(ld.Group);
            }

            double contrib = candidate.GammaFUnfav * psi * candidate.Forces[s, k] * sign;
            if (contrib > bestContrib)
            {
               bestContrib = contrib;
               bestLd      = candidate;
               bestFactor  = candidate.GammaFUnfav * psi;
            }
         }
         return (bestLd, bestFactor);
      }

      // ----------------------------------------------------------------
      // Набор сопровождающих нагрузок
      // ----------------------------------------------------------------

      (double[] f, Dictionary<string, double> active) PickRanked(
         List<Loading> pool, int s, int k, int sign,
         string? excludeName, string? excludeGroup,
         double? gammaOverride, double[] psiSeq)
      {
         double[] f = new double[_nComponents];
         var active = new Dictionary<string, double>();

         // 1. Сжать взаимоисключающие группы
         var candidates = new List<Loading>();
         var byGroup = new Dictionary<string, List<Loading>>();

         foreach (var ld in pool)
         {
            if (excludeName != null && ld.Name == excludeName) continue;
            if (excludeGroup != null && ld.Group != null && ld.Group == excludeGroup) continue;
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
            if (idx >= psiSeq.Length) break;
            var (_, ld) = scored[idx];
            double psi   = psiSeq[idx];
            double gamma = gammaOverride ?? ld.GammaFUnfav;
            double factor = gamma * psi;
            AddScaled(f, factor, ld.Forces, s);
            active[ld.Name] = factor;
         }

         return (f, active);
      }

      // ----------------------------------------------------------------
      // Вспомогательные
      // ----------------------------------------------------------------

      static void AddScaled(double[] acc, double factor, double[,] forces, int row)
      {
         int nc = acc.Length;
         for (int j = 0; j < nc; j++) acc[j] += factor * forces[row, j];
      }

      static void AddVec(double[] acc, double[] src)
      {
         for (int j = 0; j < acc.Length; j++) acc[j] += src[j];
      }

      static void Merge(Dictionary<string, double> dst, Dictionary<string, double> src)
      {
         foreach (var kv in src) dst[kv.Key] = kv.Value;
      }

      static double[,] NewMatrix(int rows, int cols, double fill)
      {
         var m = new double[rows, cols];
         for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) m[i, j] = fill;
         return m;
      }

      static IEnumerable<double> Repeat(double value, int count)
         => Enumerable.Repeat(value, count);

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
