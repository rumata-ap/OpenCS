using System.Globalization;
using CScore.Fem;
using CScore.Fem.Editing;
using CScore.Planar;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Итог разворота КонЭ: что развёрнуто, что сказать в журнале, осталась ли сетка схемы актуальной.</summary>
public sealed class FemMemberOrientationOutcome
{
    /// <summary>Разворот выполнен (иначе — <see cref="Error"/>, база не менялась).</summary>
    public bool Done { get; init; }
    public string? Error { get; init; }
    public FemOrientationResult? Flip { get; init; }
    /// <summary>Сетка схемы пересобрана с теми же КЭ (развёрнутыми); false — сетки не было или она устарела раньше.</summary>
    public bool MeshKept { get; init; }
    /// <summary>Строки журнала: сводка — <see cref="Info"/>, оговорки — <see cref="Warnings"/>.</summary>
    public List<string> Info { get; } = [];
    public List<string> Warnings { get; } = [];
}

/// <summary>
/// «Развернуть ось» стержней и «Сменить нормаль» пластин — одно для 3D-вида, дерева и CLI-тестов: состав схемы
/// (КонЭ, нагрузки, линейные связи) — через сессию и <see cref="FemSchemaAuthoring.Save"/>, области и КЭ сетки — сразу
/// в базу (<see cref="FemOrientationFlip"/>). Если сетка схемы была актуальной, она тут же пересобирается с отражёнными
/// снимками областей вместо Gmsh — КЭ те же, с новым обходом; теги КЭ и узлов развёрнутых стержней, сменившиеся при
/// новой разбивке, переносятся в нагрузки сеточного уровня и группы КЭ. Результаты постановок схемы сбрасываются.
/// Вызывать при сохранённой схеме: сессия читается из базы.
/// </summary>
public static class FemMemberOrientation
{
    /// <summary>Из КонЭ <paramref name="tags"/> — те, что разворачиваются: стержни и пластины.</summary>
    public static (List<string> Bars, List<string> Shells) Split(IEnumerable<FemMember> members, IEnumerable<string> tags)
    {
        var set = tags.ToHashSet(StringComparer.Ordinal);
        var chosen = members.Where(m => set.Contains(m.ElemTag)).ToList();
        return (chosen.Where(FemOrientationFlip.IsBar).Select(m => m.ElemTag).ToList(),
            chosen.Where(FemOrientationFlip.IsShell).Select(m => m.ElemTag).ToList());
    }

