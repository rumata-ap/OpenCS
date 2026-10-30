using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CScore;
using CScore.Fem;
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
    public Task<FemSchemaMeshBuildResult> BuildAsync(int schemaId, IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members, double? defaultTargetLengthM, Action<string>? info, CancellationToken cancellationToken) =>
        RunAsync(schemaId, nodes, members, defaultTargetLengthM, info, checkOnly: false, cancellationToken);

    /// <summary>Проверка без Gmsh и без записи: актуальны ли снимки областей и совпадает ли сохранённая сетка схемы с
    /// пересобранной.</summary>
    public Task<FemSchemaMeshBuildResult> CheckAsync(int schemaId, IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members, double? defaultTargetLengthM, CancellationToken cancellationToken) =>
        RunAsync(schemaId, nodes, members, defaultTargetLengthM, null, checkOnly: true, cancellationToken);

    async Task<FemSchemaMeshBuildResult> RunAsync(int schemaId, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMember> members,
        double? defaultTargetLengthM, Action<string>? info, bool checkOnly, CancellationToken cancellationToken)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        bool keepImported = db.HasImportedMesh(schemaId);
        var (beamNodes, beamElements) = keepImported
            ? FemMeshDiscretizer.DiscretizeKeepingImported(schemaId, nodes, members, defaultTargetLengthM,
                db.GetFemMeshNodes(schemaId), db.GetFemMeshElements(schemaId))
            : FemMeshDiscretizer.Discretize(schemaId, nodes, members, defaultTargetLengthM);

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
            var (derivationNodes, derivationElements) = FemPlanarMeshPlan.DerivationBeams(schemaId, nodes, members);
            var prefixBuilder = new FemPlanarMeshMaterializer.Prefix(schemaId, derivationNodes, derivationElements, nodes);
            // Волны из несоприкасающихся областей: в волне вывод ограничений и Gmsh — параллельно, запись снимков — здесь
            // (одно соединение SQLite), по порядку; префикс волны — все области прошлых волн, по порядку сборки.
            var plan = await Task.Run(() => FemPlanarMeshPlan.Waves(ordered, plateStep), cancellationToken);
            info?.Invoke(string.Format(CultureInfo.CurrentCulture, Loc.S("FemSchemaMeshWaves"), ordered.Count, plan.Waves.Count));
            using var gate = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount));
            foreach (var wave in plan.Waves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var prefix = await Task.Run(() => new FemPlanarMeshPrefix(prefixBuilder.Build()), cancellationToken);
                var traces = wave.Select(i => junctionOptions != null
                    ? FemRigidJunctions.Build(ordered[i].Region, nodes, members, outlines, junctionOptions) : null).ToArray();
                var constraints = new FemPlanarRegionConstraints[wave.Count];
                await Task.Run(() => Parallel.For(0, wave.Count, new ParallelOptions { CancellationToken = cancellationToken }, k =>
                    constraints[k] = FemPlanarMeshPlan.Constraints(schemaId, nodes, members, prefix, ordered[wave[k]].Region,
                        laterRegions: plan.Later(ordered, wave[k]), junctions: traces[k])), cancellationToken);

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

        var mesh = FemPlanarMeshMaterializer.Materialize(schemaId, beamNodes, beamElements, nodes, regionMeshes);
        diagnostics.AddRange(mesh.Diagnostics);
        diagnostics.AddRange(FemTopologyValidator.ValidateMesh(mesh.Nodes, mesh.Elements));
        // Проверка: сетка совпала с сохранённой (теги те же) — тела строятся и сравниваются с записанными, иначе смена
        // маски или вида АЖТ без смены геометрии осталась бы незамеченной.
        bool? current = checkOnly ? IsSameAsStored(schemaId, mesh) : null;
        if (junctionOptions != null && !mesh.HasErrors && current != false)
        {
            var junctions = FemRigidJunctionMaterializer.Build(mesh, regionMeshes, nodes, members, BlockedSlaves(schemaId, nodes, members, mesh));
            mesh.RigidBodies.AddRange(junctions.Bodies);
            if (!checkOnly)
            {
                diagnostics.AddRange(junctions.Diagnostics);
                if (junctions.Bodies.Count > 0)
                    info?.Invoke(string.Format(CultureInfo.CurrentCulture, Loc.S("FemSchemaMeshRigidJunctions"), junctions.Bodies.Count));
            }
        }
        // Тела на узлах схемы (АЖТ фрагмента «по точкам») — после тел примыканий: их узлы уже заняты.
        var nodeBodies = db.GetFemNodeRigidBodies(schemaId);
        if (nodeBodies.Count > 0 && !mesh.HasErrors && current != false)
        {
            var others = db.GetFemRigidBodies(schemaId)
                .Where(b => b.Origin != RigidJunctionTraceSet.Provenance && b.Origin != FemNodeRigidBodies.Provenance)
                .Concat(mesh.RigidBodies).ToList();
            var byPoints = FemNodeRigidBodies.Materialize(mesh.Nodes, nodes, nodeBodies, BlockedSlaves(schemaId, nodes, members, mesh), others);
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
        };
    }

    /// <summary>Ограничения сетки одной области для её диалога — тот же вход, что у сборки схемы: узлы и КонЭ из БД,
    /// пластины ранее идущих областей — по их последним расчётным снимкам.</summary>
    public FemPlanarRegionConstraints RegionConstraints(int schemaId, PlanarRegion target)
    {
        var nodes = db.GetFemNodes(schemaId);
        var members = db.GetFemMembers(schemaId);
        var regions = db.GetPlanarRegions(schemaId);
        var ordered = FemPlanarMeshPlan.OrderedRegions(members, regions, PlateStep(schemaId));
        var (derivationNodes, derivationElements) = FemPlanarMeshPlan.DerivationBeams(schemaId, nodes, members);
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
        var options = JunctionOptions(schemaId, db.HasImportedMesh(schemaId));
        var traces = options != null ? FemRigidJunctions.Build(target, nodes, members, SectionOutlines(), options) : null;
        return FemPlanarMeshPlan.Constraints(schemaId, nodes, members, prefixMesh, target,
            laterRegions: index >= 0 ? plan.Later(ordered, index) : null, junctions: traces);
    }

    /// <summary>Параметры следов АЖТ схемы; null — флажок выключен или у схемы импортная сетка (свои АЖТ источника).</summary>
    RigidJunctionOptions? JunctionOptions(int schemaId, bool keepImported)
    {
        var (auto, angle) = db.GetFemSchemaRigidJunctions(schemaId);
        return auto && !keepImported ? new RigidJunctionOptions(angle) : null;
    }

    /// <summary>Контуры сечений стержней для следов АЖТ по Id сечения (с кэшем на одну сборку).</summary>
    Func<int, IReadOnlyList<(double Y, double Z)>?> SectionOutlines()
    {
        var cache = new Dictionary<int, IReadOnlyList<(double Y, double Z)>?>();
        return id =>
        {
            if (!cache.TryGetValue(id, out var outline))
                cache[id] = outline = db.CrossSections.FirstOrDefault(s => s.Id == id) is { } section
                    ? RigidJunctionSectionOutline.From(section) : null;
            return outline;
        };
    }

    /// <summary>Узлы сетки, которые не становятся ведомыми АЖТ примыканий: закрепления (в том числе линейных связей),
    /// пружины, ручные и импортные тела.</summary>
    HashSet<string> BlockedSlaves(int schemaId, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMember> members,
        FemPlanarMaterializationResult mesh)
    {
        var line = db.ResolveFemLineSupports(schemaId, members, mesh.Nodes, mesh.Elements);
        var (supports, springs) = line.AppendTo(db.GetFemMeshNodeSupports(schemaId), db.GetFemSprings(schemaId));
        return FemRigidJunctionMaterializer.BlockedNodes(nodes, mesh.Nodes, supports, springs, db.GetFemRigidBodies(schemaId));
    }

    /// <summary>Общий шаг пластин схемы, м (null — не задан).</summary>
    public double? PlateStep(int schemaId) => db.GetFemSchemaMeshSteps(schemaId).Plate;

    /// <summary>Размер КЭ области в сборке схемы: локальный шаг КонЭ, общий шаг пластин, размер из области.</summary>
    public double PlateMeshSize(int schemaId, FemMember member, PlanarRegion region) =>
        FemPlanarMeshPlan.MeshSize(member, region, PlateStep(schemaId));

    /// <summary>Записывает сетку схемы (импортные строки сохраняются) и заменяет автоматические АЖТ примыканий
    /// (<see cref="RigidJunctionTraceSet.Provenance"/>) и тела по узлам схемы (<see cref="FemNodeRigidBodies.Provenance"/>)
    /// телами сборки — пустой список удаляет прежние; ручные и импортные тела не трогаются.</summary>
    public void Save(int schemaId, FemPlanarMaterializationResult mesh)
    {
        if (db.HasImportedMesh(schemaId))
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
