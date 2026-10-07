using CScore.Fem.Loads;

namespace CScore.Fem;

/// <summary>Суммарная пружина узла сетки: жёсткости по DOF 0–5 (X Y Z UX UY UZ), Н/м и Н·м/рад.</summary>
public sealed record FemResolvedSpring(string MeshNodeTag, IReadOnlyList<double> Stiffnesses);

/// <summary>Жёсткое тело на узлах сетки после проверок.</summary>
public sealed record FemResolvedRigidBody(string MasterNodeTag, IReadOnlyList<string> SlaveNodeTags, int Mask, string? SourceElemTag);

/// <summary>ГУ схемы, сведённые к узлам и КЭ сетки, и диагностики сведения.</summary>
public sealed class FemResolvedBoundary
{
    /// <summary>Маска закреплённых DOF по тегу узла сетки (только ненулевые).</summary>
    public required IReadOnlyDictionary<string, int> SupportMasks { get; init; }
    public required IReadOnlyList<FemResolvedSpring> Springs { get; init; }
    public required IReadOnlyList<FemResolvedRigidBody> RigidBodies { get; init; }
    /// <summary>Освобождения концов стержней по тегу КЭ (хотя бы один конец ненулевой).</summary>
    public required IReadOnlyDictionary<string, (int I, int J)> Releases { get; init; }
    /// <summary>C1 пластин по тегу КЭ, Н/м³ (только положительные).</summary>
    public required IReadOnlyDictionary<string, double> FoundationC1 { get; init; }
    public required IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; }

    /// <summary>Есть ошибки, с которыми схему считать нельзя.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.IsError);
}

/// <summary>
/// ГУ обоих уровней схемы → узлы и КЭ сетки: закрепления <see cref="FemNode.DofMask"/> (через
/// <see cref="FemMeshNode.SourceNodeTag"/>) и <see cref="FemMeshNodeSupport"/>, пружины, жёсткие тела, освобождения
/// стержней и C1 пластин. Общий путь для адаптера CSfea, 3D-показа и сводки. Пропавшие узлы/КЭ и бессмысленные
/// сочетания — предупреждения (объект пропускается); ведомый узел закреплён или ведом дважды — ошибка.
/// </summary>
public static class FemBoundaryResolver
{
    public static FemResolvedBoundary Resolve(
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemMeshNodeSupport> supports,
        IReadOnlyList<FemSpring> springs,
        IReadOnlyList<FemRigidBody> rigidBodies)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        var meshTags = meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
        var meshBySource = meshNodes.Where(n => n.SourceNodeTag != null)
            .ToLookup(n => n.SourceNodeTag!, n => n.NodeTag, StringComparer.Ordinal);

        // Закрепления: конструктивный уровень через исходный узел, сеточный — напрямую.
        var masks = new Dictionary<string, int>(StringComparer.Ordinal);
        void Fix(string tag, int mask) => masks[tag] = (masks.TryGetValue(tag, out int m) ? m : 0) | mask;
        var notInMesh = new List<string>();
        foreach (var node in nodes)
        {
            int mask = node.DofMask & FemBoundaryDofs.All;
            if (mask == 0) continue;
            var mapped = meshBySource[node.NodeTag].ToList();
            if (mapped.Count == 0) { notInMesh.Add(node.NodeTag); continue; }
            foreach (var tag in mapped) Fix(tag, mask);
        }
        Warn(diagnostics, "node_support_not_in_mesh", "Закреплённые узлы схемы не попали в сетку", notInMesh);

        var missing = new List<string>();
        foreach (var s in supports)
        {
            int mask = s.Mask & FemBoundaryDofs.All;
            if (mask == 0) continue;
            if (!meshTags.Contains(s.NodeTag)) { missing.Add(s.NodeTag); continue; }
            Fix(s.NodeTag, mask);
        }
        Warn(diagnostics, "mesh_support_node_missing", "Закрепления на отсутствующих узлах сетки", missing);

        // Пружины: сумма по узлу сетки; жёсткость по закреплённому DOF отбрасывается.
        var stiffness = new Dictionary<string, double[]>(StringComparer.Ordinal);
        missing = [];
        foreach (var s in springs)
        {
            if (s.ActiveMask == 0) continue;
            IEnumerable<string> targets;
            if (s.TargetKind == FemSpringTargetKinds.Node)
            {
                var mapped = meshBySource[s.NodeTag].ToList();
                if (mapped.Count == 0) { missing.Add(s.NodeTag); continue; }
                targets = mapped;
            }
            else
            {
                if (!meshTags.Contains(s.NodeTag)) { missing.Add(s.NodeTag); continue; }
                targets = [s.NodeTag];
            }
            var k = s.Stiffnesses;
            foreach (var tag in targets)
            {
                if (!stiffness.TryGetValue(tag, out var sum)) stiffness[tag] = sum = new double[6];
                for (int i = 0; i < 6; i++) sum[i] += k[i];
            }
        }
        Warn(diagnostics, "spring_node_missing", "Пружины на отсутствующих узлах", missing);

