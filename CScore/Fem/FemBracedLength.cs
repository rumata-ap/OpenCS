using System.Globalization;

namespace CScore.Fem;

/// <summary>
/// Вертикальная полоса стены для учёта продольного изгиба из плоскости.
/// </summary>
/// <param name="HeightM">Высота полосы между уровнями раскрепления (перекрытиями), м.</param>
/// <param name="VerticalAngleDeg">
/// Угол вертикали от оси X выдачи усилий КЭ вокруг его нормали (против часовой стрелки с конца нормали), град.
/// Усилия в осях «вертикаль — горизонталь»: <c>ShellForceTransform.Rotate(item, -VerticalAngleDeg)</c>.
/// </param>
public sealed record FemWallStrip(double HeightM, double VerticalAngleDeg);

/// <summary>
/// Длина элемента между раскреплениями по сетке схемы — база расчётной длины l0 = μ·l (п. 8.1.17 СП 63)
/// при учёте продольного изгиба в проверке по КЭ.
/// <para>
/// Стержень: цепочка соосных стержневых КЭ, проходящая через КЭ. Цепочка рвётся в узле, где сходятся не два
/// КЭ сетки (примыкают перекрытие, ригель, конец стержня) или следующий КЭ не соосен. Колонна в теле стены
/// рвётся в каждом узле — длина короткая, стена её раскрепляет.
/// </para>
/// <para>
/// Стена (пластина с почти горизонтальной нормалью): стенка — связные КЭ стен одной плоскости. Уровни
/// раскрепления — отметки её узлов, к которым примыкают перекрытия (пластины с негоризонтальной нормалью)
/// или невертикальные стержни (ригели), плюс низ и верх стенки. Высота полосы КЭ — между ближайшими уровнями
/// ниже и выше его центра. Перпендикулярные стены и колонны уровней не дают (раскрепляют по горизонтали).
/// Уровни общие для всей стенки: перекрытие, примыкающее к части стенки, считается раскрепляющим всю.
/// </para>
/// </summary>
public sealed class FemBracedLength
{
    /// <summary>Косинус угла, при котором соседние стержневые КЭ считаются соосными.</summary>
    const double CollinearCos = 0.999;
    /// <summary>Пластина — стена, если |nz| меньше этого (отклонение от вертикали до ≈ 6°).</summary>
    const double WallNormalZ = 0.1;
    /// <summary>Стержень вертикален (не раскрепляет стену по высоте), если |dz| больше этого.</summary>
    const double VerticalBarZ = 0.9;
    /// <summary>Совпадение отметок уровней, м.</summary>
    const double LevelTolerance = 1e-3;

    readonly List<FemElement> _elements = [];
    readonly List<long[]?> _nodes = [];
    readonly Dictionary<long, (double X, double Y, double Z)> _coords = [];
    readonly Dictionary<long, List<int>> _byNode = [];
    readonly Dictionary<string, int> _byTag = new(StringComparer.Ordinal);
    readonly Dictionary<int, double?> _barLength = [];
    Dictionary<int, FemWallStrip?>? _wallStrips;
    readonly object _lock = new();

    /// <param name="mesh">КЭ сетки схемы (все, не только КЭ цели).</param>
    /// <param name="nodes">Узлы сетки.</param>
    public FemBracedLength(IEnumerable<FemElement> mesh, IEnumerable<FemMeshNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(nodes);
        // Теги импортированных и построенных дискретизацией узлов могут совпадать — ключ узла учитывает
        // происхождение (как в раскладке армирования).
        var imported = new HashSet<int>();
        var generated = new HashSet<int>();
        foreach (var n in nodes)
        {
            if (!int.TryParse(n.NodeTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tag)) continue;
            bool isImported = n.Origin == FemMember.MeshSourceImported;
            if (_coords.TryAdd(Key(isImported, tag), (n.X, n.Y, n.Z)))
                (isImported ? imported : generated).Add(tag);
        }

        foreach (var e in mesh)
        {
            int index = _elements.Count;
            _elements.Add(e);
            bool own = e.Origin == FemMember.MeshSourceImported;
            long[]? ids = FemMeshTopology.ReadNodeTags(e)?
                .Select(t => int.Parse(t, CultureInfo.InvariantCulture))
                .Distinct()
                .Select(t => (own ? imported : generated).Contains(t) || !(own ? generated : imported).Contains(t)
                    ? Key(own, t) : Key(!own, t))
                .ToArray();
            _nodes.Add(ids);
            _byTag.TryAdd(e.ElemTag, index);
            if (ids == null) continue;
            foreach (long id in ids)
            {
                if (!_byNode.TryGetValue(id, out var list)) _byNode[id] = list = [];
                list.Add(index);
            }
        }
    }

