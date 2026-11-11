using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CScore;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using OpenCS.Gmsh;
using OpenCS.Gmsh.Runtime;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Итог сборки сетки схемы.</summary>
public sealed class FemSchemaMeshBuildResult
{
    /// <summary>Сетка собрана (в режиме проверки — пересобрана в памяти); null — сборка прервана ошибками.</summary>
    public FemPlanarMaterializationResult? Mesh { get; init; }
    public List<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public bool HasErrors => Diagnostics.Any(d => d.IsError);
    public int RegionCount { get; init; }
    /// <summary>Сколько сеток областей построено Gmsh (остальные — сохранённые снимки с тем же отпечатком).</summary>
    public int RebuiltRegionCount { get; init; }
    /// <summary>Режим проверки: сохранённая сетка схемы совпадает с пересобранной; null — не проверялось.</summary>
    public bool? IsCurrent { get; init; }
    /// <summary>КонЭ импортной (сохранённой) сетки, у которых нарушена связность с новой сеткой
    /// (<see cref="FemPlanarMaterializationResult.ImportedMembersAtFault"/>); пусто — нарушений нет.</summary>
    public IReadOnlySet<string> ImportedMembersAtFault { get; init; } = new HashSet<string>();
    /// <summary>Снятие замка сохранённой сетки, при котором собрана сетка (лестница перемешивания); null — без снятия.
    /// Записывается вместе с сеткой (<see cref="FemSchemaMeshService.Save"/>).</summary>
    public FemKeptMeshUnlockPlan? Unlock { get; init; }
    /// <summary>Шаг лестницы (0 — без снятия замков, 1 — виновные КонЭ, 2 — все).</summary>
    public int LadderStep { get; init; }
}

/// <summary>
/// «Построить сетку схемы» (CSfea 4г): стержни — <see cref="FemMeshDiscretizer"/>, области своей схемы — снимки Gmsh
/// (сохранённый снимок переиспользуется, если совпал отпечаток входа), затем <see cref="FemPlanarMeshMaterializer"/>.
/// При флажке схемы <see cref="FemSchema.AutoRigidJunctions"/> следы сечений стержней, примыкающих к плитам, вшиваются
/// в сетки областей, а по ним строятся АЖТ (<see cref="FemRigidJunctionMaterializer"/>), записываемые в <see cref="Save"/>.
/// Вход вывода ограничений областей — <see cref="FemPlanarMeshPlan"/>, общий с диалогом области. Области строятся
/// волнами несоприкасающихся (<see cref="FemPlanarMeshPlan.Waves"/>): в волне вывод ограничений и Gmsh (внешние
/// процессы, не больше числа ядер) — параллельно в пуле потоков. Работа с БД — в вызывающем (UI) потоке: после
/// <c>await</c> продолжение возвращается в него.
/// </summary>
public sealed class FemSchemaMeshService(DatabaseService db, GmshSettings gmsh)
{
    /// <summary>Строит сетку схемы и записывает её (если нет ошибок). <paramref name="nodes"/>/<paramref name="members"/> —
    /// конструктивный уровень (сессия редактора или БД).</summary>
    /// <remarks>Фрагмент с сохранённой сеткой родителя: при нарушениях связности — лестница перемешивания
    /// (<see cref="FemKeptMeshLadder"/>); снятие замков в итоге — <see cref="FemSchemaMeshBuildResult.Unlock"/>, пишется
    /// в <see cref="Save"/> вместе с сеткой. <paramref name="canUnlock"/> = false (несохранённая сессия редактора: запись
    /// снятия затёрла бы правки) — лестница не запускается, к ошибкам добавляется просьба сохранить схему.</remarks>
    public async Task<FemSchemaMeshBuildResult> BuildAsync(int schemaId, IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members, double? defaultTargetLengthM, Action<string>? info, CancellationToken cancellationToken,
        bool canUnlock = true)
    {
        var first = await RunAsync(schemaId, nodes, members, defaultTargetLengthM, info, checkOnly: false, cancellationToken);
        if (!FemKeptMeshLadder.Needed(first) || !IsStrict(schemaId)) return first;
        if (!canUnlock)
            return new FemSchemaMeshBuildResult
            {
                Diagnostics = [.. first.Diagnostics, new("fragment_mesh_unlock_unsaved", Loc.S("FemSchemaMeshUnlockUnsaved"), true,
                    [.. first.ImportedMembersAtFault])],
                RegionCount = first.RegionCount, RebuiltRegionCount = first.RebuiltRegionCount,
                ImportedMembersAtFault = first.ImportedMembersAtFault,
            };
        var input = UnlockInput(schemaId, nodes, members);
        // Шаг, которому нечего снимать (пластина без области), — его пояснения добавляются к итогу.
        var skipped = new List<FemValidationDiagnostic>();
        var result = await FemKeptMeshLadder.RunAsync(first, members, async (tags, step, reason) =>
        {
            var plan = FemKeptMeshUnlock.Plan(input, tags);
            if (plan.IsEmpty)
            {
                skipped.AddRange(plan.Diagnostics);
                return null;
            }
            var warning = new FemValidationDiagnostic($"fragment_mesh_unlock_step{step}",
                $"Сохранённая сетка КонЭ {Join(plan.UnlockedMemberTags)} перестраивается (шаг {step} лестницы перемешивания): {reason}",
                false, [.. plan.UnlockedMemberTags]);
            info?.Invoke(warning.Message);
            return await RunAsync(schemaId, nodes, members, defaultTargetLengthM, info, checkOnly: false, cancellationToken,
                plan, step, [warning, .. plan.Diagnostics]);
        });
        var extra = skipped.Where(s => !result.Diagnostics.Any(d => d.Code == s.Code && d.Message == s.Message))
            .DistinctBy(s => (s.Code, s.Message)).ToList();
        if (extra.Count > 0) result.Diagnostics.AddRange(extra);
        return result;
    }

