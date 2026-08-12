using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using OpenCS.Gmsh;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

public sealed record ShellMeshPatchBuildResult(
    bool IsCalculable,
    ShellMeshPatchPlateSectionResponse? Source,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>RVE-гомогенизация IPlateSectionResponse через реальный OpenSees.exe — см. спеку.
/// Синхронный Forces()/Tangent() блокирует через GetAwaiter().GetResult() на КАЖДОМ вызове
/// (каждый вызов — новый процесс OpenSees.exe, в отличие от CSfea, где решённая система
/// переиспользуется). Предназначено ТОЛЬКО для request-local/тестового контекста без
/// захваченного SynchronizationContext (WPF/UI — вне объёма). ОДИН И ТОТ ЖЕ CancellationToken
/// передаётся в КАЖДЫЙ внутренний вызов RunAsync (до 13 за один Tangent()) — не расходуется
/// первым вызовом.</summary>
public sealed class ShellMeshPatchPlateSectionResponse : IPlateSectionResponse
{
    readonly ShellOpenSeesModel _baseModel;
    readonly IReadOnlyDictionary<int, int> _nodeIndexToTag;
    readonly IReadOnlyList<int> _boundaryNodeIndices;
    readonly IReadOnlyList<PlanarMeshNode> _nodes;
    readonly double _centerU;
    readonly double _centerV;
    readonly IShellAnalysisRunner _runner;
    readonly string _executablePath;
    readonly CancellationToken _cancellationToken;
    readonly ShellMeshPatchStateBounds _bounds;
    readonly double[,] _as;

    ShellMeshPatchPlateSectionResponse(
        ShellOpenSeesModel baseModel, IReadOnlyDictionary<int, int> nodeIndexToTag,
        IReadOnlyList<int> boundaryNodeIndices, IReadOnlyList<PlanarMeshNode> nodes,
        double centerU, double centerV, IShellAnalysisRunner runner, string executablePath,
        CancellationToken cancellationToken, ShellMeshPatchStateBounds bounds, double[,] asBlock, string fingerprint)
    {
        _baseModel = baseModel;
        _nodeIndexToTag = nodeIndexToTag;
        _boundaryNodeIndices = boundaryNodeIndices;
        _nodes = nodes;
        _centerU = centerU;
        _centerV = centerV;
        _runner = runner;
        _executablePath = executablePath;
        _cancellationToken = cancellationToken;
        _bounds = bounds;
        _as = asBlock;
        Fingerprint = fingerprint;
    }

    public EquivalentSectionSourceKind SourceKind => EquivalentSectionSourceKind.ShellMeshOpenSees;
    public string Fingerprint { get; }

    public PlateResultants Forces(ShellStrainState state)
    {
        _bounds.Validate(state);
        return ComputeForces(state);
    }

    public PlateShellTangentResult Tangent(ShellStrainState state)
    {
        _bounds.Validate(state); // один раз — на реальный аргумент вызова, не на пробы ниже.
        const double hNominal = 1e-6;
        double[,] a = new double[3, 3], b = new double[3, 3], d = new double[3, 3];
        var baseForces = ComputeForces(state);
        // Знаковое соглашение — см. тот же комментарий в CSfea-версии (Task 8). ComputeForces()
        // (не Forces()) используется намеренно — проба ±h у границы bounds не должна бросать
        // ArgumentOutOfRangeException. Шаг h дополнительно ограничен расстоянием до границы
        // bounds на каждой компоненте — иначе проба у самой границы вышла бы за заявленный
        // рабочий диапазон линейности.
        var values = state.ToArray();
        for (int k = 0; k < 6; k++)
        {
            double bound = k < 3 ? _bounds.EpsGammaBoundAbs : _bounds.KappaBoundAbs;
            double h = Math.Min(hNominal, Math.Max(bound - Math.Abs(values[k]), bound * 1e-9));
            var plus = Perturb(state, k, h);
            var minus = Perturb(state, k, -h);
            double[] fPlus = ComputeForces(plus).ToArray();
            double[] fMinus = ComputeForces(minus).ToArray();
            for (int row = 0; row < 3; row++)
            {
                double dN = (fPlus[row] - fMinus[row]) / (2 * h);
                double dM = (fPlus[row + 3] - fMinus[row + 3]) / (2 * h);
                if (k < 3) { a[row, k] = dN; b[k, row] = dM; }
                else { b[row, k - 3] = dN; d[row, k - 3] = dM; }
            }
        }
        return new PlateShellTangentResult
        {
            Nx = baseForces.Nx, Ny = baseForces.Ny, Nxy = baseForces.Nxy,
            Mx = baseForces.Mx, My = baseForces.My, Mxy = baseForces.Mxy,
            A = a, B = b, D = d, As = _as, // material-derived, не продукт RVE — см. фабрику ниже.
        };
    }

    PlateResultants ComputeForces(ShellStrainState state)
    {
        Structural.ShellResult result = SolveForState(state);
        return AverageSectionResultants(result);
    }

    static ShellStrainState Perturb(ShellStrainState state, int component, double delta)
    {
        double[] v = state.ToArray();
        v[component] += delta;
        return ShellStrainState.FromArray(v);
    }

    Structural.ShellResult SolveForState(ShellStrainState state)
    {
        // Весь периметр патча несёт полное KUBC (5 из 6 DOF на каждом граничном узле) — этого
        // достаточно, чтобы устранить свободу жёсткого тела без anchor-узла. Drilling (DOF 6)
        // остаётся свободным везде, стабилизация — штатная DrillingPolicy модели, как во всех
        // остальных shell-моделях проекта.
        var kinematicLoads = new List<ShellKinematicLoad>();
        foreach (int nodeIndex in _boundaryNodeIndices)
        {
            var node = _nodes[nodeIndex];
            int tag = _nodeIndexToTag[nodeIndex];
            var field = RvePatchKinematics.NodeField(state, _centerU, _centerV, node.U, node.V);
            kinematicLoads.Add(new ShellKinematicLoad(tag, 1, field.U));
            kinematicLoads.Add(new ShellKinematicLoad(tag, 2, field.V));
            kinematicLoads.Add(new ShellKinematicLoad(tag, 3, field.W));
            kinematicLoads.Add(new ShellKinematicLoad(tag, 4, field.ThetaX));
            kinematicLoads.Add(new ShellKinematicLoad(tag, 5, field.ThetaY));
        }

        var model = _baseModel with
        {
            Stages = [new ShellNonlinearStage
            {
                Tag = "rve-kubc",
                Loads = [],
                KinematicLoads = kinematicLoads,
                LoadFactorStep = 1.0,
                MaxLoadFactor = 1.0,
            }],
        };
        model.Validate();

        ShellAnalysisRunResult run = _runner.RunAsync(model, _executablePath, _cancellationToken)
            .GetAwaiter().GetResult();

        if (run.Outcome != ShellAnalysisOutcome.Completed || run.Result is null)
            throw new InvalidOperationException(
                $"shell_mesh_patch_solver_failed: OpenSees не завершил расчёт успешно (Outcome={run.Outcome}, {run.ErrorMessage}).");

        // Проверяем именно ПОСЛЕДНЮЮ запись Steps (не последний сошедшийся шаг) —
        // последовательность «сошёлся целевой шаг, затем неудачная запись» не должна
        // считаться успехом.
        var steps = run.Result.Steps;
        if (steps.Count == 0 || !steps[^1].Converged || Math.Abs(steps[^1].LoadFactor - 1.0) > 1e-9)
            throw new InvalidOperationException(
                "shell_mesh_patch_solver_failed: последняя запись шагов не сошлась или не достигла целевого LoadFactor.");

        return run.Result;
    }

    PlateResultants AverageSectionResultants(Structural.ShellResult result)
    {
        // Равновесные веса area/n_points — документированное приближение (см. спеку, раздел
        // «Area-averaging OpenSees»); RVE-мешинг ограничен Triangles (см. фабрику ниже).
        var byElement = result.SectionResultants.GroupBy(s => s.ElementTag).ToList();
        var elementArea = _baseModel.Elements.ToDictionary(
            e => e.Tag, e => ElementAreaFromNodesUV(e, _nodeIndexToTag, _nodes));

        double sumW = 0, nx = 0, ny = 0, nxy = 0, mx = 0, my = 0, mxy = 0;
        foreach (var group in byElement)
        {
            double area = elementArea[group.Key];
            int count = group.Count();
            double weightEach = area / count;
            foreach (var s in group)
            {
                sumW += weightEach;
                nx += s.Nx * weightEach; ny += s.Ny * weightEach; nxy += s.Nxy * weightEach;
                mx += s.Mx * weightEach; my += s.My * weightEach; mxy += s.Mxy * weightEach;
            }
        }
        if (!(sumW > 0.0))
            throw new InvalidOperationException("Суммарный вес точек интегрирования должен быть положительным.");

        // OpenSees section resultants — СИ (Н/м, Н·м/м); PlateResultants — кН/м, кН·м/м.
        return new PlateResultants(
            nx / sumW / 1000.0, ny / sumW / 1000.0, nxy / sumW / 1000.0,
            mx / sumW / 1000.0, my / sumW / 1000.0, mxy / sumW / 1000.0);
    }

    /// <summary>Площадь элемента по плоским (U, V) координатам патча (PlanarMeshNode), НЕ по
    /// мировым X/Y/Z — precondition этого среза допускает произвольную ориентацию StripFrame
    /// (наклонная/вертикальная плита), при которой shoelace по мировым X/Y даёт неверную или
    /// нулевую площадь. U/V — всегда плоские координаты патча независимо от 3D-ориентации.</summary>
    static double ElementAreaFromNodesUV(
        NormalizedShellElement element, IReadOnlyDictionary<int, int> nodeIndexToTag,
        IReadOnlyList<PlanarMeshNode> nodes)
    {
        var tagToIndex = nodeIndexToTag.ToDictionary(kv => kv.Value, kv => kv.Key);
        var pts = element.NodeTags.Select(t => nodes[tagToIndex[t]]).ToList();
        double area2 = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p0 = pts[i];
            var p1 = pts[(i + 1) % pts.Count];
            area2 += p0.U * p1.V - p1.U * p0.V;
        }
        return Math.Abs(area2) / 2.0;
    }

    /// <summary>Строит RVE-патч (Gmsh + OpenSees.exe) для сечения, возможно неоднородного по
    /// площади патча (зоны армирования). Precondition: stripFrame ориентированно совпадает с
    /// sourceRegion.Frame (RvePatchPreconditions.FrameAligned), ВСЕ резолвленные по элементам
    /// слои Angle=0, весь патч внутри Hull/вне Holes региона. RVE-мешинг всегда Triangles.</summary>
    public static async Task<ShellMeshPatchBuildResult> CreateAsync(
        PlanarRegion sourceRegion, Frame3D stripFrame, double centerU, double centerV,
        PlateRebarField field, PlateSection backgroundSection,
        Diagramm concreteDiagram, Diagramm rebarDiagram,
        IPlateSectionShellMaterialResolver resolver,
        ShellMeshPatchStateBounds bounds, double rveSizeM, PlanarMeshSettings meshSettings,
        GmshPlanarMesher mesher, IShellAnalysisRunner runner, string executablePath,
        CancellationToken cancellationToken, double frameAlignmentTolerance = 1e-6,
        double asConcreteE_MPa = 30000.0, double asNu = 0.2, double asKShear = 5.0 / 6.0,
        double[,]? asOverride = null)
    {
        ArgumentNullException.ThrowIfNull(sourceRegion);
        ArgumentNullException.ThrowIfNull(stripFrame);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(backgroundSection);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(mesher);
        ArgumentNullException.ThrowIfNull(runner);
        if (meshSettings.ElementMode != PlanarMeshElementMode.Triangles)
            throw new ArgumentException("OpenSees RVE-адаптер мешируется только Triangles.", nameof(meshSettings));

        // Precondition: без TensionConcrete=true preflight/As (tensionOverride:true) расходятся
        // с фактической веткой Compute(), выбираемой по backgroundSection.TensionConcrete в
        // остальном коде проекта.
        if (!backgroundSection.TensionConcrete)
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_requires_tension_concrete",
                "RVE-адаптер поддерживает только PlateSection.TensionConcrete=true.")]);

        if (!RvePatchPreconditions.FrameAligned(stripFrame, sourceRegion.Frame, frameAlignmentTolerance))
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_frame_mismatch",
                "StripFrame не совпадает ориентированно с PlanarRegion.Frame.")]);

        var contour = RvePatchKinematics.SquareContourUV(centerU, centerV, rveSizeM);
        var contourU = contour.Select(p => p.U).ToArray();
        var contourV = contour.Select(p => p.V).ToArray();

        if (!PatchInsideRegion(sourceRegion, contourU, contourV))
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_outside_region",
                "RVE-патч выходит за пределы Hull исходного PlanarRegion или пересекает отверстие.")]);

        var patchRegion = PlanarRegion.CreateFromContour(
            new Contour { X = contourU, Y = contourV }, frame: sourceRegion.Frame);

        var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(patchRegion, meshSettings), cancellationToken);
        if (!snapshot.IsCalculable)
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_gmsh_unmeshable",
                $"Gmsh не смог построить сетку RVE-патча: {string.Join("; ", snapshot.Diagnostics.Select(d => d.Message))}")]);

        if (!NodesInsideRegion(sourceRegion, snapshot.Nodes))
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_outside_region",
                "Один или несколько узлов построенной сетки RVE-патча вне Hull/внутри Hole региона.")]);

        var centroids = snapshot.Elements
            .Select(e => (e.Index, Centroid: e.Centroid(snapshot.Nodes)))
            .Select(t => (t.Index, t.Centroid.U, t.Centroid.V))
            .ToList();
        var resolvedPerElement = PlateRebarFieldResolver.ResolveMesh(field, centroids);

        // Диагностики резолвера не должны молча теряться.
        var accumulatedDiagnostics = new List<FemValidationDiagnostic>();
        foreach (var resolved in resolvedPerElement)
            accumulatedDiagnostics.AddRange(resolved.Layout.Diagnostics);
        if (accumulatedDiagnostics.Any(d => d.IsError))
            return new(false, null, accumulatedDiagnostics.Where(d => d.IsError).ToList());

        var allLayers = resolvedPerElement.SelectMany(r => r.Layout.Layers).ToList();
        if (!RvePatchPreconditions.AllRebarAnglesZero(allLayers))
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_angled_rebar_unsupported",
                "RVE-адаптер поддерживает только раскладки с Angle=0 (проверено по ВСЕМ элементам патча).")]);

        var uniqueLayouts = resolvedPerElement
            .GroupBy(r => r.LayoutFingerprint)
            .Select(g => g.First().Layout)
            .ToList();
        foreach (var layout in uniqueLayouts)
        {
            var variant = backgroundSection.CloneForCalc();
            variant.RebarLayers = layout.Layers.Select(l => l.Clone()).ToList();
            var preflight = ShellMeshPatchPreflight.CheckLinear(variant, concreteDiagram, rebarDiagram, layerDiagrams: null, bounds);
            if (!preflight.IsLinear)
                return new(false, null, preflight.Diagnostics);
        }

        var built = PlanarMeshSnapshotShellModelAdapter.Build(snapshot, sourceRegion.Frame, backgroundSection, field, resolver);
        var rebarDiagnostics = built.RebarDiagnostics.Select(t => t.Diagnostic).ToList();
        accumulatedDiagnostics.AddRange(rebarDiagnostics);
        accumulatedDiagnostics.AddRange(built.MappingDiagnostics.Select(
            m => new FemValidationDiagnostic("shell_mesh_patch_mapping_note", m, IsError: false)));
        if (rebarDiagnostics.Any(d => d.IsError))
            return new(false, null, rebarDiagnostics.Where(d => d.IsError).ToList());

        // BoundaryMappings — по одной записи НА КАЖДЫЙ СЕГМЕНТ границы, не одна на весь внешний
        // контур (см. тот же паттерн в CSfea-версии, Task 8).
        var boundaryNodeIndices = snapshot.BoundaryMappings
            .Where(m => m.Key.Loop == BoundaryLoop.Outer)
            .SelectMany(m => m.NodeIndices)
            .Distinct()
            .ToList();

        var zero = ShellStrainState.Zero;
        var asTangent = backgroundSection.ComputeTangent(
            zero, concreteDiagram, rebarDiagram, layerDiagrams: null,
            asConcreteE_MPa, asNu, asKShear, asOverride, tensionOverride: true);
        double[,] asBlock = (double[,])asTangent.As.Clone();

        string fingerprint = ComputeFingerprint(
            sourceRegion, stripFrame, centerU, centerV, rveSizeM, meshSettings,
            uniqueLayouts, backgroundSection, concreteDiagram, rebarDiagram,
            asConcreteE_MPa, asNu, asKShear, asOverride,
            built.Model.Materials.Select(m => m.Fingerprint).ToList(), built.Model.Drilling,
            bounds, frameAlignmentTolerance);

        var source = new ShellMeshPatchPlateSectionResponse(
            built.Model, built.NodeIndexToTag, boundaryNodeIndices, snapshot.Nodes,
            centerU, centerV, runner, executablePath, cancellationToken, bounds, asBlock, fingerprint);
        return new(true, source, accumulatedDiagnostics.Where(d => !d.IsError).ToList());
    }

    static bool PatchInsideRegion(PlanarRegion region, double[] contourU, double[] contourV)
    {
        var hull = region.Hull;
        if (hull == null) return false;
        var hullPoly = hull.X.Zip(hull.Y, (x, y) => (x, y)).ToList();
        var holePolys = region.Holes.Select(h => h.X.Zip(h.Y, (x, y) => (x, y)).ToList()).ToList();

        for (int i = 0; i < contourU.Length; i++)
        {
            if (!CSTriangulation.GeometryUtils.PointInPolygon(contourU[i], contourV[i], hullPoly))
                return false;
            if (holePolys.Any(h => CSTriangulation.GeometryUtils.PointInPolygon(contourU[i], contourV[i], h)))
                return false;
        }
        return true;
    }

    static bool NodesInsideRegion(PlanarRegion region, IReadOnlyList<PlanarMeshNode> nodes)
    {
        var hull = region.Hull;
        if (hull == null) return false;
        var hullPoly = hull.X.Zip(hull.Y, (x, y) => (x, y)).ToList();
        var holePolys = region.Holes.Select(h => h.X.Zip(h.Y, (x, y) => (x, y)).ToList()).ToList();

        foreach (var node in nodes)
        {
            if (!CSTriangulation.GeometryUtils.PointInPolygon(node.U, node.V, hullPoly))
                return false;
            if (holePolys.Any(h => CSTriangulation.GeometryUtils.PointInPolygon(node.U, node.V, h)))
                return false;
        }
        return true;
    }

    static string ComputeFingerprint(
        PlanarRegion region, Frame3D stripFrame, double centerU, double centerV, double rveSizeM,
        PlanarMeshSettings meshSettings, IReadOnlyList<ResolvedRebarLayout> uniqueLayouts,
        PlateSection backgroundSection, Diagramm concreteDiagram, Diagramm rebarDiagram,
        double asConcreteE_MPa, double asNu, double asKShear, double[,]? asOverride,
        IReadOnlyList<string> nativeMaterialFingerprints, DrillingPolicy drilling,
        ShellMeshPatchStateBounds bounds, double frameAlignmentTolerance)
    {
        var parts = new List<string>
        {
            $"region:{region.GeometryFingerprint}",
            $"stripFrame:{stripFrame.LocalX}:{stripFrame.LocalY}:{stripFrame.LocalZ}",
            $"frameTol:{frameAlignmentTolerance:G17}",
            $"center:{centerU:G17}:{centerV:G17}",
            $"rveSize:{rveSizeM:G17}",
            $"mesh:{meshSettings.MaxElementSizeM:G17}:{meshSettings.Algorithm}:{meshSettings.ElementMode}",
            $"layouts:{string.Join(",", uniqueLayouts.Select(l => PlateRebarLayoutFingerprint.Compute(l.Layers)).OrderBy(f => f, StringComparer.Ordinal))}",
            $"section:{backgroundSection.H:G17}:{backgroundSection.NLayers}:{backgroundSection.PlateModel}:{backgroundSection.SofteningModel}:{backgroundSection.TensionConcrete}",
            $"diagrams:{concreteDiagram.Id}:{rebarDiagram.Id}",
            $"asMaterial:{asConcreteE_MPa:G17}:{asNu:G17}:{asKShear:G17}",
            $"asOverride:{(asOverride == null ? "null" : FormatMatrix(asOverride))}",
            $"nativeMaterials:{string.Join(",", nativeMaterialFingerprints.OrderBy(f => f, StringComparer.Ordinal))}",
            $"drilling:{drilling}",
            $"bounds:{bounds.EpsGammaBoundAbs:G17}:{bounds.KappaBoundAbs:G17}",
            "preflight:relTol=1e-4:absTol=1e-6:pointsPerAxis=3",
            "tangentFdStep=1e-6",
            "engine:opensees",
        };
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join("|", parts))));
    }

    static string FormatMatrix(double[,] m)
    {
        var parts = new List<string>();
        for (int i = 0; i < m.GetLength(0); i++)
        for (int j = 0; j < m.GetLength(1); j++)
            parts.Add(m[i, j].ToString("G17"));
        return string.Join(",", parts);
    }
}