    /// <summary>Разворачивает КонЭ <paramref name="tags"/> схемы <paramref name="schema"/>. <paramref name="gmsh"/> — для
    /// проверки и пересборки сетки (null — сетка не пересобирается и устаревает). В <paramref name="log"/> — сброс
    /// результатов постановок схемы; сводка разворота — в итоге.</summary>
    public static async Task<FemMemberOrientationOutcome> ApplyAsync(DatabaseService db, FemSchema schema,
        IReadOnlyCollection<string> tags, GmshSettings? gmsh, ILogService log, CancellationToken cancellationToken = default)
    {
        var session = FemSchemaAuthoring.OpenSession(db, schema);
        var (bars, shells) = Split(session.Members, tags);
        if (bars.Count + shells.Count == 0)
            return new FemMemberOrientationOutcome { Error = Loc.S("FemOrientNothing") };

        var meshNodes = db.GetFemMeshNodes(schema.Id);
        var meshElements = db.GetFemMeshElements(schema.Id);
        double? barStep = db.GetFemSchemaMeshSteps(schema.Id).Bar;
        var meshService = gmsh != null ? new FemSchemaMeshService(db, gmsh) : null;
        var warnings = new List<string>();

        // Сетку можно сохранить, только если она актуальна до разворота: отражается последний снимок области.
        bool wasCurrent = false;
        if (meshService != null && meshElements.Count > 0)
        {
            try
            {
                var check = await meshService.CheckAsync(schema.Id, session.Nodes, session.Members, barStep, cancellationToken);
                wasCurrent = check.IsCurrent == true && !check.HasErrors;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or InvalidOperationException
                                           or UnauthorizedAccessException)
            {
                warnings.Add(string.Format(Loc.S("FemOrientMeshCheckFailed"), ex.Message));
            }
        }

        var regions = db.GetPlanarRegions(schema.Id).ToDictionary(r => r.Id);
        var snapshots = new Dictionary<int, PlanarMeshSnapshot>();
        if (wasCurrent)
            foreach (var m in session.Members.Where(m => shells.Contains(m.ElemTag) && !m.IsMeshLocked && m.PlanarRegionId != null))
                if (db.GetLatestPlanarMeshSnapshot(m.PlanarRegionId!.Value) is { IsCalculable: true } s)
                    snapshots[m.PlanarRegionId.Value] = s;

        var flip = FemOrientationFlip.Apply(new FemOrientationInput
        {
            Nodes = session.Nodes, Members = session.Members, MemberLoads = session.MemberLoads,
            ElementLoads = session.ElementLoads, LineSupports = session.LineSupports, Groups = session.MemberGroups,
            Regions = regions, Snapshots = snapshots, MeshNodes = meshNodes, MeshElements = meshElements,
        }, [.. bars, .. shells]);

        if (!FemSchemaAuthoring.Save(db, session, out var diagnostics))
            return new FemMemberOrientationOutcome
            {
                Error = string.Join("; ", diagnostics.Where(d => d.IsError).Select(d => d.Message)),
            };
        foreach (var region in flip.Regions) db.UpdatePlanarRegion(region, schema.Id);
        db.UpdateFemElementsOrientation(flip.Elements);

        bool kept = false;
        if (wasCurrent && meshService != null)
        {
            try
            {
                var built = await meshService.BuildReorientedAsync(schema.Id, session.Nodes, session.Members, barStep,
                    flip.Snapshots, cancellationToken);
                if (built.Mesh is { } mesh && !built.HasErrors)
                {
                    if (RemapTags(session, flip, meshNodes, mesh, db.GetFemMeshNodeSupports(schema.Id), db.GetFemSprings(schema.Id)) is { } lost)
                        warnings.Add(string.Format(Loc.S("FemOrientMeshBoundaryMoved"), lost));
                    meshService.Save(schema.Id, mesh);
                    FemSchemaAuthoring.Save(db, session, out _);
                    kept = true;
                }
                else warnings.AddRange(built.Diagnostics.Where(d => d.IsError).Take(5).Select(d => d.Message));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or InvalidOperationException
                                           or UnauthorizedAccessException or OperationCanceledException)
            {
                // Разворот уже записан: сетка просто устаревает и перестроится перед расчётом.
                if (ex is not OperationCanceledException) warnings.Add(string.Format(Loc.S("FemOrientMeshCheckFailed"), ex.Message));
            }
        }

        // Расчётная геометрия изменилась — результаты постановок схемы сбрасываются (как при новой сетке).
        new FemSchemaMeshService(db, gmsh ?? new GmshSettings()).ReportMeshChanged(schema, log);
        var outcome = new FemMemberOrientationOutcome { Done = true, Flip = flip, MeshKept = kept };
        Describe(outcome, flip, meshElements.Count > 0);
        outcome.Warnings.AddRange(warnings);
        return outcome;
    }

    /// <summary>Строки журнала по итогу разворота.</summary>
    static void Describe(FemMemberOrientationOutcome outcome, FemOrientationResult flip, bool hadMesh)
    {
        if (flip.Bars.Count > 0)
            outcome.Info.Add(string.Format(Loc.S("FemOrientDoneBars"), flip.Bars.Count, List(flip.Bars)));
        if (flip.Shells.Count > 0)
            outcome.Info.Add(string.Format(Loc.S("FemOrientDoneShells"), flip.Shells.Count, List(flip.Shells)));
        if (flip.MemberLoads + flip.ElementLoads + flip.LineSupports > 0)
            outcome.Info.Add(string.Format(Loc.S("FemOrientLoads"), flip.MemberLoads, flip.ElementLoads, flip.LineSupports));
        if (hadMesh)
            outcome.Info.Add(Loc.S(outcome.MeshKept ? "FemOrientMeshKept" : "FemOrientMeshStale"));
        if (flip.Bars.Count > 0) outcome.Info.Add(Loc.S("FemOrientSectionMirrored"));
        foreach (var reason in Enum.GetValues<FemOrientationLoadSkip>())
        {
            var loads = flip.SkippedLoads.Where(s => s.Reason == reason).Select(s => Describe(s.Load)).ToList();
            if (loads.Count > 0) outcome.Warnings.Add(string.Format(Loc.S("FemOrientSkipped" + reason), List(loads)));
        }
        if (flip.LostLineSupports.Count > 0)
            outcome.Warnings.Add(string.Format(Loc.S("FemOrientLineLost"), List(flip.LostLineSupports.Select(s => s.Key.ToString()).ToList())));
    }

