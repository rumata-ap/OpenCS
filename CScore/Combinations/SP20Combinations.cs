using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CScore.Combinations
{
   /// <summary>
   /// Адаптер между ForceSet и алгоритмом Combinator.
   /// Порт GreenSectionPy / greensection/core/sp20_combinations.py.
   ///
   /// Соглашение об именовании наборов усилий:
   ///   "G: Собственный вес"           — постоянная (γf по умолчанию 1.1/0.9)
   ///   "L: Полезная нагрузка"         — длительная переменная
   ///   "Q: Снег"                      — кратковременная переменная
   ///   "Q(γf=1.0): Ветер [ветер]"     — кратковременная, γf=1.0, группа "ветер"
   ///   "A: Взрыв"                     — особая
   /// </summary>
   public static class SP20Combinations
   {
      static readonly string[] BarKeys   = ["N", "Mx", "My", "Vx", "Vy", "T"];
      static readonly string[] ShellKeys = ["Nx", "Ny", "Nxy", "Mx", "My", "Mxy", "Qx", "Qy"];

      // Regex: "Q(γf=1.4): Снег [снег]"
      static readonly Regex LabelRe = new(
         @"^\s*(?<prefix>[GLQA])" +
         @"(?:\s*\(\s*(?:γf\s*=\s*)?(?<gamma>[0-9]+(?:[.,][0-9]+)?)\s*\))?" +
         @"\s*:\s*(?<title>.*?)(?:\s*\[(?<group>[^\]]+)\]\s*)?$",
         RegexOptions.IgnoreCase | RegexOptions.Compiled);

      // ----------------------------------------------------------------
      // Разбор имени набора
      // ----------------------------------------------------------------

      /// <summary>
      /// Разобрать имя ForceSet вида "Q(γf=1.0): Ветер [ветер]".
      /// Возвращает (loadType, title, group, gammaF, warning).
      /// </summary>
      public static (NormLoadType Type, string Title, string? Group, double? GammaF, string? Warning)
         ParseForceSetName(string name)
      {
         var m = LabelRe.Match(name ?? "");
         if (!m.Success)
         {
            return (NormLoadType.ShortTerm,
                    (name ?? "—").Trim().Length > 0 ? name!.Trim() : "—",
                    null, null,
                    $"Имя набора '{name}': не распознан префикс типа нагрузки. " +
                    "Ожидается 'G:', 'L:', 'Q:' или 'A:'. Принято как кратковременная (Q).");
         }

         string prefix = m.Groups["prefix"].Value.ToUpperInvariant();
         string title  = m.Groups["title"].Value.Trim();
         if (title.Length == 0) title = (name ?? "—").Trim();
         string? group  = m.Groups["group"].Value.Trim().NullIfEmpty();
         string  gammaS = m.Groups["gamma"].Value.Trim().Replace(',', '.');
         double? gamma  = null;
         if (gammaS.Length > 0 && double.TryParse(gammaS,
               System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out double g))
            gamma = g;

         NormLoadType lt = prefix switch
         {
            "G" => NormLoadType.Permanent,
            "L" => NormLoadType.LongTerm,
            "Q" => NormLoadType.ShortTerm,
            "A" => NormLoadType.Accidental,
            _   => NormLoadType.ShortTerm
         };

         string? warn = prefix is not ("G" or "L" or "Q" or "A")
            ? $"Имя набора '{name}': неизвестный префикс '{prefix}'. Принято как кратковременная (Q)."
            : null;

         return (lt, title, group, gamma, warn);
      }

      // ----------------------------------------------------------------
      // Ключи компонент по типу набора
      // ----------------------------------------------------------------

      public static string[] ComponentKeysFor(string kind) =>
         kind == "shell" ? ShellKeys : BarKeys;

      // ----------------------------------------------------------------
      // ForceSet → Loading
      // ----------------------------------------------------------------

      /// <summary>
      /// Преобразовать список ForceSet в список Loading.
      /// Все наборы должны быть одного kind и одинакового размера (число строк).
      /// Возвращает (loadings, warnings).
      /// </summary>
      public static (List<Loading> Loadings, List<string> Warnings)
         ForceSetsToLoadings(IList<ForceSet> forceSets, Sp20GammaDefaults? gammaDefaults = null)
      {
         var warnings = new List<string>();
         var g = gammaDefaults ?? Sp20GammaDefaults.Standard;

         if (forceSets == null || forceSets.Count == 0)
            return ([], ["Не выбран ни один набор усилий."]);

         string kind = forceSets[0].Kind;
         if (kind == "punching")
            throw new InvalidOperationException(
               "Сочетания СП20 для наборов продавливания (punching) не поддерживаются.");

         int nRows = forceSets[0].Items.Count;
         foreach (var fs in forceSets)
         {
            if (fs.Kind != kind)
               throw new InvalidOperationException("Нельзя комбинировать наборы разных типов (bar/shell).");
            if (fs.Items.Count != nRows)
               throw new InvalidOperationException("Нельзя комбинировать наборы с разным числом строк.");
         }

         string[] keys   = ComponentKeysFor(kind);
         string[] cnames = keys;

         // Проверка совпадения меток строк
         var refLabels = forceSets[0].Items.Select(i => i.Label).ToArray();
         foreach (var fs in forceSets.Skip(1))
         {
            var mismatch = fs.Items
               .Select((item, idx) => (idx, item.Label))
               .Where(t => t.idx < refLabels.Length && t.Label != refLabels[t.idx])
               .Select(t => t.idx)
               .Take(12)
               .ToList();
            if (mismatch.Count > 0)
               warnings.Add(
                  $"Набор '{fs.Tag}': метки строк отличаются от '{forceSets[0].Tag}' " +
                  $"по индексам: [{string.Join(", ", mismatch)}]");
         }

         var loadings = new List<Loading>();
         foreach (var fs in forceSets)
         {
            var (lt, title, group, gammaF, warn) = ParseForceSetName(fs.Tag);
            if (warn != null) warnings.Add(warn);

            var mat = BuildMatrix(fs.Items, keys, nRows);

            var gammaKw = gammaF.HasValue ? gammaF.Value : (double?)null;
            Loading ld = lt switch
            {
               NormLoadType.Permanent   => Loading.Permanent(title, mat, cnames,
                  gammaFUnfav: gammaKw ?? g.PermanentUnfav, gammaFFav: g.PermanentFav, group: group),
               NormLoadType.LongTerm    => Loading.LongTerm(title, mat, cnames,
                  gammaFUnfav: gammaKw ?? g.LongTermUnfav, group: group),
               NormLoadType.ShortTerm   => Loading.ShortTerm(title, mat, cnames,
                  gammaFUnfav: gammaKw ?? g.ShortTermUnfav, group: group),
               NormLoadType.Accidental  => Loading.Accidental(title, mat, cnames,
                  gammaFUnfav: g.AccidentalUnfav, group: group),
               _ => Loading.ShortTerm(title, mat, cnames,
                  gammaFUnfav: gammaKw ?? g.ShortTermUnfav, group: group)
            };
            loadings.Add(ld);
         }

         return (loadings, warnings);
      }

      // ----------------------------------------------------------------
      // Envelope → ForceSet (огибающая)
      // ----------------------------------------------------------------

      /// <summary>
      /// Конвертировать огибающую в ForceSet (глобальные экстремумы, одна строка на max/min компоненты).
      /// </summary>
      public static ForceSet EnvelopeToForceSet(
         Envelope env, string kind, string tag, string labelPrefix)
      {
         var fs = new ForceSet { Tag = tag, Kind = kind };
         string[] keys = env.ComponentNames;
         int nc = keys.Length;

         foreach (string comp in keys)
         {
            int k     = Array.IndexOf(keys, comp);
            double[] maxVals = env.MaxValue(comp);
            double[] minVals = env.MinValue(comp);

            // Сечение с глобальным max и min по компоненте
            int sMax = IndexOfMax(maxVals);
            int sMin = IndexOfMin(minVals);

            var maxItem = new LoadItem { Label = $"{labelPrefix} max {comp} (sec={sMax})" };
            SetItemFields(maxItem, keys, GetForceVec(env.MaxForces, k, sMax, nc));
            fs.Items.Add(maxItem);

            var minItem = new LoadItem { Label = $"{labelPrefix} min {comp} (sec={sMin})" };
            SetItemFields(minItem, keys, GetForceVec(env.MinForces, k, sMin, nc));
            fs.Items.Add(minItem);
         }
         RenumberItems(fs);
         return fs;
      }

      // ----------------------------------------------------------------
      // Cases → ForceSet (список комбинаций)
      // ----------------------------------------------------------------

      /// <summary>Список сгенерированных комбинаций → ForceSet (одна строка на комбинацию).</summary>
      public static ForceSet CasesToForceSet(
         IList<GeneratedCase> cases, string kind, string tag)
      {
         var fs   = new ForceSet { Tag = tag, Kind = kind };
         string[] keys = cases.Count > 0 ? GetCombComponentNames(cases[0]) : BarKeys;

         foreach (var c in cases)
         {
            string sign   = c.Sign > 0 ? "max" : "min";
            string active = FormatActive(c.Active);
            string lbl    = $"{c.CombType} sec={c.Section} {sign} {c.Component}";
            if (active.Length > 0) lbl += $" [{active}]";

            var item = new LoadItem { Label = lbl };
            SetItemFields(item, keys, c.Forces);
            fs.Items.Add(item);
         }
         RenumberItems(fs);
         return fs;
      }

      // ----------------------------------------------------------------
      // Cases → ForceSet по формулам (вариант B)
      // ----------------------------------------------------------------

      /// <summary>
      /// Каждая уникальная формула (active dict) → отдельный ForceSet.
      /// Возвращает (forceSets, loadingNames, formulas).
      /// </summary>
      public static (List<ForceSet> Sets, List<string> LoadingNames, List<Dictionary<string, object>> Formulas)
         CasesToForceSetsByFormula(
            IList<Loading> loadings, IList<GeneratedCase> cases,
            string kind, string baseName, IList<string> rowLabels)
      {
         if (loadings.Count == 0) return ([], [], []);

         string[] keys     = loadings[0].ComponentNames;
         int nc            = keys.Length;
         int nSec          = loadings[0].NSections;
         var loadByName    = loadings.ToDictionary(l => l.Name);
         var loadNames     = loadings.Select(l => l.Name).ToList();

         // Группировка уникальных формул
         var groups = new Dictionary<string, Dictionary<string, object>>();
         foreach (var c in cases)
         {
            string gk = FormulaKey(c.Active);
            if (!groups.TryGetValue(gk, out var g))
               groups[gk] = g = new Dictionary<string, object>
               {
                  ["comb_type"] = c.CombType.ToString(),
                  ["coeffs"]    = new Dictionary<string, double>(c.Active),
                  ["count"]     = 0
               };
            g["count"] = (int)g["count"] + 1;
         }

         var formulas = groups.Values
            .OrderBy(d => d["comb_type"].ToString())
            .ThenBy(d => FormulaKey((Dictionary<string, double>)d["coeffs"]))
            .ToList();

         var outSets = new List<ForceSet>();
         for (int i = 0; i < formulas.Count; i++)
         {
            var coeffs = (Dictionary<string, double>)formulas[i]["coeffs"];
            // Вычислить матрицу (nSec, nc) = Σ coeff[name] * loading[name].Forces
            var mat = new double[nSec, nc];
            foreach (var (nm, fac) in coeffs)
            {
               if (!loadByName.TryGetValue(nm, out var ld)) continue;
               for (int s = 0; s < nSec; s++)
                  for (int j = 0; j < nc; j++)
                     mat[s, j] += fac * ld.Forces[s, j];
            }

            var fs = new ForceSet { Tag = $"{baseName} (комб {i + 1:D3})", Kind = kind };
            for (int s = 0; s < nSec; s++)
            {
               string lbl = s < rowLabels.Count ? rowLabels[s] : $"sec {s + 1}";
               var item   = new LoadItem { Label = lbl };
               double[] vec = new double[nc];
               for (int j = 0; j < nc; j++) vec[j] = mat[s, j];
               SetItemFields(item, keys, vec);
               fs.Items.Add(item);
            }
            RenumberItems(fs);
            outSets.Add(fs);
         }

         return (outSets, loadNames, formulas);
      }

      // ----------------------------------------------------------------
      // Главная точка входа
      // ----------------------------------------------------------------

      /// <summary>
      /// Построить огибающую и список комбинаций по СП20 из списка ForceSet.
      /// </summary>
      public static (Envelope Env, List<GeneratedCase> Cases, List<Loading> Loadings, List<string> Warnings)
         SP20EnvelopeAndCasesFromForceSets(IList<ForceSet> forceSets, CombType combType,
            Sp20GammaDefaults? gammaDefaults = null)
      {
         var (loadings, warnings) = ForceSetsToLoadings(forceSets, gammaDefaults);
         var rules = combType == CombType.Fundamental
            ? CombRules.SP20Fundamental()
            : CombRules.SP20Accidental();
         var (env, cases) = new Combinator(loadings, rules).FullEnvelopeWithCases();
         return (env, cases, loadings, warnings);
      }

      // ----------------------------------------------------------------
      // Вспомогательные
      // ----------------------------------------------------------------

      static double[,] BuildMatrix(List<LoadItem> items, string[] keys, int nRows)
      {
         var mat = new double[nRows, keys.Length];
         for (int s = 0; s < items.Count && s < nRows; s++)
         {
            var item = items[s];
            for (int j = 0; j < keys.Length; j++)
               mat[s, j] = GetFieldValue(item, keys[j]);
         }
         return mat;
      }

      static double GetFieldValue(LoadItem item, string key) => key switch
      {
         "N"   => item.N,
         "Mx"  => item.Mx,
         "My"  => item.My,
         "Vx"  => item.Vx,
         "Vy"  => item.Vy,
         "T"   => item.T,
         "Nx"  => item.N,   // shell mapped to closest bar field for now
         "Ny"  => item.Mx,
         "Nxy" => item.My,
         _     => 0.0
      };

      static void SetItemFields(LoadItem item, string[] keys, double[] vec)
      {
         for (int j = 0; j < keys.Length && j < vec.Length; j++)
         {
            switch (keys[j])
            {
               case "N":   item.N  = vec[j]; break;
               case "Mx":  item.Mx = vec[j]; break;
               case "My":  item.My = vec[j]; break;
               case "Vx":  item.Vx = vec[j]; break;
               case "Vy":  item.Vy = vec[j]; break;
               case "T":   item.T  = vec[j]; break;
            }
         }
      }

      static double[] GetForceVec(double[,,] forces, int k, int s, int nc)
      {
         var v = new double[nc];
         for (int j = 0; j < nc; j++) v[j] = forces[k, s, j];
         return v;
      }

      static string[] GetCombComponentNames(GeneratedCase c) =>
         c.Forces.Length == 6 ? BarKeys :
         c.Forces.Length == 8 ? ShellKeys :
         Enumerable.Range(0, c.Forces.Length).Select(i => $"F{i}").ToArray();

      static string FormatActive(Dictionary<string, double> active)
      {
         if (active.Count == 0) return "";
         return string.Join(", ", active.OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}={kv.Value:G3}"));
      }

      static string FormulaKey(Dictionary<string, double> active) =>
         string.Join("|", active.OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}={kv.Value:G6}"));

      static int IndexOfMax(double[] arr)
      {
         int idx = 0;
         for (int i = 1; i < arr.Length; i++) if (arr[i] > arr[idx]) idx = i;
         return idx;
      }

      static int IndexOfMin(double[] arr)
      {
         int idx = 0;
         for (int i = 1; i < arr.Length; i++) if (arr[i] < arr[idx]) idx = i;
         return idx;
      }

      static void RenumberItems(ForceSet fs)
      {
         for (int i = 0; i < fs.Items.Count; i++) fs.Items[i].Num = i + 1;
      }
   }

   static class StringExt
   {
      public static string? NullIfEmpty(this string? s) =>
         string.IsNullOrEmpty(s) ? null : s;
   }
}
