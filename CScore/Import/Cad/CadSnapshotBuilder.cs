namespace CScore.Import.Cad;

/// <summary>Вершина полилинии с кривизной сегмента до следующей вершины (bulge = tg ¼ центрального угла).</summary>
public readonly record struct CadVertex(double X, double Y, double Bulge = 0);

/// <summary>
/// Сборка <see cref="CadSnapshot"/> из примитивов любого источника (netDxf, COM AutoCAD/nanoCAD):
/// дуги полилиний (скругления профилей, дуговые фаски), отрезки и дуги заменяются ломаной; незамкнутые
/// цепи из LINE/ARC/полилиний одного слоя сшиваются в замкнутые контуры (расчленённые профили). Каждая полилиния и
/// каждая сшитая цепь, замкнутая или нет, сохраняется ещё и линией <see cref="CadPath"/> (для хомутов).
/// </summary>
/// <param name="arcs">Дискретизация дуг и окружностей (настройка импорта).</param>
/// <param name="tolerance">Допуск совпадения концов при сшивке, в единицах чертежа.</param>
public sealed class CadSnapshotBuilder(CadArcDiscretization arcs, double tolerance)
{
   readonly CadSnapshot _s = new();
   readonly List<(string Layer, List<CadPoint> Pts, double Width)> _open = [];

   public CadSnapshot Snapshot => _s;

   /// <summary>Дискретизация дуг, с которой работает построитель.</summary>
   public CadArcDiscretization Arcs => arcs;

   /// <summary>Полилиния (LWPOLYLINE, 2D POLYLINE); замкнутая — контур, незамкнутая — в сшивку.</summary>
   /// <param name="width">Постоянная ширина полилинии; 0 — без ширины.</param>
   public void AddPolyline(string layer, IReadOnlyList<CadVertex> v, bool closed, double width = 0)
   {
      if (v.Count < 2) return;
      var pts = new List<CadPoint>(v.Count * 2) { new(v[0].X, v[0].Y) };
      int segs = closed ? v.Count : v.Count - 1;
      for (int i = 0; i < segs; i++)
      {
         var a = v[i];
         var b = v[(i + 1) % v.Count];
         pts.AddRange(CadGeometry.BulgeArc(new(a.X, a.Y), new(b.X, b.Y), a.Bulge, arcs));
         pts.Add(new(b.X, b.Y));
      }
      // Замкнутая — повтор первой вершины в конце убираем; незамкнутая, но с совпадающими концами — тоже контур.
      bool ends = Near(pts[0], pts[^1]);
      if (closed || (ends && pts.Count > 3))
      {
         if (ends) pts.RemoveAt(pts.Count - 1);
         AddClosed(layer, pts, width);
      }
      else _open.Add((layer, pts, width));
   }

   public void AddLine(string layer, double x1, double y1, double x2, double y2)
   {
      if (Math.Abs(x1 - x2) + Math.Abs(y1 - y2) > 0)
         _open.Add((layer, [new(x1, y1), new(x2, y2)], 0));
   }

