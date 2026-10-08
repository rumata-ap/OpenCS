namespace CSfea.CScoreBridge.Structural;

/// <summary>
/// Плоскость симметрии схемы: глобальная ось <see cref="Axis"/> (0 — x, 1 — y, 2 — z) = <see cref="Coordinate"/>;
/// сохраняется сторона <see cref="KeepPositive"/> (true — координата ≥ плоскости, false — ≤).
/// </summary>
public sealed record RcSymmetryPlane(int Axis, double Coordinate, bool KeepPositive = false);

/// <summary>
/// Вырезка части симметричной схемы. Симметрия должна быть полной — геометрия, сечения, опоры и нагрузки, — и решение
/// должно оставаться симметричным (нет потери устойчивости с несимметричной формой).
/// </summary>
public static class RcSymmetry
{
    /// <summary>
    /// Часть схемы по плоскостям симметрии. Узлы на плоскости получают условия симметрии (перемещение по нормали и
    /// повороты вокруг двух осей плоскости — ноль); их узловые нагрузки и пружины делятся на 2 на каждую плоскость.
    /// КЭ, жёсткие тела и стержни, пересекающие плоскость или лежащие в ней, не поддерживаются (исключение).
    /// </summary>
    public static RcStructuralModel Cut(RcStructuralModel model, IReadOnlyList<RcSymmetryPlane> planes, double tol = 1e-6)
    {
        foreach (var p in planes)
            if (p.Axis is < 0 or > 2) throw new ArgumentException($"Плоскость симметрии: ось {p.Axis} вне 0..2.");

        // Сторона узла: −1 — отброшен, 0 — на плоскости (число плоскостей), 1 — внутри.
        var removed = new HashSet<int>();
        var onPlanes = new Dictionary<int, List<RcSymmetryPlane>>();
        foreach (var n in model.Nodes)
        {
            double[] c = [n.X, n.Y, n.Z];
            foreach (var p in planes)
            {
                double s = (c[p.Axis] - p.Coordinate) * (p.KeepPositive ? 1 : -1);
                if (s < -tol) removed.Add(n.Id);
                else if (s <= tol)
                {
                    if (!onPlanes.TryGetValue(n.Id, out var list)) onPlanes[n.Id] = list = [];
                    list.Add(p);
                }
            }
        }
        bool Inside(int id) => !removed.Contains(id) && !onPlanes.ContainsKey(id);

        // Элемент сохраняется, если ни один узел не отброшен; отбрасывается, если ни один узел не строго внутри.
        bool Keep(string what, IReadOnlyCollection<int> nodes)
        {
            bool anyRemoved = nodes.Any(removed.Contains), anyInside = nodes.Any(Inside);
            if (anyRemoved && anyInside) throw new InvalidOperationException($"{what} пересекает плоскость симметрии.");
            if (anyRemoved) return false;
            if (!anyInside) throw new InvalidOperationException($"{what} лежит в плоскости симметрии — не поддерживается.");
            return true;
        }

        var cut = new RcStructuralModel();
        cut.Nodes.AddRange(model.Nodes.Where(n => !removed.Contains(n.Id)));
        cut.Shells.AddRange(model.Shells.Where(s => Keep($"Оболочка {s.Id}", s.NodeIds)));
        cut.Beams.AddRange(model.Beams.Where(b => Keep($"Стержень {b.Id}", [b.NodeI, b.NodeJ])));
        cut.RigidBodies.AddRange(model.RigidBodies.Where(r => Keep($"Жёсткое тело {r.Id}", [r.Master, .. r.Slaves])));

        var keptShells = cut.Shells.Select(s => s.Id).ToHashSet();
        var keptBeams = cut.Beams.Select(b => b.Id).ToHashSet();
        double Share(int node) => onPlanes.TryGetValue(node, out var l) ? Math.Pow(0.5, l.Count) : 1.0;

        // Опоры: исходные маски + условия симметрии узлов на плоскостях.
        var masks = model.Supports.Where(s => !removed.Contains(s.NodeId))
            .GroupBy(s => s.NodeId).ToDictionary(g => g.Key, g => g.Aggregate(0, (m, s) => m | s.Mask));
        foreach (var (node, list) in onPlanes)
        {
            if (removed.Contains(node)) continue;
            int mask = list.Aggregate(0, (m, p) => m | SymmetryMask(p.Axis));
            masks[node] = masks.TryGetValue(node, out int m0) ? m0 | mask : mask;
        }
        cut.Supports.AddRange(masks.OrderBy(kv => kv.Key).Select(kv => new RcSupport(kv.Key, kv.Value)));
        cut.Springs.AddRange(model.Springs.Where(s => !removed.Contains(s.NodeId))
            .Select(s => s with { Stiffness = s.Stiffness * Share(s.NodeId) }));

        foreach (var lc in model.LoadCases)
        {
            var c = new RcLoadCase(lc.Id, lc.Name);
            c.Nodal.AddRange(lc.Nodal.Where(p => !removed.Contains(p.NodeId))
                .Select(p => p with { Force = p.Force.Select(f => f * Share(p.NodeId)).ToArray() }));
            c.Shells.AddRange(lc.Shells.Where(s => keptShells.Contains(s.ShellId)));
            c.Beams.AddRange(lc.Beams.Where(b => keptBeams.Contains(b.BeamId)));
            c.BeamEnds.AddRange(lc.BeamEnds.Where(b => keptBeams.Contains(b.BeamId)));
            cut.LoadCases.Add(c);
        }
        cut.Stages.AddRange(model.Stages);
        return cut;
    }

