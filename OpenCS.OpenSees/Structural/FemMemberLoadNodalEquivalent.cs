namespace OpenCS.OpenSees.Structural;

/// <summary>
/// Вектор эквивалентных узловых сил <c>p_eq</c> нагрузок одного элемента в одной стадии — в местных
/// осях <b>начальной</b> конфигурации, концы i и j по 6 компонент (N, Vy, Vz, T, My, Mz). Массивы
/// копируются: запись неизменяема.
/// </summary>
public sealed record FemElementLoadEquivalent
{
    public FemElementLoadEquivalent(int stageIndex, int elementTag, IReadOnlyList<double> localI, IReadOnlyList<double> localJ)
    {
        ArgumentNullException.ThrowIfNull(localI);
        ArgumentNullException.ThrowIfNull(localJ);
        if (stageIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(stageIndex), stageIndex, "Индекс стадии не может быть отрицательным.");
        if (localI.Count != 6 || localJ.Count != 6)
            throw new ArgumentException("Вектор эквивалентных сил конца элемента должен иметь ровно 6 компонент.");
        if (localI.Concat(localJ).Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Компоненты эквивалентных сил должны быть конечными числами.");
        StageIndex = stageIndex;
        ElementTag = elementTag;
        LocalI = localI.ToArray();
        LocalJ = localJ.ToArray();
    }

    public int StageIndex { get; }
    public int ElementTag { get; }
    public IReadOnlyList<double> LocalI { get; }
    public IReadOnlyList<double> LocalJ { get; }
}

/// <summary>
/// Перевод нагрузок в пролёте стержней (<c>eleLoad</c>) в согласованные эквивалентные узловые силы —
/// обход ограничения OpenSees: 3D <c>forceBeamColumn</c> с <c>geomTransf Corotational</c> не принимает
/// <c>eleLoad</c>. <b>Приближение:</b> узловые эквиваленты точны только в упругой постановке, а при
/// физической нелинейности меняют состояния фибр и отклик; поправка концевых усилий
/// (<see cref="FemElementForceCorrection"/>) задаётся в начальных осях и деградирует с поворотом
/// элемента. Осевая нагрузка — по линейным функциям формы, поперечная — по функциям формы Эрмита.
/// </summary>
public static class FemMemberLoadNodalEquivalent
{
    // Гаусс–Лежандр, 3 точки на [−1, 1]: точен для полинома ≤ 5-й степени (q·N — не выше 4-й).
    static readonly double[] GaussPoints = [-Math.Sqrt(0.6), 0, Math.Sqrt(0.6)];
    static readonly double[] GaussWeights = [5.0 / 9, 8.0 / 9, 5.0 / 9];

    /// <summary>
    /// Модель без <c>DistributedLoads</c>/<c>PointLoads</c>: их эквивалентные узловые силы добавлены в
    /// <c>Loads</c> стадий (глобальные оси); плюс <c>p_eq</c> по элементам и стадиям для поправки
    /// концевых усилий. Модель без нагрузок стержней возвращается как есть.
    /// </summary>
    public static (FemNonlinearModel Model, IReadOnlyList<FemElementLoadEquivalent> Equivalents) Convert(FemNonlinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Stages.All(s => s.DistributedLoads.Count == 0 && s.PointLoads.Count == 0))
            return (model, []);

        var nodeByTag = model.Nodes.ToDictionary(n => n.Tag);
        var elementByTag = model.Elements.ToDictionary(e => e.Tag);
        var equivalents = new List<FemElementLoadEquivalent>();
        var stages = new List<FemNonlinearStage>();

        for (int stageIndex = 0; stageIndex < model.Stages.Count; stageIndex++)
        {
            var stage = model.Stages[stageIndex];
            var local = new SortedDictionary<int, double[]>();
            double[] Accumulator(int elementTag) =>
                local.TryGetValue(elementTag, out var v) ? v : local[elementTag] = new double[12];

            foreach (var load in stage.DistributedLoads)
            {
                var element = Element(elementByTag, load.ElementTag);
                Add(Accumulator(load.ElementTag), Distributed(load, Length(nodeByTag, element)));
            }
            foreach (var load in stage.PointLoads)
            {
                var element = Element(elementByTag, load.ElementTag);
                Add(Accumulator(load.ElementTag), Point(load, Length(nodeByTag, element)));
            }

            var nodal = new List<FemLinearNodalLoad>(stage.Loads);
            foreach (var (elementTag, p) in local)
            {
                var element = elementByTag[elementTag];
                var (x, y, z) = LocalAxes(Node(nodeByTag, element.NodeI), Node(nodeByTag, element.NodeJ), element.Vecxz);
                nodal.Add(ToGlobal(element.NodeI, p, 0, x, y, z));
                nodal.Add(ToGlobal(element.NodeJ, p, 6, x, y, z));
                equivalents.Add(new FemElementLoadEquivalent(stageIndex, elementTag, p[..6], p[6..]));
            }

            stages.Add(new FemNonlinearStage
            {
                Tag = stage.Tag, Loads = nodal, DistributedLoads = [], PointLoads = [],
                KinematicLoads = stage.KinematicLoads, LoadFactorStep = stage.LoadFactorStep,
                MaxLoadFactor = stage.MaxLoadFactor, PathControl = stage.PathControl
            });
        }

