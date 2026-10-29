namespace CScore.Fem;

/// <summary>
/// Жёсткие тела на узлах схемы (конструктивный уровень; спека <c>2026-10-29-storey-fragment-rigid-points-design.md</c>):
/// фрагмент этажа переносит импортные АЖТ родителя «по точкам» — ведущий и ведомые заданы тегами <see cref="FemNode"/>.
/// После сборки сетки <see cref="Materialize"/> даёт настоящие <see cref="FemRigidBody"/> на узлах сетки в этих точках.
/// </summary>
public static class FemNodeRigidBodies
{
    /// <summary>Происхождение тел сетки, построенных по телам на узлах схемы.</summary>
    public const string Provenance = "auto:node_points";

    const double Tolerance = FemMeshDiscretizer.CollinearToleranceM;

    /// <summary>
    /// Тела на узлах сетки по телам на узлах схемы <paramref name="bodies"/>. Узел схемы → узел сетки: по
    /// <see cref="FemMeshNode.SourceNodeTag"/>, иначе по координатам (±<see cref="FemMeshDiscretizer.CollinearToleranceM"/>);
    /// не нашёлся — точка отбрасывается. Узел ведом одним телом: ведомый из <paramref name="blocked"/> (закрепления,
    /// пружины, ручные и импортные тела), из тел <paramref name="otherBodies"/> или уже занятый более ранним телом по
    /// точкам — отбрасывается; ведущий, ведомый в другом теле, — тело не создаётся. Без ведомых тело не создаётся.
    /// </summary>
    public static FemRigidJunctionResult Materialize(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemRigidBody> bodies,
        IReadOnlySet<string> blocked,
        IReadOnlyList<FemRigidBody> otherBodies)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        var result = new List<FemRigidBody>();
        if (bodies.Count == 0) return new(result, diagnostics);

        var bySource = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in meshNodes)
            if (n.SourceNodeTag != null) bySource.TryAdd(n.SourceNodeTag, n.NodeTag);
        var grid = new Dictionary<(long, long, long), List<FemMeshNode>>();
        foreach (var n in meshNodes)
        {
            var key = Cell(n.X, n.Y, n.Z);
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = [];
            list.Add(n);
        }
        var nodeByTag = nodes.GroupBy(n => n.NodeTag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        string? MeshTag(string nodeTag)
        {
            if (bySource.TryGetValue(nodeTag, out var tag)) return tag;
            if (!nodeByTag.TryGetValue(nodeTag, out var n)) return null;
            var (i, j, k) = Cell(n.X, n.Y, n.Z);
            FemMeshNode? best = null;
            double bestD = double.MaxValue;
            for (long a = i - 1; a <= i + 1; a++)
            for (long b = j - 1; b <= j + 1; b++)
            for (long c = k - 1; c <= k + 1; c++)
                if (grid.TryGetValue((a, b, c), out var list))
                    foreach (var m in list)
                    {
                        double dx = m.X - n.X, dy = m.Y - n.Y, dz = m.Z - n.Z, d = dx * dx + dy * dy + dz * dz;
                        if (d <= Tolerance * Tolerance && d < bestD) (best, bestD) = (m, d);
                    }
            return best?.NodeTag;
        }

        var otherSlaves = otherBodies.SelectMany(b => b.SlaveNodeTags).ToHashSet(StringComparer.Ordinal);
        var otherNodes = otherBodies.SelectMany(b => b.SlaveNodeTags.Append(b.MasterNodeTag)).ToHashSet(StringComparer.Ordinal);
        var usedSlaves = new HashSet<string>(StringComparer.Ordinal);
        var usedNodes = new HashSet<string>(StringComparer.Ordinal);
        var unmeshed = new SortedSet<string>(TagComparer);
        var blockedHits = new SortedSet<string>(TagComparer);
        var dropped = new SortedSet<string>(TagComparer);

        foreach (var body in bodies)
        {
            string label = body.SourceElemTag ?? body.MasterNodeTag;
            var master = MeshTag(body.MasterNodeTag);
            if (master == null) unmeshed.Add(body.MasterNodeTag);
            if (master == null || otherSlaves.Contains(master) || usedSlaves.Contains(master))
            {
                dropped.Add(label);
                continue;
            }
            var slaves = new List<string>();
            foreach (var slaveNode in body.SlaveNodeTags)
            {
                var slave = MeshTag(slaveNode);
                if (slave == null) { unmeshed.Add(slaveNode); continue; }
                if (slave == master || slaves.Contains(slave)) continue;
                if (blocked.Contains(slave) || otherNodes.Contains(slave) || usedNodes.Contains(slave))
                {
                    blockedHits.Add(slave);
                    continue;
                }
                slaves.Add(slave);
            }
            if (slaves.Count == 0)
            {
                dropped.Add(label);
                continue;
            }
            var created = new FemRigidBody
            {
                MasterNodeTag = master,
                Mask = (body.Mask & FemBoundaryDofs.All) == 0 ? FemBoundaryDofs.All : body.Mask & FemBoundaryDofs.All,
                Kind = body.Kind,
                Origin = Provenance,
                SourceElemTag = body.SourceElemTag,
            };
            created.SetSlaveNodeTags(slaves);
            result.Add(created);
            usedNodes.Add(master);
            foreach (var s in slaves) { usedNodes.Add(s); usedSlaves.Add(s); }
        }

        if (unmeshed.Count > 0)
            diagnostics.Add(new("node_rigid_point_unmeshed",
                $"Узлы схемы {Short(unmeshed)} АЖТ по узлам схемы не попали в сетку — точки отброшены.", false, [.. unmeshed]));
        if (blockedHits.Count > 0)
            diagnostics.Add(new("node_rigid_slave_blocked",
                $"Узлы сетки {Short(blockedHits)} с закреплениями, пружинами или в других АЖТ не включены в АЖТ по узлам схемы.",
                false, [.. blockedHits]));
        if (dropped.Count > 0)
            diagnostics.Add(new("node_rigid_body_dropped",
                $"АЖТ по узлам схемы {Short(dropped)} не созданы: ведущий не в сетке или ведом другим телом, либо не осталось ведомых.",
                false, [.. dropped]));
        return new(result, diagnostics);
    }

    static (long, long, long) Cell(double x, double y, double z) =>
        ((long)Math.Floor(x / Tolerance), (long)Math.Floor(y / Tolerance), (long)Math.Floor(z / Tolerance));

    /// <summary>Первые 10 номеров и «… (всего N)».</summary>
    public static string Short(IEnumerable<string> tags)
    {
        var list = tags.Distinct().ToList();
        return list.Count <= 10 ? string.Join(", ", list) : string.Join(", ", list.Take(10)) + $" … (всего {list.Count})";
    }

    /// <summary>Числовые теги — по числу, прочие — после, по строке.</summary>
    public static readonly Comparer<string> TagComparer = Comparer<string>.Create((x, y) =>
    {
        bool nx = int.TryParse(x, out int a), ny = int.TryParse(y, out int b);
        if (nx && ny) return a.CompareTo(b);
        if (nx != ny) return nx ? -1 : 1;
        return string.CompareOrdinal(x, y);
    });
}
