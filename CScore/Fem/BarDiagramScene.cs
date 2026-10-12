namespace CScore.Fem;

/// <summary>Эпюра одного стержня: концы, точки (t, значение) вдоль него и явное направление ординат.</summary>
/// <param name="Profile">Точки по возрастанию t (t = 0 — начальный узел; две точки с одним t — скачок).</param>
/// <param name="Direction">Единичное направление ординат (местные оси расчёта); null — по плоскости сцены в осях
/// <see cref="BarDiagramGeometry.LocalAxes"/>.</param>
public readonly record struct BarDiagramInput(
    string Tag, (double X, double Y, double Z) P1, (double X, double Y, double Z) P2,
    IReadOnlyList<(double T, double V)> Profile, (double X, double Y, double Z)? Direction = null);

/// <summary>Треугольник заливки эпюры.</summary>
public readonly record struct BarDiagramTriangle((double X, double Y, double Z) A, (double X, double Y, double Z) B,
    (double X, double Y, double Z) C);

/// <summary>Подпись значения эпюры: вершина ординаты, значение, КЭ.</summary>
public readonly record struct BarDiagramLabel((double X, double Y, double Z) Position, double Value, string Tag);

/// <summary>
/// Эпюры на стержнях схемы для показа в 3D: заливка площади и контур по знаку (участок со сменой знака делится в
/// нуле — без «перекрута»), прореженные ординаты и подписи экстремумов по цепочкам соосных КЭ.
/// </summary>
public sealed class BarDiagramScene
{
    public List<BarDiagramTriangle> PositiveFill { get; } = [];
    public List<BarDiagramTriangle> NegativeFill { get; } = [];
    public List<BarDiagramLine> PositiveOutline { get; } = [];
    public List<BarDiagramLine> NegativeOutline { get; } = [];
    public List<BarDiagramLine> PositiveOrdinates { get; } = [];
    public List<BarDiagramLine> NegativeOrdinates { get; } = [];
    /// <summary>Подписи по убыванию |значения| (порядок отбора без наложения).</summary>
    public List<BarDiagramLabel> Labels { get; } = [];

    /// <summary>Сцены нет — нечего рисовать.</summary>
    public bool IsEmpty => PositiveFill.Count + NegativeFill.Count + PositiveOutline.Count + NegativeOutline.Count == 0;

    /// <summary>Соосность КЭ цепочки: косинус угла между осями и между направлениями ординат.</summary>
    const double ChainCos = 0.999;

    /// <summary>Построить эпюры.</summary>
    /// <param name="scale">Длина ординаты на единицу значения, м.</param>
    /// <param name="ordinateSpacing">Наименьшее расстояние между ординатами вдоль цепочки (кроме концов, скачков и
    /// экстремумов), м; 0 — ордината в каждой точке.</param>
    /// <param name="labelShare">Подписи экстремумов не меньше этой доли наибольшего |значения| сцены.</param>
    public static BarDiagramScene Build(IReadOnlyList<BarDiagramInput> bars, BarDiagramPlane plane, double scale,
        double ordinateSpacing, double labelShare = 0.02)
    {
        var scene = new BarDiagramScene();
        var items = new List<Item>();
        double max = 0;
        foreach (var bar in bars)
        {
            if (bar.Profile.Count == 0 || BarDiagramGeometry.LocalAxes(bar.P1, bar.P2) is not { } axes) continue;
            var d = bar.Direction ?? (plane == BarDiagramPlane.Z1 ? axes.Z1 : axes.Y1);
            items.Add(new Item(bar, d));
            foreach (var (_, v) in bar.Profile) max = Math.Max(max, Math.Abs(v));
        }
        if (!(max > 0)) return scene;

        foreach (var item in items) scene.AddAreaAndOutline(item, scale);
        foreach (var chain in Chains(items))
            scene.AddOrdinatesAndLabels(chain, scale, ordinateSpacing, max * 1e-9, labelShare * max);
        scene.Labels.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
        return scene;
    }

    sealed record Item(BarDiagramInput Bar, (double X, double Y, double Z) D)
    {
        public (double X, double Y, double Z) Axis(double t) => (Bar.P1.X + (Bar.P2.X - Bar.P1.X) * t,
            Bar.P1.Y + (Bar.P2.Y - Bar.P1.Y) * t, Bar.P1.Z + (Bar.P2.Z - Bar.P1.Z) * t);