        var converted = new FemNonlinearModel
        {
            Nodes = model.Nodes, Sections = model.Sections, Elements = model.Elements, Stages = stages,
            GeomTransfKind = model.GeomTransfKind, ElementFormulation = model.ElementFormulation,
            Policy = model.Policy, CalcTypeName = model.CalcTypeName, RecordFiberStates = model.RecordFiberStates,
            FiberStatesIntegrationPoints = model.FiberStatesIntegrationPoints
        };
        return (converted, equivalents);
    }

    /// <summary>Местные оси по правилу OpenSees: <c>x = (j − i)/L</c>, <c>y = vecxz × x</c>, <c>z = x × y</c>.</summary>
    public static ((double X, double Y, double Z) X, (double X, double Y, double Z) Y, (double X, double Y, double Z) Z)
        LocalAxes(FemLinearNode i, FemLinearNode j, (double X, double Y, double Z) vecxz)
    {
        var x = Normalize((j.X - i.X, j.Y - i.Y, j.Z - i.Z), "Нулевая длина элемента.");
        var y = Normalize(Cross(vecxz, x), "Вектор vecxz параллелен оси элемента.");
        return (x, y, Cross(x, y));
    }

    /// <summary>Согласованный местный вектор распределённой нагрузки (12 компонент: i 0..5, j 6..11).</summary>
    public static double[] Distributed(FemLinearDistributedLoad load, double length)
    {
        ArgumentNullException.ThrowIfNull(load);
        double a = load.AOverL, b = load.BOverL;
        var p = new double[12];
        if (b <= a) return p;
        for (int k = 0; k < GaussPoints.Length; k++)
        {
            double t = (GaussPoints[k] + 1) / 2;                 // доля отрезка нагрузки [a, b]
            double xi = a + (b - a) * t;
            double w = GaussWeights[k] / 2 * (b - a) * length;   // вес · dx
            AddShape(p, xi, length,
                w * Lerp(load.WxStart, load.WxEnd, t), w * Lerp(load.WyStart, load.WyEnd, t), w * Lerp(load.WzStart, load.WzEnd, t));
        }
        return p;
    }

    /// <summary>Согласованный местный вектор сосредоточенной силы в точке <c>XOverL</c>.</summary>
    public static double[] Point(FemLinearPointLoad load, double length)
    {
        ArgumentNullException.ThrowIfNull(load);
        var p = new double[12];
        AddShape(p, load.XOverL, length, load.Px, load.Py, load.Pz);
        return p;
    }

    /// <summary>Вклад местной силы (fx, fy, fz) в точке ξ через функции формы.</summary>
    static void AddShape(double[] p, double xi, double length, double fx, double fy, double fz)
    {
        double xi2 = xi * xi, xi3 = xi2 * xi;
        double h1 = 1 - 3 * xi2 + 2 * xi3, h2 = length * (xi - 2 * xi2 + xi3);
        double h3 = 3 * xi2 - 2 * xi3, h4 = length * (xi3 - xi2);
        p[0] += fx * (1 - xi);
        p[6] += fx * xi;
        // изгиб в плоскости xy (сила вдоль y): поперечная сила Vy и момент Mz
        p[1] += fy * h1; p[5] += fy * h2; p[7] += fy * h3; p[11] += fy * h4;
        // изгиб в плоскости xz (сила вдоль z): поперечная сила Vz и момент My (обратный знак)
        p[2] += fz * h1; p[4] -= fz * h2; p[8] += fz * h3; p[10] -= fz * h4;
    }

    static FemLinearNodalLoad ToGlobal(int nodeTag, double[] p, int offset,
        (double X, double Y, double Z) x, (double X, double Y, double Z) y, (double X, double Y, double Z) z)
    {
        var f = Combine(p[offset], p[offset + 1], p[offset + 2], x, y, z);
        var m = Combine(p[offset + 3], p[offset + 4], p[offset + 5], x, y, z);
        return new FemLinearNodalLoad(nodeTag, f.X, f.Y, f.Z, m.X, m.Y, m.Z);
    }

    static (double X, double Y, double Z) Combine(double a, double b, double c,
        (double X, double Y, double Z) x, (double X, double Y, double Z) y, (double X, double Y, double Z) z) =>
        (a * x.X + b * y.X + c * z.X, a * x.Y + b * y.Y + c * z.Y, a * x.Z + b * y.Z + c * z.Z);

    static FemNonlinearElement Element(Dictionary<int, FemNonlinearElement> byTag, int tag) =>
        byTag.TryGetValue(tag, out var e) ? e : throw new InvalidOperationException($"Нагрузка ссылается на неизвестный элемент {tag}.");

    static FemLinearNode Node(Dictionary<int, FemLinearNode> byTag, int tag) =>
        byTag.TryGetValue(tag, out var n) ? n : throw new InvalidOperationException($"Элемент ссылается на неизвестный узел {tag}.");

    static double Length(Dictionary<int, FemLinearNode> nodes, FemNonlinearElement e)
    {
        var i = Node(nodes, e.NodeI);
        var j = Node(nodes, e.NodeJ);
        return Math.Sqrt((j.X - i.X) * (j.X - i.X) + (j.Y - i.Y) * (j.Y - i.Y) + (j.Z - i.Z) * (j.Z - i.Z));
    }

    static void Add(double[] target, double[] source)
    {
        for (int k = 0; k < target.Length; k++) target[k] += source[k];
    }

    static double Lerp(double start, double end, double t) => start + (end - start) * t;

    static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v, string error)
    {
        double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        if (!double.IsFinite(length) || length < 1e-12) throw new InvalidOperationException(error);
        return (v.X / length, v.Y / length, v.Z / length);
    }
}
