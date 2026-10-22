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
/// Вход вывода ограничений областей — <see cref="FemPlanarMeshPlan"/>, общий с диалогом области. Работа с БД — в
/// вызывающем (UI) потоке: после <c>await</c> продолжение возвращается в него; Gmsh — внешний процесс.
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
            for (int k = 0; k < ordered.Count; k++)
            {
                var (member, region) = ordered[k];
                cancellationToken.ThrowIfCancellationRequested();
                var prefix = FemPlanarMeshMaterializer.Materialize(schemaId, derivationNodes, derivationElements, nodes, regionMeshes);
                var traces = junctionOptions != null ? FemRigidJunctions.Build(region, nodes, members, outlines, junctionOptions) : null;
                if (traces != null && !checkOnly) diagnostics.AddRange(traces.Diagnostics);
                var constraints = FemPlanarMeshPlan.Constraints(schemaId, nodes, members, prefix, region,
                    laterRegions: Later(ordered, k), junctions: traces);
                var settings = new PlanarMeshSettings(FemPlanarMeshPlan.MeshSize(member, region, plateStep), gmsh.Algorithm, gmsh.ElementMode);
                var fingerprint = PlanarMeshFingerprint.Compute(region, settings, provenance, constraints.SourceFingerprint);
                var snapshot = db.GetPlanarMeshSnapshots(region.Id).LastOrDefault();
                if (snapshot is not { IsCalculable: true } || snapshot.InputFingerprint != fingerprint)
                {
                    if (checkOnly)
                        return new FemSchemaMeshBuildResult
                        {
                            Diagnostics = [new("planar_mesh_stale", $"Сетка области «{member.ElemTag}» устарела.", false, [member.ElemTag])],
                            RegionCount = ordered.Count, IsCurrent = false,
                        };
                    info?.Invoke(string.Format(CultureInfo.CurrentCulture, Loc.S("FemSchemaMeshRegionBuilding"), member.ElemTag,
                        constraints.Derived.PointLocusCount, constraints.Derived.CurveLocusCount, constraints.FreeNodeCount));
                    snapshot = await BuildRegionMeshAsync(region, settings, constraints, cancellationToken);
                    db.SavePlanarMeshSnapshot(snapshot);
                    rebuilt++;
                    if (!snapshot.IsCalculable)
                    {
                        diagnostics.AddRange(snapshot.Diagnostics.Where(d => d.IsError).Take(10));
                        diagnostics.Add(new("planar_mesh_failed",
                            string.Format(CultureInfo.CurrentCulture, Loc.S("FemSchemaMeshRegionFailed"), member.ElemTag), true, [member.ElemTag]));
                        return new FemSchemaMeshBuildResult { Diagnostics = diagnostics, RegionCount = ordered.Count, RebuiltRegionCount = rebuilt };
                    }
                }
                regionMeshes.Add(new FemPlanarRegionMesh(member, region, snapshot,
                    member.PlateSectionId is int id && thickness.TryGetValue(id, out var h) ? h : null, traces));
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
        var prefix = new List<FemPlanarRegionMesh>();
        int index = ordered.FindIndex(p => p.Region.Id == target.Id);
        foreach (var (member, region) in ordered)
        {
            if (region.Id == target.Id) break;
            if (db.GetPlanarMeshSnapshots(region.Id).LastOrDefault() is { IsCalculable: true } snapshot)
                prefix.Add(new FemPlanarRegionMesh(member, region, snapshot, null));
        }
        var prefixMesh = FemPlanarMeshMaterializer.Materialize(schemaId, derivationNodes, derivationElements, nodes, prefix);
        var options = JunctionOptions(schemaId, db.HasImportedMesh(schemaId));
        var traces = options != null ? FemRigidJunctions.Build(target, nodes, members, SectionOutlines(), options) : null;
        return FemPlanarMeshPlan.Constraints(schemaId, nodes, members, prefixMesh, target,
            laterRegions: index >= 0 ? Later(ordered, index) : null, junctions: traces);
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

    /// <summary>Области, которые строятся после k-й: их линии стыка с ней — по геометрии.</summary>
    static List<(string Tag, PlanarRegion Region)> Later(List<(FemMember Member, PlanarRegion Region)> ordered, int k) =>
        ordered.Skip(k + 1).Select(p => (p.Member.ElemTag, p.Region)).ToList();

    /// <summary>Записывает сетку схемы (импортные строки сохраняются) и заменяет автоматические АЖТ примыканий
    /// (<see cref="RigidJunctionTraceSet.Provenance"/>) телами сборки — пустой список удаляет прежние; ручные и
    /// импортные тела не трогаются.</summary>
    public void Save(int schemaId, FemPlanarMaterializationResult mesh)
    {
        if (db.HasImportedMesh(schemaId))
            db.SaveFemMeshSnapshotKeepingImported(schemaId, mesh.Nodes, mesh.Elements);
        else
            db.SaveFemMeshSnapshot(schemaId, mesh.Nodes, mesh.Elements);
        db.SaveFemBoundary(schemaId, RigidJunctionTraceSet.Provenance, [], [], mesh.RigidBodies);
    }

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

    /// <summary>Авто-АЖТ примыканий в БД совпадают с телами сборки: ведущий, ведомые, маска, вид.</summary>
    bool SameJunctionBodies(int schemaId, IEnumerable<FemRigidBody> built)
    {
        static List<string> Keys(IEnumerable<FemRigidBody> bodies) => bodies
            .Select(b => $"{b.MasterNodeTag}|{string.Join(",", b.SlaveNodeTags.Order(StringComparer.Ordinal))}|{b.Mask}|{b.Kind}")
            .Order(StringComparer.Ordinal).ToList();
        var stored = db.GetFemRigidBodies(schemaId).Where(b => b.Origin == RigidJunctionTraceSet.Provenance);
        return Keys(stored).SequenceEqual(Keys(built));
    }

    /// <summary>Сохранённая сгенерированная сетка схемы совпадает с пересобранной (теги, координаты, связность, свойства).</summary>
    public bool IsSameAsStored(int schemaId, FemPlanarMaterializationResult mesh) =>
        Hash(db.GetFemMeshNodes(schemaId), db.GetFemMeshElements(schemaId)) == Hash(mesh.Nodes, mesh.Elements);

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