        var inFixed = new List<string>();
        foreach (var (tag, k) in stiffness)
        {
            int fixedMask = masks.TryGetValue(tag, out int m) ? m : 0;
            bool hit = false;
            for (int i = 0; i < 6; i++)
                if ((fixedMask & (1 << i)) != 0 && k[i] != 0) { k[i] = 0; hit = true; }
            if (hit) inFixed.Add(tag);
        }
        Warn(diagnostics, "spring_in_fixed_dof",
            "Пружины по закреплённым направлениям не имеют смысла и не учитываются; узлы", inFixed);
        var resolvedSprings = stiffness.Where(kv => kv.Value.Any(v => v != 0))
            .OrderBy(kv => kv.Key, TagComparer)
            .Select(kv => new FemResolvedSpring(kv.Key, kv.Value))
            .ToList();

        // Жёсткие тела.
        var bodies = new List<FemResolvedRigidBody>();
        var masterMissing = new List<string>();
        var slaveMissing = new List<string>();
        var slaveIsMaster = new List<string>();
        var slaveOwner = new Dictionary<string, int>(StringComparer.Ordinal);
        var slaveTwice = new List<string>();
        var slaveFixed = new List<string>();
        foreach (var b in rigidBodies)
        {
            int mask = b.Mask & FemBoundaryDofs.All;
            if (mask == 0) continue;
            if (!meshTags.Contains(b.MasterNodeTag)) { masterMissing.Add(b.MasterNodeTag); continue; }
            var slaves = new List<string>();
            foreach (var slave in b.SlaveNodeTags)
            {
                if (!meshTags.Contains(slave)) { slaveMissing.Add(slave); continue; }
                if (slave == b.MasterNodeTag) { slaveIsMaster.Add(slave); continue; }
                slaves.Add(slave);
            }
            if (slaves.Count == 0) continue;
            int index = bodies.Count;
            foreach (var slave in slaves)
            {
                if (!slaveOwner.TryAdd(slave, index)) slaveTwice.Add(slave);
                if (masks.TryGetValue(slave, out int fixedMask) && (fixedMask & mask) != 0) slaveFixed.Add(slave);
            }
            bodies.Add(new FemResolvedRigidBody(b.MasterNodeTag, slaves, mask, b.SourceElemTag));
        }
        Warn(diagnostics, "rigid_master_missing", "Жёсткие тела с отсутствующим ведущим узлом пропущены; узлы", masterMissing);
        Warn(diagnostics, "rigid_slave_missing", "Отсутствующие ведомые узлы жёстких тел пропущены", slaveMissing);
        Warn(diagnostics, "rigid_slave_is_master", "Ведомый узел совпадает с ведущим и пропущен", slaveIsMaster);
        Error(diagnostics, "rigid_slave_twice", "Узлы ведомы в нескольких жёстких телах", slaveTwice);
        Error(diagnostics, "rigid_slave_supported", "Ведомые узлы жёстких тел закреплены по объединяемым направлениям", slaveFixed);

        // ГУ КЭ: освобождения — только у стержней, C1 — только у пластин.
        var releases = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        var c1 = new Dictionary<string, double>(StringComparer.Ordinal);
        var releaseNotBeam = new List<string>();
        var c1NotShell = new List<string>();
        var c1Negative = new List<string>();
        foreach (var e in meshElements)
        {
            int ri = (e.ReleaseI ?? 0) & FemBoundaryDofs.All, rj = (e.ReleaseJ ?? 0) & FemBoundaryDofs.All;
            if (ri != 0 || rj != 0)
            {
                if (e.ElemType == "beam") releases[e.ElemTag] = (ri, rj);
                else releaseNotBeam.Add(e.ElemTag);
            }
            if (e.FoundationC1 is { } value && value != 0)
            {
                if (e.ElemType != "shell") c1NotShell.Add(e.ElemTag);
                else if (value < 0) c1Negative.Add(e.ElemTag);
                else c1[e.ElemTag] = value;
            }
        }
        Warn(diagnostics, "release_not_beam", "Шарниры у КЭ, не являющихся стержнями, не учитываются; КЭ", releaseNotBeam);
        Warn(diagnostics, "foundation_not_shell", "Коэффициент постели у КЭ, не являющихся пластинами, не учитывается; КЭ", c1NotShell);
        Warn(diagnostics, "foundation_negative", "Отрицательный коэффициент постели не учитывается; КЭ", c1Negative);

        return new FemResolvedBoundary
        {
            SupportMasks = masks,
            Springs = resolvedSprings,
            RigidBodies = bodies,
            Releases = releases,
            FoundationC1 = c1,
            Diagnostics = diagnostics,
        };
    }

    static readonly Comparer<string> TagComparer = Comparer<string>.Create((a, b) =>
    {
        bool na = int.TryParse(a, out int x), nb = int.TryParse(b, out int y);
        if (na && nb) return x.CompareTo(y);
        if (na != nb) return na ? -1 : 1;
        return string.CompareOrdinal(a, b);
    });

    static void Warn(List<FemValidationDiagnostic> diagnostics, string code, string text, List<string> tags) =>
        Add(diagnostics, code, text, tags, isError: false);

    static void Error(List<FemValidationDiagnostic> diagnostics, string code, string text, List<string> tags) =>
        Add(diagnostics, code, text, tags, isError: true);

    static void Add(List<FemValidationDiagnostic> diagnostics, string code, string text, List<string> tags, bool isError)
    {
        if (tags.Count == 0) return;
        var distinct = tags.Distinct(StringComparer.Ordinal).OrderBy(t => t, TagComparer).ToArray();
        diagnostics.Add(new(code, $"{text}: {FemLoadTargets.Sample(distinct)}.", isError, distinct));
    }
}
