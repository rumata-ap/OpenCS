using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using CSfea.Core;
using OpenCS.Gmsh;

namespace CSfea.CScoreBridge;

public sealed record ShellMeshPatchBuildResult(
    bool IsCalculable,
    ShellMeshPatchPlateSectionResponse? Source,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>RVE-гомогенизация IPlateSectionResponse через реальный CSfea.Core.ShellMesh —
/// см. docs/superpowers/specs/2026-08-11-plate-strip-shell-mesh-adapter-design.md. Request-local,
/// не персистентный; ограничен precondition-проверками RvePatchPreconditions (см. Task 2).</summary>
public sealed class ShellMeshPatchPlateSectionResponse : IPlateSectionResponse
{
    readonly LinearDirichletSystem _system;
    readonly ShellMesh _mesh;
    readonly double[] _patchOriginWorld;
    readonly double[,] _patchBasis;
    readonly int[] _boundaryFixedDofs;
    readonly double _centerU;
    readonly double _centerV;
    readonly IReadOnlyList<PlanarMeshNode> _nodes;
    readonly IReadOnlyList<int> _boundaryNodeIndices;
    readonly ShellMeshPatchStateBounds _bounds;
    readonly double[,] _as;

    ShellMeshPatchPlateSectionResponse(
        LinearDirichletSystem system, ShellMesh mesh, double[] patchOriginWorld, double[,] patchBasis,
        int[] boundaryFixedDofs, double centerU, double centerV,
        IReadOnlyList<PlanarMeshNode> nodes, IReadOnlyList<int> boundaryNodeIndices,
        ShellMeshPatchStateBounds bounds, double[,] asBlock, string fingerprint)
    {
        _system = system;
        _mesh = mesh;
        _patchOriginWorld = patchOriginWorld;
        _patchBasis = patchBasis;
        _boundaryFixedDofs = boundaryFixedDofs;
        _centerU = centerU;
        _centerV = centerV;
        _nodes = nodes;
        _boundaryNodeIndices = boundaryNodeIndices;
        _bounds = bounds;
        _as = asBlock;
        Fingerprint = fingerprint;
    }

    public EquivalentSectionSourceKind SourceKind => EquivalentSectionSourceKind.ShellMeshCsfea;
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
        // Знаковое соглашение — PlateSectionResponseMath.BuildH: h[i,j+3]=b[i,j] (N←κ),
        // h[i+3,j]=b[j,i] (M←ε, транспонировано). Поэтому при пробе по ε (k<3) производная
        // ∂M_row/∂ε_k пишется в b[k, row] (не b[row, k]); при пробе по κ (k>=3) производная
        // ∂N_row/∂κ_(k-3) пишется в b[row, k-3]. Пробы ±h — численный артефакт дифференцирования,
        // НЕ реальный вызов контракта: ComputeForces() (не Forces()) используется намеренно, чтобы
        // проба у самой границы bounds не бросала ArgumentOutOfRangeException. Шаг h ДОПОЛНИТЕЛЬНО
        // ограничивается расстоянием до соответствующей границы bounds на каждой компоненте —
        // иначе проба ±1e-6 у самой границы выходила бы за заявленный рабочий диапазон линейности.
        var values = state.ToArray();
        for (int k = 0; k < 6; k++)
        {
            double bound = k < 3 ? _bounds.EpsGammaBoundAbs : _bounds.KappaBoundAbs;
            double h = Math.Min(hNominal, Math.Max(bound - Math.Abs(values[k]), bound * 1e-9));
            var plus = Perturb(state, k, h);
            var minus = Perturb(state, k, -h);
            var fPlus = ComputeForces(plus).ToArray();
            var fMinus = ComputeForces(minus).ToArray();
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
        double[] u = SolveForState(state);
        var points = ShellMeshPatchPostprocessor.SectionResultantsAt(_mesh, u, _patchOriginWorld, _patchBasis);
        var averageSi = ShellMeshPatchPostprocessor.Average(points);
        return ToCScoreUnits(averageSi);
    }

    static ShellStrainState Perturb(ShellStrainState state, int component, double delta)
    {
        double[] v = state.ToArray();
        v[component] += delta;
        return ShellStrainState.FromArray(v);
    }

    double[] SolveForState(ShellStrainState state)
    {
        var uFixed = new double[_boundaryFixedDofs.Length];
        int k = 0;
        foreach (int nodeIndex in _boundaryNodeIndices)
        {
            var node = _nodes[nodeIndex];
            var field = RvePatchKinematics.NodeField(state, _centerU, _centerV, node.U, node.V);
            uFixed[k++] = field.U; uFixed[k++] = field.V; uFixed[k++] = field.W;
            uFixed[k++] = field.ThetaX; uFixed[k++] = field.ThetaY;
        }
        return _system.Solve(uFixed);
    }

    static PlateResultants ToCScoreUnits(PlateResultants siNPerM) => new(
        siNPerM.Nx / 1000.0, siNPerM.Ny / 1000.0, siNPerM.Nxy / 1000.0,
        siNPerM.Mx / 1000.0, siNPerM.My / 1000.0, siNPerM.Mxy / 1000.0);

    /// <summary>Строит RVE-патч (Gmsh + линейный решатель) для сечения, возможно неоднородного
    /// по площади патча (зоны армирования). Precondition: stripFrame ориентированно совпадает с
    /// sourceRegion.Frame (RvePatchPreconditions.FrameAligned), ВСЕ резолвленные по элементам
    /// слои армирования Angle=0, весь патч внутри Hull/вне Holes региона.</summary>
    public static async Task<ShellMeshPatchBuildResult> CreateAsync(
        PlanarRegion sourceRegion, Frame3D stripFrame, double centerU, double centerV,
        PlateRebarField field, PlateSection backgroundSection, PlateSectionMaterials materials,
        ShellMeshPatchStateBounds bounds, double rveSizeM, PlanarMeshSettings meshSettings,
        GmshPlanarMesher mesher, double frameAlignmentTolerance = 1e-6)
    {
        ArgumentNullException.ThrowIfNull(sourceRegion);
        ArgumentNullException.ThrowIfNull(stripFrame);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(backgroundSection);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(mesher);

        // Precondition: PlateSectionShellResponse.Forces() на runtime-пути вызывает
        // _section.Compute(...) БЕЗ tensionOverride (использует backgroundSection.TensionConcrete),
        // а preflight/As ниже используют tensionOverride:true явно. Совпадение веток гарантируется
        // только если TensionConcrete уже true — тот же precondition, что PlateSectionTangentSnapshot.Create
        // уже требует в Срезе 2.
        if (!backgroundSection.TensionConcrete)
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_requires_tension_concrete",
                "RVE-адаптер поддерживает только PlateSection.TensionConcrete=true (иначе preflight/As расходятся с runtime-веткой PlateSectionShellResponse).")]);

        if (!RvePatchPreconditions.FrameAligned(stripFrame, sourceRegion.Frame, frameAlignmentTolerance))
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_frame_mismatch",
                "StripFrame не совпадает ориентированно с PlanarRegion.Frame — общий наклонный/анизотропный случай вне объёма Среза 3b.")]);

        var contour = RvePatchKinematics.SquareContourUV(centerU, centerV, rveSizeM);
        var contourU = contour.Select(p => p.U).ToArray();
        var contourV = contour.Select(p => p.V).ToArray();

        if (!PatchInsideRegion(sourceRegion, contourU, contourV))
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_outside_region",
                "RVE-патч выходит за пределы Hull исходного PlanarRegion или пересекает отверстие.")]);

        var patchRegion = PlanarRegion.CreateFromContour(
            new Contour { X = contourU, Y = contourV }, frame: sourceRegion.Frame);

        var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(patchRegion, meshSettings));
        if (!snapshot.IsCalculable)
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_gmsh_unmeshable",
                $"Gmsh не смог построить сетку RVE-патча: {string.Join("; ", snapshot.Diagnostics.Select(d => d.Message))}")]);

        // Дополнительная проверка узлов реальной сетки (не только углов исходного квадрата) —
        // Gmsh может слегка выйти за контур на кривых границах; для простого квадрата это
        // избыточно, но дёшево и защищает от будущих несиловых контуров.
        if (!NodesInsideRegion(sourceRegion, snapshot.Nodes))
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_outside_region",
                "Один или несколько узлов построенной сетки RVE-патча вне Hull/внутри Hole региона.")]);

        var centroids = snapshot.Elements
            .Select(e => (e.Index, Centroid: e.Centroid(snapshot.Nodes)))
            .Select(t => (t.Index, t.Centroid.U, t.Centroid.V))
            .ToList();
        var resolvedPerElement = PlateRebarFieldResolver.ResolveMesh(field, centroids);

        // Диагностики резолвера (например plate_rebar_zone_priority_conflict) не должны молча
        // теряться при переходе между bridge-слоями. IsError=true блокирует построение;
        // предупреждения прокидываются в итоговый результат.
        var accumulatedDiagnostics = new List<FemValidationDiagnostic>();
        foreach (var resolved in resolvedPerElement)
            accumulatedDiagnostics.AddRange(resolved.Layout.Diagnostics);
        if (accumulatedDiagnostics.Any(d => d.IsError))
            return new(false, null, accumulatedDiagnostics.Where(d => d.IsError).ToList());

        var allLayers = resolvedPerElement.SelectMany(r => r.Layout.Layers).ToList();
        if (!RvePatchPreconditions.AllRebarAnglesZero(allLayers))
            return new(false, null, [new FemValidationDiagnostic(
                "shell_mesh_patch_angled_rebar_unsupported",
                "RVE-адаптер поддерживает только раскладки с Angle=0 (проверено по ВСЕМ элементам патча, не только по центру).")]);

        var uniqueLayouts = resolvedPerElement
            .GroupBy(r => r.LayoutFingerprint)
            .Select(g => g.First().Layout)
            .ToList();
        foreach (var layout in uniqueLayouts)
        {
            var variant = backgroundSection.CloneForCalc();
            variant.RebarLayers = layout.Layers.Select(l => l.Clone()).ToList();
            var preflight = ShellMeshPatchPreflight.CheckLinear(
                variant, materials.ConcreteDiagram, materials.RebarDiagram, materials.LayerDiagrams, bounds);
            if (!preflight.IsLinear)
                return new(false, null, preflight.Diagnostics);
        }

        var built = PlanarMeshSnapshotShellMeshAdapter.Build(snapshot, backgroundSection, field, materials);
        // built.Diagnostics — (ElementId, FemValidationDiagnostic) по маппингу армирования/секций;
        // та же политика блокирующее/предупреждение, что и выше.
        var mapDiagnostics = built.Diagnostics.Select(t => t.Diagnostic).ToList();
        accumulatedDiagnostics.AddRange(mapDiagnostics);
        if (mapDiagnostics.Any(d => d.IsError))
            return new(false, null, mapDiagnostics.Where(d => d.IsError).ToList());

        // BoundaryMappings — по одной записи НА КАЖДЫЙ СЕГМЕНТ границы (PlanarBoundaryKey несёт
        // StartVertex/EndVertex), не одна на весь внешний контур — для квадратного RVE-патча
        // их минимум 4 (по числу сторон). Собираем узлы со всех сегментов внешнего контура.
        var boundaryNodeIndices = snapshot.BoundaryMappings
            .Where(m => m.Key.Loop == BoundaryLoop.Outer)
            .SelectMany(m => m.NodeIndices)
            .Distinct()
            .ToList();
        var fixedDofs = boundaryNodeIndices
            .SelectMany(nodeIndex => Enumerable.Range(0, 5).Select(c => 6 * nodeIndex + c)) // Ux,Uy,Uz,Rx,Ry — Rz (drilling) свободен
            .ToArray();

        var patchBasis = new double[,]
        {
            { stripFrame.LocalX.X, stripFrame.LocalX.Y, stripFrame.LocalX.Z },
            { stripFrame.LocalY.X, stripFrame.LocalY.Y, stripFrame.LocalY.Z },
            { stripFrame.LocalZ.X, stripFrame.LocalZ.Y, stripFrame.LocalZ.Z },
        };
        var centerNode = snapshot.Nodes.OrderBy(n =>
            (n.U - centerU) * (n.U - centerU) + (n.V - centerV) * (n.V - centerV)).First();
        double[] originWorld = [centerNode.X, centerNode.Y, centerNode.Z];

        var system = new LinearDirichletSystem(built.Mesh, fixedDofs);

        // As с теми же параметрами, что реально использует PlateSectionShellResponse.Forces()
        // на runtime-пути (ConcreteE_MPa/Nu/KShear/AsOverride из materials, не defaults
        // ComputeTangent).
        var zero = ShellStrainState.Zero;
        var asTangent = backgroundSection.ComputeTangent(
            zero, materials.ConcreteDiagram, materials.RebarDiagram, materials.LayerDiagrams,
            materials.ConcreteE_MPa, materials.Nu, materials.KShear, materials.AsOverride,
            tensionOverride: true);
        double[,] asBlock = (double[,])asTangent.As.Clone();

        string fingerprint = ComputeFingerprint(
            sourceRegion, stripFrame, centerU, centerV, rveSizeM, meshSettings,
            uniqueLayouts, backgroundSection, materials, bounds, frameAlignmentTolerance);

        var source = new ShellMeshPatchPlateSectionResponse(
            system, built.Mesh, originWorld, patchBasis, fixedDofs, centerU, centerV,
            snapshot.Nodes, boundaryNodeIndices, bounds, asBlock, fingerprint);
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
        PlateSection backgroundSection, PlateSectionMaterials materials,
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
            $"diagrams:{materials.ConcreteDiagram.Id}:{materials.RebarDiagram.Id}",
            $"materials:{materials.ConcreteE_MPa:G17}:{materials.Nu:G17}:{materials.KShear:G17}",
            $"asOverride:{(materials.AsOverride == null ? "null" : FormatMatrix(materials.AsOverride))}",
            $"bounds:{bounds.EpsGammaBoundAbs:G17}:{bounds.KappaBoundAbs:G17}",
            "preflight:relTol=1e-4:absTol=1e-6:pointsPerAxis=3",
            "tangentFdStep=1e-6",
            "engine:csfea",
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