    static long Key(bool imported, int tag) => ((imported ? 1L : 0L) << 32) | (uint)tag;

    /// <summary>Длина стержня между раскреплениями, м; null — КЭ не стержень или нет координат узлов.</summary>
    public double? BarLength(FemElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!_byTag.TryGetValue(element.ElemTag, out int index)) return null;
        lock (_lock)
        {
            if (_barLength.TryGetValue(index, out var cached)) return cached;
            var (length, chain) = WalkBar(index);
            foreach (int i in chain) _barLength[i] = length;
            _barLength[index] = length;
            return length;
        }
    }

    /// <summary>
    /// Вертикальная полоса стены, к которой относится КЭ; null — КЭ не пластина-стена или нет координат узлов.
    /// Ось X выдачи усилий — «узел 1 → узел 2», повёрнутая на <see cref="FemElement.LocalAxisAngleDeg"/>
    /// (не задан — без поворота).
    /// </summary>
    public FemWallStrip? WallStrip(FemElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!_byTag.TryGetValue(element.ElemTag, out int index)) return null;
        lock (_lock)
        {
            _wallStrips ??= BuildWallStrips();
            return _wallStrips.GetValueOrDefault(index);
        }
    }

    // ── Стержни ──────────────────────────────────────────────────────────────────────────────

    bool IsBar(int index) =>
        _elements[index].ElemType != "shell" && _nodes[index] is { Length: 2 };

    /// <summary>Единичный вектор и длина стержня; null — нет координат или нулевая длина.</summary>
    (double X, double Y, double Z, double L)? Axis(int index)
    {
        if (_nodes[index] is not { Length: 2 } ids
            || !_coords.TryGetValue(ids[0], out var a) || !_coords.TryGetValue(ids[1], out var b)) return null;
        double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
        double l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        return l > 1e-9 ? (dx / l, dy / l, dz / l, l) : null;
    }

    (double? Length, List<int> Chain) WalkBar(int start)
    {
        var chain = new List<int> { start };
        if (!IsBar(start) || Axis(start) is not { } axis) return (null, chain);
        double length = axis.L;
        var visited = new HashSet<int> { start };
        foreach (long end in _nodes[start]!)
        {
            long node = end;
            int previous = start;
            while (_byNode.TryGetValue(node, out var incident) && incident.Count == 2)
            {
                int next = incident[0] == previous ? incident[1] : incident[0];
                if (visited.Contains(next) || !IsBar(next) || Axis(next) is not { } a) break;
                if (Math.Abs(a.X * axis.X + a.Y * axis.Y + a.Z * axis.Z) < CollinearCos) break;
                visited.Add(next);
                chain.Add(next);
                length += a.L;
                var ids = _nodes[next]!;
                node = ids[0] == node ? ids[1] : ids[0];
                previous = next;
            }
        }
        return (length, chain);
    }

    // ── Стены ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Геометрия пластины: центр, единичная нормаль, единичная ось «узел 1 → узел 2».</summary>
    sealed record ShellGeometry(
        (double X, double Y, double Z) Centroid, (double X, double Y, double Z) Normal, (double X, double Y, double Z) NodeAxis);

    ShellGeometry? Geometry(int index)
    {
        if (_elements[index].ElemType != "shell" || _nodes[index] is not { Length: >= 3 } ids) return null;
        var points = new List<(double X, double Y, double Z)>(ids.Length);
        foreach (long id in ids)
        {
            if (!_coords.TryGetValue(id, out var p)) return null;
            points.Add(p);
        }
        double cx = points.Average(p => p.X), cy = points.Average(p => p.Y), cz = points.Average(p => p.Z);
        var a = Sub(points[1], points[0]);
        for (int i = 2; i < points.Count; i++)
        {
            var cross = Cross(a, Sub(points[i], points[0]));
            double len = Norm(cross);
            if (len < 1e-12) continue;
            return new ShellGeometry((cx, cy, cz), Scale(cross, 1 / len), Scale(a, 1 / Norm(a)));
        }
        return null;
    }

    Dictionary<int, FemWallStrip?> BuildWallStrips()
    {
        var result = new Dictionary<int, FemWallStrip?>();
        var geometry = new Dictionary<int, ShellGeometry>();
        for (int i = 0; i < _elements.Count; i++)
            if (Geometry(i) is { } g && Math.Abs(g.Normal.Z) < WallNormalZ)
                geometry[i] = g;

        // Стенки: связные КЭ стен с параллельными нормалями.
        var visited = new HashSet<int>();
        foreach (int seed in geometry.Keys)
        {
            if (!visited.Add(seed)) continue;
            var panel = new List<int> { seed };
            var normal = geometry[seed].Normal;
            for (int k = 0; k < panel.Count; k++)
                foreach (long node in _nodes[panel[k]]!)
                    foreach (int other in _byNode[node])
                        if (geometry.TryGetValue(other, out var og) && Math.Abs(Dot(og.Normal, normal)) > CollinearCos
                            && visited.Add(other))
                            panel.Add(other);

            var members = panel.ToHashSet();
            var levels = new List<double>();
            double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            foreach (long node in panel.SelectMany(i => _nodes[i]!).Distinct())
            {
                double z = _coords[node].Z;
                minZ = Math.Min(minZ, z);
                maxZ = Math.Max(maxZ, z);
                if (_byNode[node].Any(o => !members.Contains(o) && Braces(o))) levels.Add(z);
            }
            levels.Add(minZ);
            levels.Add(maxZ);
            levels.Sort();

            foreach (int i in panel)
            {
                var g = geometry[i];
                double zc = g.Centroid.Z;
                double below = levels.Where(z => z <= zc + LevelTolerance).DefaultIfEmpty(minZ).Max();
                double above = levels.Where(z => z >= zc - LevelTolerance && z > below + LevelTolerance)
                    .DefaultIfEmpty(maxZ).Min();
                double height = above - below;
                result[i] = height > LevelTolerance ? new FemWallStrip(height, VerticalAngle(g, _elements[i].LocalAxisAngleDeg)) : null;
            }
        }
        return result;
    }

    /// <summary>КЭ раскрепляет стену по высоте: перекрытие (негоризонтальная нормаль) или невертикальный стержень.</summary>
    bool Braces(int index)
    {
        if (IsBar(index)) return Axis(index) is { } a && Math.Abs(a.Z) < VerticalBarZ;
        return Geometry(index) is { } g && Math.Abs(g.Normal.Z) >= WallNormalZ;
    }

    /// <summary>Угол вертикали (проекции глобальной Z на плоскость КЭ) от оси X выдачи усилий, град.</summary>
    static double VerticalAngle(ShellGeometry g, double? localAxisAngleDeg)
    {
        var n = g.Normal;
        double a = (localAxisAngleDeg ?? 0) * Math.PI / 180.0;
        var outputX = Add(Scale(g.NodeAxis, Math.Cos(a)), Scale(Cross(n, g.NodeAxis), Math.Sin(a)));
        var outputY = Cross(n, outputX);
        // Проекция Z на плоскость: Z − (Z·n)·n; угол достаточно найти по компонентам в осях X/Y выдачи.
        var v = Sub((0.0, 0.0, 1.0), Scale(n, n.Z));
        return Math.Atan2(Dot(v, outputY), Dot(v, outputX)) * 180.0 / Math.PI;
    }

    static (double X, double Y, double Z) Sub((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    static (double X, double Y, double Z) Add((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    static (double X, double Y, double Z) Scale((double X, double Y, double Z) a, double k) => (a.X * k, a.Y * k, a.Z * k);
    static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    static double Norm((double X, double Y, double Z) a) => Math.Sqrt(Dot(a, a));
    static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