    /// <summary>
    /// Разбиение четырёхугольных оболочек, пересекаемых плоскостью <paramref name="plane"/>, на две: новые узлы — в
    /// точках пересечения плоскостью двух противоположных рёбер (общие у соседних КЭ). Нужно, когда сетка источника не
    /// имеет узлов на плоскости симметрии. Первая половина сохраняет номер КЭ, вторая получает новый; равномерные
    /// нагрузки на КЭ копируются на обе половины. Стержни и жёсткие тела не трогаются (их пересечение отловит
    /// <see cref="Cut"/>). Треугольники и иные варианты пересечения — исключение.
    /// </summary>
    public static RcStructuralModel SplitAt(RcStructuralModel model, RcSymmetryPlane plane, double tol = 1e-6)
    {
        var nodes = model.Nodes.ToDictionary(n => n.Id);
        double D(int id) { var n = nodes[id]; return (plane.Axis switch { 0 => n.X, 1 => n.Y, _ => n.Z }) - plane.Coordinate; }

        var res = new RcStructuralModel();
        res.Nodes.AddRange(model.Nodes);
        int nextNode = model.Nodes.Max(n => n.Id) + 1, nextShell = model.Shells.Max(s => s.Id) + 1;
        var edgeNodes = new Dictionary<(int, int), int>();
        int EdgeNode(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (edgeNodes.TryGetValue(key, out int id)) return id;
            double da = D(a), db = D(b), t = da / (da - db);
            RcNode na = nodes[a], nb = nodes[b];
            var n = new RcNode(nextNode++, na.X + t * (nb.X - na.X), na.Y + t * (nb.Y - na.Y), na.Z + t * (nb.Z - na.Z));
            res.Nodes.Add(n);
            nodes[n.Id] = n;
            return edgeNodes[key] = n.Id;
        }

        var twins = new Dictionary<int, int>();
        foreach (var s in model.Shells)
        {
            var d = s.NodeIds.Select(D).ToArray();
            if (d.Min() >= -tol || d.Max() <= tol) { res.Shells.Add(s); continue; }
            bool Cross(int i, int j) => d[i] * d[j] < 0 && Math.Abs(d[i]) > tol && Math.Abs(d[j]) > tol;
            if (s.NodeIds.Length != 4)
                throw new InvalidOperationException($"Оболочка {s.Id}: разбиение плоскостью поддержано только для четырёхугольников.");
            var n = s.NodeIds;
            int[] h1, h2;
            if (Cross(0, 1) && Cross(2, 3) && !Cross(1, 2) && !Cross(3, 0))
            {
                int a = EdgeNode(n[0], n[1]), b = EdgeNode(n[2], n[3]);
                (h1, h2) = ([n[0], a, b, n[3]], [a, n[1], n[2], b]);
            }
            else if (Cross(1, 2) && Cross(3, 0) && !Cross(0, 1) && !Cross(2, 3))
            {
                int a = EdgeNode(n[1], n[2]), b = EdgeNode(n[3], n[0]);
                (h1, h2) = ([n[0], n[1], a, b], [b, a, n[2], n[3]]);
            }
            else throw new InvalidOperationException($"Оболочка {s.Id}: плоскость должна пересекать два противоположных ребра.");
            res.Shells.Add(s with { NodeIds = h1 });
            res.Shells.Add(s with { Id = nextShell, NodeIds = h2 });
            twins[s.Id] = nextShell++;
        }

        res.Beams.AddRange(model.Beams);
        res.RigidBodies.AddRange(model.RigidBodies);
        res.Supports.AddRange(model.Supports);
        res.Springs.AddRange(model.Springs);
        foreach (var lc in model.LoadCases)
        {
            var c = new RcLoadCase(lc.Id, lc.Name);
            c.Nodal.AddRange(lc.Nodal);
            foreach (var l in lc.Shells)
            {
                c.Shells.Add(l);
                if (twins.TryGetValue(l.ShellId, out int t)) c.Shells.Add(l with { ShellId = t });
            }
            c.Beams.AddRange(lc.Beams);
            c.BeamEnds.AddRange(lc.BeamEnds);
            res.LoadCases.Add(c);
        }
        res.Stages.AddRange(model.Stages);
        return res;
    }

    /// <summary>
    /// Маска DOF узла на плоскости симметрии с нормалью по оси <paramref name="axis"/>: перемещение по нормали и
    /// повороты вокруг двух других осей (повороты — псевдовекторы, при отражении меняют знак именно они).
    /// </summary>
    public static int SymmetryMask(int axis) => (1 << axis) | (0b111 << 3 & ~(1 << (3 + axis)));
}