        public (double X, double Y, double Z) Tip(double t, double v, double scale)
        {
            var a = Axis(t);
            return (a.X + D.X * v * scale, a.Y + D.Y * v * scale, a.Z + D.Z * v * scale);
        }

        public double Length => Dist(Bar.P1, Bar.P2);
    }

    void AddAreaAndOutline(Item item, double scale)
    {
        var profile = item.Bar.Profile;
        void Line(double v, (double, double, double) a, (double, double, double) b) =>
            (v > 0 ? PositiveOutline : NegativeOutline).Add(new BarDiagramLine(a, b));
        void Fill(double v, (double, double, double) a, (double, double, double) b, (double, double, double) c) =>
            (v > 0 ? PositiveFill : NegativeFill).Add(new BarDiagramTriangle(a, b, c));

        for (int i = 0; i + 1 < profile.Count; i++)
        {
            var (t, v) = profile[i];
            var (t2, v2) = profile[i + 1];
            if (v == 0 && v2 == 0) continue;
            bool jump = t2 - t < 1e-12;
            var a1 = item.Axis(t);
            var a2 = item.Axis(t2);
            var tip1 = item.Tip(t, v, scale);
            var tip2 = item.Tip(t2, v2, scale);
            if (v * v2 < 0)
            {
                // Смена знака — в точке нуля: два треугольника, контур — от вершин до оси.
                double tz = t + (t2 - t) * v / (v - v2);
                var az = item.Axis(tz);
                Line(v, tip1, az);
                Line(v2, az, tip2);
                if (jump) continue;
                Fill(v, a1, tip1, az);
                Fill(v2, az, tip2, a2);
            }
            else
            {
                double sign = v != 0 ? v : v2;
                Line(sign, tip1, tip2);
                if (jump) continue;
                Fill(sign, a1, tip1, tip2);
                Fill(sign, a1, tip2, a2);
            }
        }
    }

    /// <summary>Точка эпюры на цепочке: расстояние от её начала, значение, КЭ и t на нём.</summary>
    readonly record struct ChainPoint(double S, double V, Item Item, double T);

    void AddOrdinatesAndLabels(List<(Item Item, bool Reversed)> chain, double scale, double spacing, double eps, double labelMin)
    {
        var points = new List<ChainPoint>();
        double s0 = 0;
        foreach (var (item, reversed) in chain)
        {
            double length = item.Length;
            var profile = item.Bar.Profile;
            for (int k = 0; k < profile.Count; k++)
            {
                var (t, v) = profile[reversed ? profile.Count - 1 - k : k];
                double s = s0 + length * (reversed ? 1 - t : t);
                // Общий узел соседних КЭ с тем же значением — одна точка.
                if (points.Count > 0 && Math.Abs(points[^1].S - s) < 1e-9 && Math.Abs(points[^1].V - v) <= eps) continue;
                points.Add(new ChainPoint(s, v, item, t));
            }
            s0 += length;
        }
        if (points.Count == 0) return;

        // Ординаты: концы цепочки, скачки, экстремумы — всегда; остальные — не чаще шага прореживания.
        double lastS = double.NegativeInfinity;
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            bool end = i == 0 || i == points.Count - 1;
            bool jump = i > 0 && Math.Abs(points[i - 1].S - p.S) < 1e-9
                        || i + 1 < points.Count && Math.Abs(points[i + 1].S - p.S) < 1e-9;
            bool extremum = !end && IsExtremum(points, i, eps);
            if (!(end || jump || extremum || p.S - lastS >= spacing - 1e-12)) continue;
            lastS = p.S;
            if (Math.Abs(p.V) <= eps) continue;
            (p.V > 0 ? PositiveOrdinates : NegativeOrdinates)
                .Add(new BarDiagramLine(p.Item.Axis(p.T), p.Item.Tip(p.T, p.V, scale)));
        }