    /// <summary>
    /// Ручная команда «Перестроить сетку КонЭ» (фрагмент с сохранённой сеткой): снимает замок с КонЭ
    /// <paramref name="memberTags"/> тем же планировщиком, что лестница (<see cref="FemKeptMeshUnlock.Plan"/>), и пишет
    /// снятие без новой сетки — сгенерированная сетка остальных КонЭ остаётся, сетка схемы устаревает (её перестроит
    /// сборка или расчёт). Пустой план (снимать нечего) — ничего не записано. <paramref name="nodes"/>/<paramref name="members"/>
    /// — сохранённый конструктивный уровень.
    /// </summary>
    public FemKeptMeshUnlockPlan UnlockKeptMembers(int schemaId, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMember> members,
        IEnumerable<string> memberTags)
    {
        var plan = FemKeptMeshUnlock.Plan(UnlockInput(schemaId, nodes, members), memberTags);
        if (!plan.IsEmpty) db.ApplyFemKeptMeshUnlock(schemaId, plan, null, null);
        return plan;
    }

    /// <summary>Проверка без Gmsh и без записи: актуальны ли снимки областей и совпадает ли сохранённая сетка схемы с
    /// пересобранной. Нарушения связности сохранённой сетки фрагмента — не ошибка: сетка устарела, сборка перемешает.</summary>
    public async Task<FemSchemaMeshBuildResult> CheckAsync(int schemaId, IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members, double? defaultTargetLengthM, CancellationToken cancellationToken)
    {
        var result = await RunAsync(schemaId, nodes, members, defaultTargetLengthM, null, checkOnly: true, cancellationToken);
        if (!FemKeptMeshLadder.Needed(result) || !IsStrict(schemaId)) return result;
        return new FemSchemaMeshBuildResult
        {
            Diagnostics = [new("fragment_mesh_unlock_pending",
                $"Сохранённая сетка КонЭ {Join(result.ImportedMembersAtFault)} несвязна с новой — сборка сетки её перестроит.",
                false, [.. result.ImportedMembersAtFault])],
            RegionCount = result.RegionCount, IsCurrent = false, ImportedMembersAtFault = result.ImportedMembersAtFault,
        };
    }

    /// <summary>Фрагмент с сохранённой сеткой: связность стыков — ошибка, лестница перемешивания разрешена.</summary>
    bool IsStrict(int schemaId) => db.HasKeptMesh(schemaId) && db.GetFemSchemaFragmentSource(schemaId).IsFragment;

    /// <summary>Вход планировщика снятия замков: узлы и КонЭ сборки, сетка, ГУ, тела и нагрузки сеточного уровня из БД.</summary>
    FemKeptMeshUnlockInput UnlockInput(int schemaId, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMember> members) => new(
        schemaId, nodes, members, db.GetFemMeshNodes(schemaId), db.GetFemMeshElements(schemaId), db.GetFemMeshNodeSupports(schemaId),
        db.GetFemSprings(schemaId), db.GetFemRigidBodies(schemaId), db.GetFemElementLoads(schemaId), db.GetFemMeshNodeLoads(schemaId),
        db.GetFragmentRefElements(schemaId).Select(r => r.Tag).ToHashSet(StringComparer.Ordinal));

    static string Join(IEnumerable<string> tags)
    {
        var list = tags.ToList();
        string head = string.Join(", ", list.Take(10).Select(t => $"«{t}»"));
        return list.Count > 10 ? $"{head} и ещё {list.Count - 10}" : head;
    }

