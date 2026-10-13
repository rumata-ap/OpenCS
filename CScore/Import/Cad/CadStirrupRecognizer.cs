using System.Globalization;
using System.Text.RegularExpressions;

namespace CScore.Import.Cad;

/// <summary>Способ, которым хомут или шпилька нарисованы на поперечном сечении.</summary>
public enum CadStirrupShape
{
   /// <summary>Полилиния с шириной: ось — линия, диаметр — ширина.</summary>
   Wide = 0,
   /// <summary>Тонкая замкнутая линия по оси (или окружность): диаметр — из подписи или по умолчанию для слоя.</summary>
   Thin = 1,
   /// <summary>Две параллельные грани стержня: диаметр — зазор, ось — посередине.</summary>
   DoubleContour = 2,
   /// <summary>Незамкнутый хомут с крючками: замкнут по пересечению (сближению) концевых участков, хвосты отброшены.</summary>
   Hooked = 3,
   /// <summary>Шпилька — открытый поперечный стержень, концевые крючки отброшены.</summary>
   Tie = 4,
}

/// <summary>Хомут или шпилька распознанного сечения в местных осях, м.</summary>
public sealed class CadSectionStirrup
{
   public string Layer { get; init; } = "";
   public CadStirrupShape Shape { get; init; }

   /// <summary>Осевая линия; у замкнутой первая вершина в конце не повторяется.</summary>
   public List<CadPoint> Axis { get; init; } = [];

   /// <summary>Замкнутый хомут; иначе — шпилька.</summary>
   public bool Closed => Shape != CadStirrupShape.Tie;

   public double DiameterM { get; init; }

   /// <summary>Шаг вдоль элемента, м; null — не задан ни подписью, ни слоем.</summary>
   public double? SpacingM { get; init; }

   /// <summary>Класс арматуры из подписи («A240»), латиницей; null — по слою.</summary>
   public string? Class { get; init; }
}

/// <summary>Диаметр и шаг хомутов слоя по умолчанию, мм (для элементов без подписи).</summary>
public sealed record CadStirrupDefaults(double? DiameterMm, double? SpacingMm);