    static string Describe(FemElementLoad load) => load.Id > 0
        ? string.Create(CultureInfo.InvariantCulture, $"№{load.Id} ({load.LoadKind})")
        : $"({load.LoadKind})";

    static string List(IReadOnlyList<string> items) =>
        items.Count <= 10 ? string.Join(", ", items) : string.Join(", ", items.Take(10)) + " " + string.Format(Loc.S("FemOrientMore"), items.Count - 10).Trim();

    /// <summary>
    /// Новая разбивка развёрнутых стержней даёт их промежуточным узлам и КЭ другие теги (нумерация идёт от нового
    /// начала). Теги переносятся по положению в нагрузки на КЭ (список КЭ), группы КЭ и нагрузки на узлы сетки сессии.
    /// Возвращает теги узлов с ручными ГУ сетки, сменившие тег (их ГУ остались на прежнем теге), или null.
    /// </summary>
    static string? RemapTags(FemSchemaEditSession session, FemOrientationResult flip, IReadOnlyList<FemMeshNode> oldNodes,
        FemPlanarMaterializationResult mesh, IReadOnlyList<FemMeshNodeSupport> supports, IReadOnlyList<FemSpring> springs)
    {
        static string Key(double x, double y, double z) => string.Create(CultureInfo.InvariantCulture,
            $"{Math.Round(x, 6):R}|{Math.Round(y, 6):R}|{Math.Round(z, 6):R}");
        var newByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in mesh.Nodes) newByKey.TryAdd(Key(n.X, n.Y, n.Z), n.NodeTag);
        var nodeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var oldByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in oldNodes)
        {
            oldByTag.TryAdd(n.NodeTag, n);
            if (newByKey.TryGetValue(Key(n.X, n.Y, n.Z), out var t) && t != n.NodeTag) nodeMap[n.NodeTag] = t;
        }

        // КЭ развёрнутых стержней своей сетки — по паре концов.
        var bars = flip.Bars.ToHashSet(StringComparer.Ordinal);
        string? EndsKey(IReadOnlyList<string>? ids, Func<string, (double, double, double)?> point)
        {
            if (ids is not { Count: 2 } || point(ids[0]) is not { } a || point(ids[1]) is not { } b) return null;
            var keys = new[] { Key(a.Item1, a.Item2, a.Item3), Key(b.Item1, b.Item2, b.Item3) };
            Array.Sort(keys, StringComparer.Ordinal);
            return keys[0] + "/" + keys[1];
        }
        var meshNodeByTag = mesh.Nodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        var newElements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in mesh.Elements.Where(e => e.SourceMemberTag is { } t && bars.Contains(t)))
            if (EndsKey(FemMeshTopology.ReadNodeTags(e), t => meshNodeByTag.TryGetValue(t, out var n) ? (n.X, n.Y, n.Z) : null) is { } k)
                newElements.TryAdd(k, e.ElemTag);
        var elementMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in flip.Elements.Where(e => e.Origin == FemMember.MeshSourceGenerated && e.SourceMemberTag is { } t && bars.Contains(t)))
            if (EndsKey(FemMeshTopology.ReadNodeTags(e), t => oldByTag.TryGetValue(t, out var n) ? (n.X, n.Y, n.Z) : null) is { } k
                && newElements.TryGetValue(k, out var tag) && tag != e.ElemTag)
                elementMap[e.ElemTag] = tag;

        if (elementMap.Count > 0)
        {
            foreach (var l in session.ElementLoads.Where(l => l.TargetKind == FemLoadTargetKinds.Elements && l.TargetTags.Any(elementMap.ContainsKey)))
                l.SetTargetTags(l.TargetTags.Select(t => elementMap.GetValueOrDefault(t, t)).ToList());
            foreach (var g in session.MemberGroups.Where(g => g.IsMeshGroup && g.Tags.Any(elementMap.ContainsKey)))
                g.SetTags(g.Tags.Select(t => elementMap.GetValueOrDefault(t, t)).ToList());
        }
        foreach (var l in session.MeshNodeLoads)
            if (nodeMap.TryGetValue(l.MeshNodeTag, out var t)) l.MeshNodeTag = t;

        var moved = supports.Where(s => s.Origin == FemLoadOrigin.Manual).Select(s => s.NodeTag)
            .Concat(springs.Where(s => s.Origin == FemLoadOrigin.Manual && s.TargetKind == FemSpringTargetKinds.MeshNode).Select(s => s.NodeTag))
            .Where(nodeMap.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
        return moved.Count == 0 ? null : string.Join(", ", moved.Take(20));
    }
}