    async Task<FemSchemaMeshBuildResult> RunAsync(int schemaId, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMember> members,
        double? defaultTargetLengthM, Action<string>? info, bool checkOnly, CancellationToken cancellationToken,
        FemKeptMeshUnlockPlan? unlock = null, int ladderStep = 0, IReadOnlyList<FemValidationDiagnostic>? unlockDiagnostics = null)
    {
        var diagnostics = new List<FemValidationDiagnostic>(unlockDiagnostics ?? []);
        if (unlock != null)
        {
            nodes = unlock.Nodes;
            members = unlock.Members;
        }
        bool keepImported = db.HasKeptMesh(schemaId);
        IReadOnlyList<FemMeshNode> storedNodes = unlock?.MeshNodes ?? (keepImported ? db.GetFemMeshNodes(schemaId) : []);
        IReadOnlyList<FemElement> storedElements = unlock?.MeshElements ?? (keepImported ? db.GetFemMeshElements(schemaId) : []);
        var (beamNodes, beamElements) = keepImported
            ? FemMeshDiscretizer.DiscretizeKeepingImported(schemaId, nodes, members, defaultTargetLengthM, storedNodes, storedElements)
            : FemMeshDiscretizer.Discretize(schemaId, nodes, members, defaultTargetLengthM);
        // Фрагмент с сохранённой сеткой родителя: несвязный стык с ней — ошибка (в CSfea нет MPC); у импортных схем без
        // фрагмента — предупреждение, как раньше.
        bool strictImported = keepImported && db.GetFemSchemaFragmentSource(schemaId).IsFragment;

        double? plateStep = PlateStep(schemaId);
        var ordered = FemPlanarMeshPlan.OrderedRegions(members, db.GetPlanarRegions(schemaId), plateStep);
        var junctionOptions = JunctionOptions(schemaId, keepImported);
        var outlines = SectionOutlines();
        // Явные сопряжения областей (фрагменты) с независимыми сетками и MPC в схеме не поддерживаются: стыки сетки
        // схемы — только общими узлами.
        foreach (var connection in db.GetPlanarConnections(schemaId).Where(c => c.MeshMode == PlanarConnectionMeshMode.IndependentMpc))
            diagnostics.Add(new("planar_connection_mpc_unsupported",
                string.Format(CultureInfo.CurrentCulture, Loc.S("FemSchemaMeshMpcUnsupported"), connection.Tag), true, [connection.Tag]));
        if (diagnostics.Any(d => d.IsError))
            return new FemSchemaMeshBuildResult { Diagnostics = diagnostics, RegionCount = ordered.Count };
        var thickness = PlateThickness();
        var regionMeshes = new List<FemPlanarRegionMesh>();
        int rebuilt = 0;
        if (ordered.Count > 0)
        {
            var executable = new GmshExecutableResolver().Resolve(gmsh.ExecutablePath);
            var version = await GmshProcessRunner.ReadVersionAsync(executable.Path, TimeSpan.FromSeconds(10), cancellationToken);
            var provenance = new PlanarMeshProvenance(version, GmshPlanarMesher.GeneratorVersion);
            // Префикс начинается с импортной сетки: новые области встраивают её узлы на стыках.
            var (derivationNodes, derivationElements) = DerivationBase(schemaId, nodes, members, keepImported, storedNodes, storedElements);
            var prefixBuilder = new FemPlanarMeshMaterializer.Prefix(schemaId, derivationNodes, derivationElements, nodes);
            // Волны из несоприкасающихся областей: в волне вывод ограничений и Gmsh — параллельно, запись снимков — здесь
            // (одно соединение SQLite), по порядку; префикс волны — все области прошлых волн, по порядку сборки.
            var plan = await Task.Run(() => FemPlanarMeshPlan.Waves(ordered, plateStep), cancellationToken);
            info?.Invoke(string.Format(CultureInfo.CurrentCulture, Loc.S("FemSchemaMeshWaves"), ordered.Count, plan.Waves.Count));
            using var gate = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount));
            // Следы АЖТ не зависят от префикса — все заранее: ранняя область получает точки в вершинах следов поздних.
            var keptPoints = strictImported ? KeptPoints(storedNodes, storedElements) : [];
            var allTraces = ordered.Select(p => junctionOptions != null
                ? AwayFromKept(FemRigidJunctions.Build(p.Region, nodes, members, outlines, junctionOptions), p.Region, keptPoints, plateStep)
                : null).ToArray();
            foreach (var wave in plan.Waves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var prefix = await Task.Run(() => new FemPlanarMeshPrefix(prefixBuilder.Build()), cancellationToken);
                var traces = wave.Select(i => allTraces[i]).ToArray();
                var constraints = new FemPlanarRegionConstraints[wave.Count];
                await Task.Run(() => Parallel.For(0, wave.Count, new ParallelOptions { CancellationToken = cancellationToken }, k =>
                    constraints[k] = FemPlanarMeshPlan.Constraints(schemaId, nodes, members, prefix, ordered[wave[k]].Region,
                        laterRegions: plan.Later(ordered, wave[k]), junctions: traces[k],
                        laterJunctions: LaterJunctions(plan, ordered, wave[k], allTraces))), cancellationToken);

                var settings = new PlanarMeshSettings[wave.Count];
                var headers = new (int Id, string InputFingerprint, bool IsCalculable)?[wave.Count];
                var stale = new List<int>();
                for (int k = 0; k < wave.Count; k++)
                {
                    var (member, region) = ordered[wave[k]];
                    settings[k] = new PlanarMeshSettings(FemPlanarMeshPlan.MeshSize(member, region, plateStep), gmsh.Algorithm, gmsh.ElementMode);
                    var fingerprint = PlanarMeshFingerprint.Compute(region, settings[k], provenance, constraints[k].SourceFingerprint);
                    headers[k] = db.GetLatestPlanarMeshSnapshotHeader(region.Id);
                    if (headers[k] is not { IsCalculable: true } header || header.InputFingerprint != fingerprint) stale.Add(k);
                }
                if (checkOnly && stale.Count > 0)
                {
                    var tag = ordered[wave[stale[0]]].Member.ElemTag;
                    return new FemSchemaMeshBuildResult
                    {
                        Diagnostics = [new("planar_mesh_stale", $"Сетка области «{tag}» устарела.", false, [tag])],
                        RegionCount = ordered.Count, IsCurrent = false,
                    };
                }

                foreach (var k in stale)
                    info?.Invoke(string.Format(CultureInfo.CurrentCulture, Loc.S("FemSchemaMeshRegionBuilding"), ordered[wave[k]].Member.ElemTag,
                        constraints[k].Derived.PointLocusCount, constraints[k].Derived.CurveLocusCount, constraints[k].FreeNodeCount));
                var built = await Task.WhenAll(stale.Select(async k =>
                {
                    await gate.WaitAsync(cancellationToken);
                    try { return await Task.Run(() => BuildRegionMeshAsync(ordered[wave[k]].Region, settings[k], constraints[k], cancellationToken), cancellationToken); }
                    finally { gate.Release(); }
                }));
                var snapshots = new PlanarMeshSnapshot?[wave.Count];
                for (int s = 0; s < stale.Count; s++)
                {
                    db.SavePlanarMeshSnapshot(built[s]);
                    snapshots[stale[s]] = built[s];
                    rebuilt++;
                }
                var failed = stale.Where(k => !snapshots[k]!.IsCalculable).ToList();
                if (failed.Count > 0)
                {
                    foreach (var k in failed)
                    {
                        var tag = ordered[wave[k]].Member.ElemTag;
                        diagnostics.AddRange(snapshots[k]!.Diagnostics.Where(d => d.IsError).Take(10));
                        diagnostics.Add(new("planar_mesh_failed",
                            string.Format(CultureInfo.CurrentCulture, Loc.S("FemSchemaMeshRegionFailed"), tag), true, [tag]));
                    }
                    return new FemSchemaMeshBuildResult { Diagnostics = diagnostics, RegionCount = ordered.Count, RebuiltRegionCount = rebuilt };
                }

                for (int k = 0; k < wave.Count; k++)
                {
                    var (member, region) = ordered[wave[k]];
                    if (traces[k] != null && !checkOnly) diagnostics.AddRange(traces[k]!.Diagnostics);
                    var snapshot = snapshots[k] ?? db.GetPlanarMeshSnapshot(headers[k]!.Value.Id)!;
                    // Толщина — из сечения пластины, иначе собственная толщина КонЭ (КонЭ из импортной сетки: «КЭ без КонЭ»
                    // фрагмента, группы КЭ SCAD/ЛИРЫ без сечения).
                    regionMeshes.Add(new FemPlanarRegionMesh(member, region, snapshot,
                        member.PlateSectionId is int id && thickness.TryGetValue(id, out var h) ? h : member.ThicknessM, traces[k]));
                    prefixBuilder.Add(regionMeshes[^1]);
                }
            }
        }

        var mesh = FemPlanarMeshMaterializer.Materialize(schemaId, beamNodes, beamElements, nodes, regionMeshes, strictImported);
        // Узлы схемы без КЭ с опорой или пружиной (голова сваи фрагмента) — свои узлы сетки, иначе ГУ не попадут в сетку.
        FemIsolatedSupportNodes.Add(mesh.Nodes, nodes, [.. db.GetFemSprings(schemaId), .. unlock?.NodeSprings ?? []], schemaId);
        diagnostics.AddRange(mesh.Diagnostics);
        diagnostics.AddRange(FemTopologyValidator.ValidateMesh(mesh.Nodes, mesh.Elements));
        // Проверка: сетка совпала с сохранённой (теги те же) — тела строятся и сравниваются с записанными, иначе смена
        // маски или вида АЖТ без смены геометрии осталась бы незамеченной.
        bool? current = checkOnly ? IsSameAsStored(schemaId, mesh) : null;
        if (junctionOptions != null && !mesh.HasErrors && current != false)
        {
            var junctions = FemRigidJunctionMaterializer.Build(mesh, regionMeshes, nodes, members, BlockedSlaves(schemaId, nodes, members, mesh, unlock));
            mesh.RigidBodies.AddRange(junctions.Bodies);
            if (!checkOnly)
            {
                diagnostics.AddRange(junctions.Diagnostics);
                if (junctions.Bodies.Count > 0)
                    info?.Invoke(string.Format(CultureInfo.CurrentCulture, Loc.S("FemSchemaMeshRigidJunctions"), junctions.Bodies.Count));
            }
        }
        // Тела на узлах схемы (АЖТ фрагмента «по точкам») — после тел примыканий: их узлы уже заняты.
        // Снятие замка в этой сборке добавляет импортные тела, переведённые «по точкам» (пишутся вместе с сеткой).
        List<FemRigidBody> nodeBodies = unlock is { NodeRigidBodies.Count: > 0 }
            ? [.. db.GetFemNodeRigidBodies(schemaId), .. unlock.NodeRigidBodies]
            : db.GetFemNodeRigidBodies(schemaId);
        if (nodeBodies.Count > 0 && !mesh.HasErrors && current != false)
        {
            var others = StoredRigidBodies(schemaId, unlock)
                .Where(b => b.Origin != RigidJunctionTraceSet.Provenance && b.Origin != FemNodeRigidBodies.Provenance)
                .Concat(mesh.RigidBodies).ToList();
            var byPoints = FemNodeRigidBodies.Materialize(mesh.Nodes, nodes, nodeBodies, BlockedSlaves(schemaId, nodes, members, mesh, unlock), others);
            mesh.RigidBodies.AddRange(byPoints.Bodies);
            if (!checkOnly)
            {
                diagnostics.AddRange(byPoints.Diagnostics);
                info?.Invoke(string.Format(CultureInfo.CurrentCulture, Loc.S("FemSchemaMeshNodeRigidBodies"), byPoints.Bodies.Count, nodeBodies.Count));
            }
        }
        if (current == true && !SameJunctionBodies(schemaId, mesh.RigidBodies))
        {
            current = false;
            diagnostics.Add(new("rigid_junctions_stale", Loc.S("FemSchemaMeshRigidJunctionsStale"), false));
        }
        return new FemSchemaMeshBuildResult
        {
            Mesh = mesh, Diagnostics = diagnostics, RegionCount = ordered.Count, RebuiltRegionCount = rebuilt, IsCurrent = current,
            ImportedMembersAtFault = mesh.ImportedMembersAtFault, Unlock = unlock, LadderStep = ladderStep,
        };
    }

    /// <summary>Тела схемы в БД без удаляемых снятием замка.</summary>
    List<FemRigidBody> StoredRigidBodies(int schemaId, FemKeptMeshUnlockPlan? unlock)
    {
        var bodies = db.GetFemRigidBodies(schemaId);
        if (unlock is null || unlock.RemovedRigidBodies.Count == 0) return bodies;
        var removed = unlock.RemovedRigidBodies.Select(b => b.Id).ToHashSet();
        return bodies.Where(b => !removed.Contains(b.Id)).ToList();
    }

    /// <summary>База префикса волн: стержни КонЭ, разбитые узлами схемы, а у схемы с импортной сеткой — ещё и её узлы и
    /// КЭ (<see cref="FemPlanarMeshPlan.DerivationBeams(int, IReadOnlyList{FemNode}, IReadOnlyList{FemMember}, IReadOnlyList{FemMeshNode}, IReadOnlyList{FemElement})"/>).</summary>
    static (List<FemMeshNode> Nodes, List<FemElement> Elements) DerivationBase(int schemaId, IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members, bool keepImported, IReadOnlyList<FemMeshNode> storedNodes, IReadOnlyList<FemElement> storedElements) =>
        keepImported
            ? FemPlanarMeshPlan.DerivationBeams(schemaId, nodes, members, storedNodes, storedElements)
            : FemPlanarMeshPlan.DerivationBeams(schemaId, nodes, members);

    /// <summary>Ограничения сетки одной области для её диалога — тот же вход, что у сборки схемы: узлы и КонЭ из БД,
    /// пластины ранее идущих областей — по их последним расчётным снимкам.</summary>
    public FemPlanarRegionConstraints RegionConstraints(int schemaId, PlanarRegion target)
    {
        var nodes = db.GetFemNodes(schemaId);
        var members = db.GetFemMembers(schemaId);
        var regions = db.GetPlanarRegions(schemaId);
        var ordered = FemPlanarMeshPlan.OrderedRegions(members, regions, PlateStep(schemaId));
        bool keepImported = db.HasKeptMesh(schemaId);
        var (derivationNodes, derivationElements) = keepImported
            ? DerivationBase(schemaId, nodes, members, true, db.GetFemMeshNodes(schemaId), db.GetFemMeshElements(schemaId))
            : DerivationBase(schemaId, nodes, members, false, [], []);
        var prefix = new FemPlanarMeshMaterializer.Prefix(schemaId, derivationNodes, derivationElements, nodes);
        var plan = FemPlanarMeshPlan.Waves(ordered, PlateStep(schemaId));
        int index = ordered.FindIndex(p => p.Region.Id == target.Id);
        // Префикс — области прошлых волн по порядку сборки (область вне порядка — заблокированная или новая — видит все).
        foreach (var i in plan.Sequence)
        {
            if (index >= 0 && plan.WaveOf[i] >= plan.WaveOf[index]) break;
            var (member, region) = ordered[i];
            if (db.GetLatestPlanarMeshSnapshot(region.Id) is { IsCalculable: true } snapshot)
                prefix.Add(new FemPlanarRegionMesh(member, region, snapshot, null));
        }
        var prefixMesh = prefix.Build();
        var options = JunctionOptions(schemaId, keepImported);
        var outlines = SectionOutlines();
        double? step = PlateStep(schemaId);
        var keptPoints = keepImported && db.GetFemSchemaFragmentSource(schemaId).IsFragment
            ? KeptPoints(db.GetFemMeshNodes(schemaId), db.GetFemMeshElements(schemaId)) : [];
        var traces = options != null ? AwayFromKept(FemRigidJunctions.Build(target, nodes, members, outlines, options), target, keptPoints, step) : null;
        // Следы — только касающихся поздних областей: другие в вывод не входят.
        var allTraces = new RigidJunctionTraceSet?[ordered.Count];
        if (index >= 0 && options != null)
            foreach (var j in plan.Neighbours[index].Where(j => plan.WaveOf[j] > plan.WaveOf[index]))
                allTraces[j] = AwayFromKept(FemRigidJunctions.Build(ordered[j].Region, nodes, members, outlines, options),
                    ordered[j].Region, keptPoints, step);
        return FemPlanarMeshPlan.Constraints(schemaId, nodes, members, prefixMesh, target,
            laterRegions: index >= 0 ? plan.Later(ordered, index) : null, junctions: traces,
            laterJunctions: index >= 0 ? LaterJunctions(plan, ordered, index, allTraces) : null);
    }

    /// <summary>Узлы сохранённой (импортной) сетки — точки стыков, на которых следы АЖТ не ставят своих вершин. Только узлы
    /// оставшихся КЭ сохранённой сетки: после снятия замка с плиты её узлы под сохранёнными стержнями остаются помеченными
    /// импортными, и следы АЖТ колонн обрезались бы от стыка, которого уже нет (а проверка сетки, где этих узлов нет,
    /// считала бы её устаревшей).</summary>
    static List<PlanarVector3> KeptPoints(IReadOnlyList<FemMeshNode> stored, IReadOnlyList<FemElement> elements)
    {
        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in elements)
        {
            if (e.Origin != FemMember.MeshSourceImported) continue;
            int[]? ids;
            try { ids = System.Text.Json.JsonSerializer.Deserialize<int[]>(e.NodeIdsJson); }
            catch (System.Text.Json.JsonException) { continue; }
            foreach (var id in ids ?? []) kept.Add(id.ToString(CultureInfo.InvariantCulture));
        }
        return stored.Where(n => n.Origin == FemMember.MeshSourceImported && kept.Contains(n.NodeTag))
            .Select(n => new PlanarVector3(n.X, n.Y, n.Z)).ToList();
    }

    /// <summary>Следы АЖТ области — обрезанные в полшага сетки от стыков с сохранённой сеткой
    /// (<see cref="FemRigidJunctionSeams.AwayFromKeptSeams"/>).</summary>
    static RigidJunctionTraceSet AwayFromKept(RigidJunctionTraceSet set, PlanarRegion region, IReadOnlyList<PlanarVector3> kept,
        double? plateStep) =>
        kept.Count == 0 ? set : FemRigidJunctionSeams.AwayFromKeptSeams(set, region, kept, 0.5 * (plateStep ?? region.MeshMaxElementSizeM));

    /// <summary>Следы АЖТ касающихся областей, которые строятся позже <paramref name="index"/>-й, по тегу КонЭ.</summary>
    static Dictionary<string, RigidJunctionTraceSet?> LaterJunctions(FemPlanarMeshWaves plan,
        IReadOnlyList<(FemMember Member, PlanarRegion Region)> ordered, int index, RigidJunctionTraceSet?[] traces) =>
        plan.Neighbours[index].Where(j => plan.WaveOf[j] > plan.WaveOf[index])
            .ToDictionary(j => ordered[j].Member.ElemTag, j => traces[j], StringComparer.Ordinal);

    /// <summary>Параметры следов АЖТ схемы; null — флажок выключен или у схемы импортная сетка (свои АЖТ источника).
    /// У фрагмента с сохранённой сеткой родителя следы строятся: только у перестраиваемых областей (сохранённые не входят
    /// в <see cref="FemPlanarMeshPlan.OrderedRegions"/>), АЖТ сохранённых — из родителя по тегам.</summary>
    RigidJunctionOptions? JunctionOptions(int schemaId, bool keepImported)
    {
        var (auto, angle) = db.GetFemSchemaRigidJunctions(schemaId);
        return auto && (!keepImported || db.GetFemSchemaFragmentSource(schemaId).IsFragment) ? new RigidJunctionOptions(angle) : null;
    }

    /// <summary>Контуры сечений стержней для следов АЖТ по Id сечения (с кэшем на одну сборку).</summary>
    Func<int, IReadOnlyList<(double Y, double Z)>?> SectionOutlines() => db.SectionOutlines();

    /// <summary>Узлы сетки, которые не становятся ведомыми АЖТ примыканий: закрепления (в том числе линейных связей),
    /// пружины, ручные и импортные тела.</summary>
    HashSet<string> BlockedSlaves(int schemaId, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMember> members,
        FemPlanarMaterializationResult mesh, FemKeptMeshUnlockPlan? unlock = null)
    {
        var line = db.ResolveFemLineSupports(schemaId, members, mesh.Nodes, mesh.Elements);
        var meshSupports = db.GetFemMeshNodeSupports(schemaId);
        var allSprings = db.GetFemSprings(schemaId);
        // Снятие замка: ГУ удаляемых узлов сетки уже перенесены в узлы схемы (их маски — в nodes), пружины — новые.
        if (unlock != null)
        {
            meshSupports = meshSupports.Where(s => !unlock.RemovedNodeTags.Contains(s.NodeTag)).ToList();
            allSprings = [.. allSprings.Where(s => s.TargetKind != FemSpringTargetKinds.MeshNode || !unlock.RemovedNodeTags.Contains(s.NodeTag)),
                .. unlock.NodeSprings];
        }
        var (supports, springs) = line.AppendTo(meshSupports, allSprings);
        return FemRigidJunctionMaterializer.BlockedNodes(nodes, mesh.Nodes, supports, springs, StoredRigidBodies(schemaId, unlock));
    }

    /// <summary>Общий шаг пластин схемы, м (null — не задан).</summary>
    public double? PlateStep(int schemaId) => db.GetFemSchemaMeshSteps(schemaId).Plate;

    /// <summary>Размер КЭ области в сборке схемы: локальный шаг КонЭ, общий шаг пластин, размер из области.</summary>
    public double PlateMeshSize(int schemaId, FemMember member, PlanarRegion region) =>
        FemPlanarMeshPlan.MeshSize(member, region, PlateStep(schemaId));

    /// <summary>Записывает сетку схемы (импортные строки сохраняются) и заменяет автоматические АЖТ примыканий
    /// (<see cref="RigidJunctionTraceSet.Provenance"/>) и тела по узлам схемы (<see cref="FemNodeRigidBodies.Provenance"/>)
    /// телами сборки — пустой список удаляет прежние; ручные и импортные тела не трогаются.</summary>
    /// <remarks>Со снятием замка (<paramref name="unlock"/>, лестница перемешивания) — замки, ГУ, нагрузки и удаление
    /// снятой сохранённой сетки пишутся одной транзакцией с сеткой (<see cref="DatabaseService.ApplyFemKeptMeshUnlock"/>).</remarks>
    public void Save(int schemaId, FemPlanarMaterializationResult mesh, FemKeptMeshUnlockPlan? unlock = null)
    {
        if (unlock is { IsEmpty: false })
            db.ApplyFemKeptMeshUnlock(schemaId, unlock, mesh.Nodes, mesh.Elements);
        else if (db.HasKeptMesh(schemaId))
            db.SaveFemMeshSnapshotKeepingImported(schemaId, mesh.Nodes, mesh.Elements);
        else
            db.SaveFemMeshSnapshot(schemaId, mesh.Nodes, mesh.Elements);
        foreach (var origin in BuiltOrigins)
            db.SaveFemBoundary(schemaId, origin, [], [], mesh.RigidBodies.Where(b => b.Origin == origin).ToList());
    }

    /// <summary>Происхождения тел, которые строит сборка сетки.</summary>
    static readonly string[] BuiltOrigins = [RigidJunctionTraceSet.Provenance, FemNodeRigidBodies.Provenance];

    /// <summary>Сетка схемы изменилась: результаты постановок сбрасываются, наборы усилий схемы (номера КЭ прежней
    /// сетки) перечисляются в журнале — не удаляются.</summary>
    public void ReportMeshChanged(FemSchema schema, ILogService log)
    {
        int invalidated = 0;
        foreach (var analysis in schema.Analyses.Where(a => a.ResultId != null))
        {
            analysis.InvalidateResult();
            db.SaveFemAnalysis(analysis);
            invalidated++;
        }
        if (invalidated > 0) log.Info(string.Format(Loc.S("FemSchemaMeshAnalysesInvalidated"), invalidated));
        var stale = db.ForceSets.Where(f => f.SourceType == "fea" && f.SourceSchemaId == schema.Id).Select(f => f.Tag).ToList();
        if (stale.Count > 0) log.Warning(string.Format(Loc.S("FemSchemaMeshForceSetsStale"), string.Join(", ", stale)));
    }

    async Task<PlanarMeshSnapshot> BuildRegionMeshAsync(PlanarRegion region, PlanarMeshSettings settings,
        FemPlanarRegionConstraints constraints, CancellationToken cancellationToken)
    {
        var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
        {
            ExecutablePath = gmsh.ExecutablePath,
            ArtifactRoot = gmsh.ResolveArtifactsPath(),
            Timeout = TimeSpan.FromSeconds(gmsh.TimeoutSeconds),
        });
        var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(region, settings, constraints.Constraints,
            constraints.SourceFingerprint, constraints.Diagnostics), cancellationToken);
        if (snapshot.IsCalculable && !gmsh.KeepArtifacts && snapshot.Provenance?.ArtifactDirectory is { } dir && Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return snapshot;
    }

    Dictionary<int, double> PlateThickness()
    {
        var result = new Dictionary<int, double>();
        foreach (var section in db.PlateSections) result.TryAdd(section.Id, section.H);
        return result;
    }

    /// <summary>Тела сборки в БД (авто-АЖТ примыканий и по узлам схемы) совпадают с телами сборки: происхождение,
    /// ведущий, ведомые, маска, вид.</summary>
    bool SameJunctionBodies(int schemaId, IEnumerable<FemRigidBody> built)
    {
        static List<string> Keys(IEnumerable<FemRigidBody> bodies) => bodies
            .Select(b => $"{b.Origin}|{b.MasterNodeTag}|{string.Join(",", b.SlaveNodeTags.Order(StringComparer.Ordinal))}|{b.Mask}|{b.Kind}")
            .Order(StringComparer.Ordinal).ToList();
        var stored = db.GetFemRigidBodies(schemaId).Where(b => BuiltOrigins.Contains(b.Origin));
        return Keys(stored).SequenceEqual(Keys(built));
    }

    /// <summary>Сохранённая сгенерированная сетка схемы совпадает с пересобранной (теги, координаты, связность, свойства).</summary>
    public bool IsSameAsStored(int schemaId, FemPlanarMaterializationResult mesh) =>
        Hash(db.GetFemMeshNodes(schemaId), db.GetFemMeshElements(schemaId)) == Hash(mesh.Nodes, mesh.Elements);

    /// <summary>Отличается ли сборка от записанной: сетка или авто-АЖТ примыканий. Смена одних тел (маска, вид, режим
    /// стержня при той же сетке) тоже меняет расчёт — результаты постановок нужно сбросить.</summary>
    public bool DiffersFromStored(int schemaId, FemPlanarMaterializationResult mesh) =>
        !IsSameAsStored(schemaId, mesh) || !SameJunctionBodies(schemaId, mesh.RigidBodies);

    static string Hash(IEnumerable<FemMeshNode> nodes, IEnumerable<FemElement> elements)
    {
        var text = new StringBuilder();
        foreach (var n in nodes.Where(n => n.Origin != FemMember.MeshSourceImported).OrderBy(n => n.NodeTag, StringComparer.Ordinal))
            text.Append(CultureInfo.InvariantCulture, $"n|{n.NodeTag}|{n.X:G15}|{n.Y:G15}|{n.Z:G15}|{n.SourceNodeTag}|{n.SourceMemberTag}\n");
        foreach (var e in elements.Where(e => e.Origin != FemMember.MeshSourceImported).OrderBy(e => e.ElemTag, StringComparer.Ordinal))
            text.Append(CultureInfo.InvariantCulture,
                $"e|{e.ElemTag}|{e.ElemType}|{e.NodeIdsJson}|{e.SourceMemberTag}|{e.CrossSectionId}|{e.ThicknessM:G15}|{e.GjStrategy}|{e.GjManualValue:G15}|{e.GjTorsionTaskId}\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