   /// <summary>Дуга против часовой стрелки от start до end (радианы).</summary>
   public void AddArc(string layer, double cx, double cy, double r, double start, double end)
   {
      double sweep = end - start;
      while (sweep <= 0) sweep += 2 * Math.PI;
      int n = arcs.Segments(r, sweep);
      var pts = new List<CadPoint>(n + 1);
      for (int i = 0; i <= n; i++)
      {
         double a = start + sweep * i / n;
         pts.Add(new(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
      }
      _open.Add((layer, pts, 0));
   }

   public void AddCircle(string layer, double cx, double cy, double r)
   {
      if (r > 0) _s.Circles.Add(new CadCircle(layer, cx, cy, r));
   }

   /// <summary>Полный эллипс — контур (готовые вершины); дуга эллипса — в сшивку.</summary>
   public void AddEllipse(string layer, IReadOnlyList<CadPoint> pts, bool full)
   {
      if (pts.Count < 2) return;
      if (full) AddClosed(layer, [.. pts], 0);
      else _open.Add((layer, [.. pts], 0));
   }

   public void AddText(string layer, double x, double y, double height, string text)
   {
      if (!string.IsNullOrWhiteSpace(text)) _s.Texts.Add(new CadText(layer, x, y, height, text.Trim()));
   }

   /// <summary>Замкнутая линия: контур и линия с шириной.</summary>
   void AddClosed(string layer, List<CadPoint> pts, double width)
   {
      var clean = Clean(pts, closed: true);
      if (clean.Count < 3 || Math.Abs(CadGeometry.SignedArea(clean)) < tolerance * tolerance)
      {
         _s.Warnings.Add($"вырожденный контур на слое «{layer}»");
         return;
      }
      _s.Polygons.Add(new CadPolygon(layer, clean));
      _s.Paths.Add(new CadPath(layer, clean, closed: true, width));
   }

   void AddOpen(string layer, List<CadPoint> pts, double width)
   {
      var clean = Clean(pts, closed: false);
      if (clean.Count >= 2) _s.Paths.Add(new CadPath(layer, clean, closed: false, width));
   }

   /// <summary>Без подряд совпадающих вершин (нулевых сегментов); у замкнутой — без повтора первой в конце.</summary>
   List<CadPoint> Clean(List<CadPoint> pts, bool closed)
   {
      var clean = new List<CadPoint>(pts.Count);
      foreach (var p in pts)
         if (clean.Count == 0 || !Near(clean[^1], p)) clean.Add(p);
      if (closed && clean.Count > 1 && Near(clean[0], clean[^1])) clean.RemoveAt(clean.Count - 1);
      return clean;
   }

   bool Near(CadPoint a, CadPoint b) => Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

   /// <summary>Сшивает незамкнутые цепи и возвращает снимок.</summary>
   public CadSnapshot Build()
   {
      foreach (var layer in _open.GroupBy(o => o.Layer, StringComparer.OrdinalIgnoreCase))
         Stitch(layer.Key, layer.Select(o => (o.Pts, o.Width)).ToList());
      _open.Clear();
      return _s;
   }

   /// <summary>
   /// Сшивка цепей одного слоя: концы, совпадающие с допуском, соединяются; замкнувшиеся — контуры, остальные —
   /// незамкнутые линии. Ширина сшитой линии — общая ширина её частей, при разной — 0.
   /// </summary>
   void Stitch(string layer, List<(List<CadPoint> Pts, double Width)> parts)
   {
      var chains = parts.Select(p => p.Pts).ToList();
      // Индекс концов по ячейкам сетки с шагом допуска: соседние ячейки тоже проверяются.
      var grid = new Dictionary<(long, long), List<(int Chain, bool End)>>();
      (long, long) Cell(CadPoint p) => ((long)Math.Floor(p.X / tolerance), (long)Math.Floor(p.Y / tolerance));
      void Index(int i, bool end)
      {
         var c = Cell(end ? chains[i][^1] : chains[i][0]);
         if (!grid.TryGetValue(c, out var l)) grid[c] = l = [];
         l.Add((i, end));
      }
      for (int i = 0; i < chains.Count; i++) { Index(i, false); Index(i, true); }

      var used = new bool[chains.Count];
      (int Chain, bool End)? Partner(CadPoint p, int self)
      {
         var (cx, cy) = Cell(p);
         for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
               if (grid.TryGetValue((cx + dx, cy + dy), out var l))
                  foreach (var e in l)
                     if (e.Chain != self && !used[e.Chain] && Near(p, e.End ? chains[e.Chain][^1] : chains[e.Chain][0]))
                        return e;
         return null;
      }

      int unclosed = 0;
      for (int i = 0; i < chains.Count; i++)
      {
         if (used[i]) continue;
         used[i] = true;
         var path = new List<CadPoint>(chains[i]);
         double width = parts[i].Width;
         // Наращиваем вперёд, затем назад.
         for (int dir = 0; dir < 2 && !Near(path[0], path[^1]); dir++)
         {
            while (!Near(path[0], path[^1]) && Partner(path[^1], -1) is { } e)
            {
               used[e.Chain] = true;
               if (parts[e.Chain].Width != width) width = 0;
               var next = chains[e.Chain];
               // Подходим к концу соседней цепи — проходим её в обратном направлении.
               IEnumerable<CadPoint> seq = e.End ? Enumerable.Reverse(next) : next;
               path.AddRange(seq.Skip(1));
            }
            path.Reverse();
         }
         if (path.Count > 3 && Near(path[0], path[^1])) AddClosed(layer, path, width);
         else
         {
            AddOpen(layer, path, width);
            unclosed++;
         }
      }
      if (unclosed > 0) _s.Warnings.Add($"незамкнутых линий на слое «{layer}»: {unclosed}");
   }
}