/// <summary>Разобранная подпись хомутов: «Ø8 шаг 150», «Ø8А240 с шагом 150», «%%c10 s=200».</summary>
public readonly partial record struct CadStirrupLabel(double? DiameterMm, double? SpacingMm, string? Class)
{
   /// <summary>Подпись хомутов — есть диаметр или шаг (класс сам по себе подписью не считается).</summary>
   public bool IsAnnotation => DiameterMm != null || SpacingMm != null;

   public static CadStirrupLabel Parse(string text)
   {
      double? Num(Match m) => m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
         CultureInfo.InvariantCulture, out double v) && v > 0 ? v : null;
      var cls = RebarClass().Match(CadLayerRules.Latin(text).ToUpperInvariant());
      return new(Num(Diameter().Match(text)), Num(Spacing().Match(text)), cls.Success ? cls.Value : null);
   }

   [GeneratedRegex(@"(?:[ØøФ⌀∅]|%%[cC])\s*(\d+(?:[.,]\d+)?)")]
   private static partial Regex Diameter();

   [GeneratedRegex(@"(?:с\s+шагом|шаг(?:ом)?|(?<![\p{L}])s\s*=|@)\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase)]
   private static partial Regex Spacing();

   [GeneratedRegex(@"(?<![A-Z])A\d{3}C?(?![0-9])")]
   private static partial Regex RebarClass();
}

/// <summary>
/// Распознавание хомутов и шпилек по линиям слоёв роли «Хомуты» одного сечения (единицы чертежа на входе,
/// местные оси сечения в метрах на выходе). Порядок: полилинии с шириной; пары замкнутых контуров с почти
/// постоянным зазором (двойной контур); остальные замкнутые — тонкие осевые; незамкнутые с близкими концами
/// или самопересечением — хомуты с крючками; прочие незамкнутые — шпильки.
/// </summary>
public static class CadStirrupRecognizer
{
   /// <summary>Наибольшее отношение СКО зазора к среднему у двойного контура.</summary>
   const double GapCv = 0.15;

   /// <summary>Пределы среднего зазора двойного контура, мм.</summary>
   const double GapMinMm = 3, GapMaxMm = 40;

   /// <summary>Длина концевого крючка (и близость концов хомута с крючками) в диаметрах.</summary>
   const double HookDiameters = 10;

   /// <summary>Предел длины концевого крючка шпильки в диаметрах: хвост ровно 10 d не должен зависеть от округления.</summary>
   const double TieHookDiameters = 12;

   sealed class Raw(string layer, CadStirrupShape shape, List<CadPoint> axis, double? d)
   {
      public string Layer { get; } = layer;
      public CadStirrupShape Shape { get; } = shape;
      public List<CadPoint> Axis { get; } = axis;

      /// <summary>Диаметр по геометрии (ширина, зазор), единицы чертежа.</summary>
      public double? D { get; } = d;

      public bool Closed => Shape != CadStirrupShape.Tie;
   }

   /// <param name="paths">Линии слоёв «Хомуты» сечения (окружности — уже замкнутыми линиями).</param>
   /// <param name="texts">Тексты сечения (подписи и тексты слоёв хомутов): подписями хомутов считаются те, где есть диаметр или шаг.</param>
   /// <param name="toLocal">Перевод точки чертежа в местные оси сечения, м.</param>
   public static List<CadSectionStirrup> Recognize(IReadOnlyList<CadPath> paths, IReadOnlyList<CadText> texts,
                                                   CadImportOptions o, Func<CadPoint, CadPoint> toLocal,
                                                   List<string> warnings)
   {
      double mpu = o.MetersPerUnit;
      double Units(double mm) => mm / 1000 / mpu;
      var notes = texts.Select(t => (T: t, L: CadStirrupLabel.Parse(t.Text))).Where(x => x.L.IsAnnotation).ToList();

      CadStirrupLabel? NoteFor(IReadOnlyList<CadPoint> axis, bool closed)
      {
         if (notes.Count == 0) return null;
         if (notes.Count == 1) return notes[0].L;
         return notes.MinBy(n => DistanceToLine(axis, closed, n.T.X, n.T.Y)).L;
      }
      CadStirrupDefaults? Defaults(string layer) =>
         o.StirrupDefaults.TryGetValue(layer, out var d) ? d : null;
      // Диаметр для порогов (крючки, близость концов) до окончательного назначения.
      double? GuessD(CadPath p) => p.Width > 0 ? p.Width
         : NoteFor(p.Points, p.Closed)?.DiameterMm is { } mm ? Units(mm)
         : Defaults(p.Layer)?.DiameterMm is { } dm ? Units(dm) : null;

      var raw = new List<Raw>();
      foreach (var p in paths.Where(p => p.Closed && p.Width > 0))
         raw.Add(new Raw(p.Layer, CadStirrupShape.Wide, [.. p.Points], p.Width));

      // Двойной контур: внешний — от больших к меньшим, внутренний — с наименьшим разбросом зазора.
      var thin = paths.Where(p => p.Closed && p.Width <= 0)
                      .Select(p => new CadPolygon(p.Layer, p.Points))
                      .OrderByDescending(p => Math.Abs(p.Area)).ToList();
      var used = new bool[thin.Count];
      for (int i = 0; i < thin.Count; i++)
      {
         if (used[i]) continue;
         int best = -1;
         double bestCv = double.MaxValue, bestGap = 0;
         for (int j = i + 1; j < thin.Count; j++)
         {
            if (used[j] || !string.Equals(thin[i].Layer, thin[j].Layer, StringComparison.OrdinalIgnoreCase)
                || !CadGeometry.Contains(thin[i], thin[j])) continue;
            var gaps = thin[j].Points.Select(v => CadGeometry.DistanceToBoundary(thin[i].Points, v.X, v.Y)).ToList();
            double mean = gaps.Average();
            double cv = Math.Sqrt(gaps.Average(g => (g - mean) * (g - mean))) / mean;
            if (cv <= GapCv && mean >= Units(GapMinMm) && mean <= Units(GapMaxMm) && cv < bestCv)
               (best, bestCv, bestGap) = (j, cv, mean);
         }
         if (best < 0) continue;
         used[i] = used[best] = true;
         raw.Add(new Raw(thin[i].Layer, CadStirrupShape.DoubleContour,
            CadGeometry.Offset(thin[i].Points, -bestGap / 2), bestGap));
      }
      for (int i = 0; i < thin.Count; i++)
         if (!used[i]) raw.Add(new Raw(thin[i].Layer, CadStirrupShape.Thin, [.. thin[i].Points], null));

      foreach (var p in paths.Where(p => !p.Closed))
      {
         double? d = GuessD(p);
         double? w = p.Width > 0 ? p.Width : null;
         if (CloseHooked(p.Points, d) is { } loop) raw.Add(new Raw(p.Layer, CadStirrupShape.Hooked, loop, w));
         else raw.Add(new Raw(p.Layer, CadStirrupShape.Tie, TrimHooks(p.Points, d), w));
      }

      // Диаметр, шаг, класс: геометрия → подпись → слой.
      var result = new List<CadSectionStirrup>();
      int noD = 0, noS = 0, unsnapped = 0;
      foreach (var r in raw)
      {
         var note = NoteFor(r.Axis, r.Closed);
         var def = Defaults(r.Layer);
         double? dMm = r.D is { } du ? du * mpu * 1000 : note?.DiameterMm ?? def?.DiameterMm;
         if (dMm is not { } mm) { noD++; continue; }
         if (o.SnapDiameters)
         {
            double near = CadSectionRecognizer.StandardDiametersMm.MinBy(v => Math.Abs(v - mm));
            if (Math.Abs(near - mm) <= 0.5) mm = near;
            else unsnapped++;
         }
         double? sMm = note?.SpacingMm ?? def?.SpacingMm;
         if (sMm == null) noS++;
         result.Add(new CadSectionStirrup
         {
            Layer = r.Layer, Shape = r.Shape, Axis = [.. r.Axis.Select(toLocal)],
            DiameterM = mm / 1000, SpacingM = sMm / 1000, Class = note?.Class,
         });
      }
      if (noD > 0) warnings.Add($"хомутов без диаметра (нет подписи и диаметра слоя) пропущено: {noD}");
      if (noS > 0) warnings.Add($"хомутов без шага: {noS}");
      if (unsnapped > 0) warnings.Add($"диаметров хомутов вне сортамента: {unsnapped}");
      return result;
   }

   // ── Хомут с крючками ────────────────────────────────────────────────────

   /// <summary>
   /// Замыкание незамкнутой линии с крючками: если концы близки (≤ 10 d или ≤ 15 % длины) или линия пересекает
   /// себя — по точке пересечения несмежных отрезков, охватывающей наибольшую часть длины, иначе по паре наиболее
   /// сближенных вершин; хвосты отбрасываются. null — линия не хомут (петля короче половины длины или не сходится).
   /// </summary>
   static List<CadPoint>? CloseHooked(IReadOnlyList<CadPoint> p, double? d)
   {
      int n = p.Count;
      if (n < 4) return null;
      var s = new double[n];
      for (int i = 1; i < n; i++) s[i] = s[i - 1] + Dist(p[i - 1], p[i]);
      double len = s[n - 1];
      double ends = Dist(p[0], p[n - 1]);
      bool near = ends <= 0.15 * len || (d is { } dd && ends <= HookDiameters * dd);

      // Пересечения несмежных отрезков: петля от точки на отрезке i до точки на отрезке j.
      (double Loop, int I, int J, CadPoint X)? cross = null;
      for (int i = 0; i < n - 1; i++)
         for (int j = i + 2; j < n - 1; j++)
            if (Intersect(p[i], p[i + 1], p[j], p[j + 1]) is { } x)
            {
               double loop = s[j] + Dist(p[j], x.P) - (s[i] + Dist(p[i], x.P));
               if (loop >= 0.5 * len && (cross == null || loop > cross.Value.Loop)) cross = (loop, i, j, x.P);
            }
      if (cross is { } c)
      {
         var pts = new List<CadPoint> { c.X };
         for (int k = c.I + 1; k <= c.J; k++) pts.Add(p[k]);
         return Valid(pts, len);
      }
      if (!near) return null;

      // Без пересечения: ближайшие вершины начального и конечного участков, петля — не меньше половины длины.
      double gapMax = Math.Max(d is { } d2 ? 2 * d2 : 0, 0.03 * len);
      (double Gap, double Loop, int I, int J)? best = null;
      for (int i = 0; i < n; i++)
         for (int j = n - 1; j > i; j--)
         {
            double loop = s[j] - s[i];
            if (loop < 0.5 * len) break;
            double gap = Dist(p[i], p[j]);
            if (gap > gapMax) continue;
            if (best == null || gap < best.Value.Gap - 1e-9 * len
                || (gap <= best.Value.Gap + 1e-9 * len && loop > best.Value.Loop))
               best = (gap, loop, i, j);
         }
      if (best is not { } b) return null;
      var loopPts = new List<CadPoint>();
      for (int k = b.I; k <= b.J; k++) loopPts.Add(p[k]);
      if (b.Gap <= 1e-9 * len) loopPts.RemoveAt(loopPts.Count - 1);
      return Valid(loopPts, len);
   }

   static List<CadPoint>? Valid(List<CadPoint> pts, double len)
   {
      var clean = new List<CadPoint>(pts.Count);
      foreach (var q in pts)
         if (clean.Count == 0 || Dist(clean[^1], q) > 1e-9 * len) clean.Add(q);
      if (clean.Count > 1 && Dist(clean[0], clean[^1]) <= 1e-9 * len) clean.RemoveAt(clean.Count - 1);
      return clean.Count >= 3 && Math.Abs(CadGeometry.SignedArea(clean)) > 1e-6 * len * len ? clean : null;
   }

   // ── Шпилька ─────────────────────────────────────────────────────────────

   /// <summary>
   /// Отбрасывает концевые крючки: участок до первого отрезка длиннее 12 d (хвост 10 d или 75 мм — с запасом; при неизвестном d — 20 % длины линии),
   /// если сам участок не длиннее этого предела и его начальный отрезок повёрнут к основному на 90° и больше.
   /// </summary>
   static List<CadPoint> TrimHooks(IReadOnlyList<CadPoint> p, double? d)
   {
      var pts = p.ToList();
      double len = 0;
      for (int i = 1; i < pts.Count; i++) len += Dist(pts[i - 1], pts[i]);
      double limit = d is { } dd ? TieHookDiameters * dd : 0.2 * len;
      pts = TrimStart(pts, limit);
      pts.Reverse();
      pts = TrimStart(pts, limit);
      pts.Reverse();
      return pts;
   }

   static List<CadPoint> TrimStart(List<CadPoint> p, double limit)
   {
      double s = 0;
      for (int k = 0; k < p.Count - 1; k++)
      {
         double seg = Dist(p[k], p[k + 1]);
         if (seg > limit)
         {
            if (k == 0) return p;
            double dot = (p[1].X - p[0].X) * (p[k + 1].X - p[k].X) + (p[1].Y - p[0].Y) * (p[k + 1].Y - p[k].Y);
            return dot <= 1e-9 * Dist(p[0], p[1]) * seg ? p.GetRange(k, p.Count - k) : p;
         }
         s += seg;
         if (s > limit) return p;
      }
      return p;
   }

   // ── Геометрия ───────────────────────────────────────────────────────────

   static double Dist(CadPoint a, CadPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

   /// <summary>Расстояние от точки до ломаной (замкнутой или нет).</summary>
   static double DistanceToLine(IReadOnlyList<CadPoint> p, bool closed, double x, double y)
   {
      if (closed) return CadGeometry.DistanceToBoundary(p, x, y);
      double best = double.MaxValue;
      for (int i = 0; i + 1 < p.Count; i++)
         best = Math.Min(best, CadGeometry.DistanceToBoundary([p[i], p[i + 1]], x, y));
      return best;
   }

   /// <summary>Точка пересечения отрезков (включая касание концом); null — не пересекаются или параллельны.</summary>
   static (CadPoint P, double T)? Intersect(CadPoint a, CadPoint b, CadPoint c, CadPoint d)
   {
      double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
      double den = rx * sy - ry * sx;
      if (Math.Abs(den) < 1e-12 * (rx * rx + ry * ry + sx * sx + sy * sy)) return null;
      double t = ((c.X - a.X) * sy - (c.Y - a.Y) * sx) / den;
      double u = ((c.X - a.X) * ry - (c.Y - a.Y) * rx) / den;
      const double e = 1e-9;
      if (t < -e || t > 1 + e || u < -e || u > 1 + e) return null;
      return (new CadPoint(a.X + t * rx, a.Y + t * ry), t);
   }
}