        // Подписи: наибольшее «+» и наименьшее «−» цепочки.
        var hi = points.MaxBy(p => p.V);
        var lo = points.MinBy(p => p.V);
        if (hi.V > 0 && hi.V >= labelMin) Labels.Add(new BarDiagramLabel(hi.Item.Tip(hi.T, hi.V, scale), hi.V, hi.Item.Bar.Tag));
        if (lo.V < 0 && -lo.V >= labelMin) Labels.Add(new BarDiagramLabel(lo.Item.Tip(lo.T, lo.V, scale), lo.V, lo.Item.Bar.Tag));
    }

    /// <summary>Внутренняя точка — локальный экстремум значения (с плато: сравнение с ближайшими отличающимися).</summary>
    static bool IsExtremum(List<ChainPoint> points, int i, double eps)
    {
        double v = points[i].V;
        double? prev = null, next = null;
        for (int j = i - 1; j >= 0 && prev == null; j--) if (Math.Abs(points[j].V - v) > eps) prev = points[j].V;
        for (int j = i + 1; j < points.Count && next == null; j++) if (Math.Abs(points[j].V - v) > eps) next = points[j].V;
        if (prev == null || next == null) return false;
        // Плато экстремума отмечается первой точкой.
        if (i > 0 && Math.Abs(points[i - 1].V - v) <= eps) return false;
        return v > prev && v > next || v < prev && v < next;
    }

    /// <summary>
    /// Цепочки КЭ: соседи по узлу, в котором сходятся ровно два КЭ с эпюрой, соосные и с тем же направлением
    /// ординат. Каждая цепочка — КЭ по порядку с признаком обхода от конца к началу.
    /// </summary>
    static List<List<(Item Item, bool Reversed)>> Chains(List<Item> items)
    {
        double size = 0;
        foreach (var it in items) size = Math.Max(size, it.Length);
        double tol = Math.Max(size * 1e-6, 1e-9);
        (long, long, long) Key((double X, double Y, double Z) p) =>
            ((long)Math.Round(p.X / tol), (long)Math.Round(p.Y / tol), (long)Math.Round(p.Z / tol));

        var byNode = new Dictionary<(long, long, long), List<(int Index, bool AtEnd)>>();
        for (int i = 0; i < items.Count; i++)
        {
            foreach (var (p, atEnd) in new[] { (items[i].Bar.P1, false), (items[i].Bar.P2, true) })
            {
                var key = Key(p);
                if (!byNode.TryGetValue(key, out var list)) byNode[key] = list = [];
                list.Add((i, atEnd));
            }
        }

        // Сосед КЭ i через его конец atEnd: (индекс, сосед подходит своим концом?) или null.
        (int Index, bool AtEnd)? Next(int i, bool atEnd)
        {
            var list = byNode[Key(atEnd ? items[i].Bar.P2 : items[i].Bar.P1)];
            if (list.Count != 2) return null;
            var other = list[0].Index == i && list[0].AtEnd == atEnd ? list[1] : list[0];
            if (other.Index == i) return null;
            var a = Unit(items[i]);
            var b = Unit(items[other.Index]);
            double axisCos = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z);
            var da = items[i].D;
            var db = items[other.Index].D;
            double dirCos = da.X * db.X + da.Y * db.Y + da.Z * db.Z;
            return axisCos >= ChainCos && dirCos >= ChainCos ? other : null;
        }

        var visited = new bool[items.Count];
        var chains = new List<List<(Item, bool)>>();
        void Walk(int start, bool reversed)
        {
            var chain = new List<(Item, bool)>();
            int i = start;
            bool rev = reversed;
            while (!visited[i])
            {
                visited[i] = true;
                chain.Add((items[i], rev));
                // Выход — через конец обхода: у прямого — P2, у обратного — P1.
                if (Next(i, atEnd: !rev) is not { } next) break;
                i = next.Index;
                // Сосед подходит концом P2 — обходится от конца к началу.
                rev = next.AtEnd;
            }
            chains.Add(chain);
        }

        // Сначала от концов цепочек (нет соседа с одной стороны), потом замкнутые.
        for (int i = 0; i < items.Count; i++)
        {
            if (visited[i]) continue;
            if (Next(i, atEnd: false) == null) Walk(i, reversed: false);
            else if (Next(i, atEnd: true) == null) Walk(i, reversed: true);
        }
        for (int i = 0; i < items.Count; i++)
            if (!visited[i]) Walk(i, reversed: false);
        return chains;
    }

    static (double X, double Y, double Z) Unit(Item item)
    {
        var p1 = item.Bar.P1;
        var p2 = item.Bar.P2;
        double l = item.Length;
        return ((p2.X - p1.X) / l, (p2.Y - p1.Y) / l, (p2.Z - p1.Z) / l);
    }

    static double Dist((double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
