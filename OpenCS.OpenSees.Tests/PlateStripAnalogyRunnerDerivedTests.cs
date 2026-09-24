using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateStrip;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>Срез 8a, Task 12: сверка полосы с опорами из родителя и граничными интерфейсами.
/// Реальные бинарники не запускаются — мешер и analysis runner инъецируются фейками.
///
/// Регион 0..12 × −1..1, регулярная сетка с шагом 1,5 м по длине (узлы на x = 0, 6, 12), полоса
/// 0 → 6 по оси. Кандидаты по умолчанию — стены на x = 0 (на контуре) и x = 6 (внутри).</summary>
public sealed class PlateStripAnalogyRunnerDerivedTests
{
    const double RegionLength = 12.0;
    const double StripLength = 6.0;
    const double WidthM = 2.0;
    const double ConcreteEValue = 30_000.0;

    static StripSupportCandidate Wall(double u)
    {
        var source = new PlanarBoundarySourceReference("planar_region", $"W{u:G}");
        return new(StripSupportCandidate.BuildId(StripSupportKind.Wall, source, 0), StripSupportKind.Wall,
            new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, [new(u, -1.0), new(u, 1.0)]),
            new bool[6], source);
    }

    static StripBoundaryInterface Settlement(string id, double u, double value) => new()
    {
        Id = id,
        StripId = "strip",
        Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, [new(u, -1.0), new(u, 1.0)]),
        NormalFromReplacedToRetained = new PlanarVector3(1, 0, 0),
        ModeByDof = PlanarBoundaryModeByDof.None.With(PlanarDofMask.UZ, PlanarBoundaryDofMode.Kinematic),
        KinematicAction = new PlanarBoundaryKinematicAction
        {
            InterfaceId = id,
            DofMask = PlanarDofMask.UZ,
            Samples = [new PlanarBoundaryKinematicSample(0, new PlanarVector3(0, 0, value), PlanarVector3.Zero)]
        }
    };

    static StripBoundaryInterface PreserveSupport(double u) => new()
    {
        Id = "preserve",
        StripId = "strip",
        Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, [new(u, -1.0), new(u, 1.0)]),
        NormalFromReplacedToRetained = new PlanarVector3(1, 0, 0),
        ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.PreserveSupport)
    };

    [Fact]
    public async Task NoCandidates_StopsBeforeMeshing()
    {
        var mesher = new RecordingMesher();

        var result = await Run(mesher, supports: []);

        Assert.Contains(result.DomainDiagnostics, d => d.Code == "plate_strip_support_not_found");
        Assert.Equal(0, mesher.CallCount);
    }

    [Fact]
    public async Task StationsNotSpanningStrip_AreRejectedBeforeDerivation()
    {
        var mesher = new RecordingMesher();

        var result = await Run(mesher, stations: [0.0, 0.5, 0.75]);

        Assert.Contains(result.DomainDiagnostics, d => d.Code == "plate_strip_stations_must_span_strip");
        Assert.Null(result.Derivation);
        Assert.Equal(0, mesher.CallCount);
    }

    [Fact]
    public async Task Constraints_KeepRegionOnesAndEmbedOnlyInteriorFootprints()
    {
        var mesher = new RecordingMesher();
        var analysis = new FakeAnalysisRunner();

        var result = await Run(mesher, analysis, regionConstraint: "region-pt");

        var ids = mesher.LastRequest!.EffectiveConstraintObjects.Select(c => c.Id).ToList();
        Assert.Equal(["region-pt", Wall(6).Id], ids);
        Assert.Equal(PlanarMeshKind.EmbeddedCurve, mesher.LastRequest.EffectiveConstraintObjects[1].MeshFacet.Kind);

        var restrained = analysis.LastModel!.Nodes.Where(n => n.Fixed[2]).ToList();
        Assert.NotEmpty(restrained);
        Assert.All(restrained, n => Assert.True(Math.Abs(n.X) < 1e-9 || Math.Abs(n.X - 6.0) < 1e-9,
            $"Узел {n.Tag} на x={n.X} закреплён, хотя не лежит на стене."));
        Assert.Equal(2 * 3, restrained.Count);
        Assert.True(result.IsConverged);
    }

    [Fact]
    public async Task DerivedRun_FillsSchemeEndActionsAndFrozenWarning()
    {
        var result = await Run(new RecordingMesher(), new FakeAnalysisRunner());

        Assert.True(result.BeamIsCalculable, string.Join("; ", result.DomainDiagnostics.Select(d => d.Message)));
        Assert.Equal(StripBeamEndCondition.Pinned, result.Derivation!.Scheme!.StartCondition);
        Assert.NotNull(result.EndActions);
        Assert.NotEqual(0.0, result.EndActions!.StartMy);
        Assert.Contains(result.DomainDiagnostics, d => d.Code == "plate_strip_end_moments_frozen" && !d.IsError);
    }

    [Fact]
    public async Task CandidateWithoutMeshNodes_IsReportedById()
    {
        var analysis = new FakeAnalysisRunner();
        var lost = Wall(3.25); // шаг сетки 1,5 м — узлов на x = 3,25 нет

        var result = await Run(new RecordingMesher(), analysis, supports: [Wall(0), Wall(6), lost]);

        var diagnostic = Assert.Single(result.DomainDiagnostics, d => d.Code == "plate_strip_parent_support_not_meshed");
        Assert.Contains(lost.Id, diagnostic.Message);
        Assert.Equal(0, analysis.CallCount);
    }

    [Fact]
    public async Task AutomaticIdClashingWithRegionConstraint_IsRejected()
    {
        var mesher = new RecordingMesher();

        var result = await Run(mesher, regionConstraint: Wall(6).Id);

        Assert.Contains(result.DomainDiagnostics, d => d.Code == "plate_strip_constraint_id_conflict");
        Assert.Equal(0, mesher.CallCount);
    }

    [Fact]
    public async Task PreserveSupportWithoutSupport_IsErrorInDerivedAndInfoInExplicit()
    {
        var derived = await Run(new RecordingMesher(), interfaces: [PreserveSupport(4.5)]);
        Assert.Contains(derived.DomainDiagnostics,
            d => d.Code == "plate_strip_boundary_preserve_support_without_support" && d.IsError);

        var explicitRun = await Run(new RecordingMesher(), new FakeAnalysisRunner(),
            mode: PlateStripSupportMode.Explicit, interfaces: [PreserveSupport(4.5)], regionLength: StripLength);
        Assert.Contains(explicitRun.DomainDiagnostics,
            d => d.Code == "plate_strip_preserve_support_unchecked" && !d.IsError);
        Assert.DoesNotContain(explicitRun.DomainDiagnostics,
            d => d.Code == "plate_strip_boundary_preserve_support_without_support");
    }

    [Fact]
    public async Task KinematicInterface_ReachesShellAndBeam()
    {
        var mesher = new RecordingMesher();
        var analysis = new FakeAnalysisRunner();

        var result = await Run(mesher, analysis, interfaces: [Settlement("settle", 6.0, -0.005)]);

        // Геометрия интерфейса совпадает со следом стены x = 6: второй встроенной кривой нет.
        Assert.DoesNotContain(mesher.LastRequest!.EffectiveConstraintObjects, c => c.Id.StartsWith("interface:"));

        var stage = Assert.Single(analysis.LastModel!.Stages);
        Assert.Equal(3, stage.KinematicLoads.Count);
        Assert.All(stage.KinematicLoads, load =>
        {
            Assert.Equal(3, load.Dof);
            Assert.Equal(-0.005, load.Value, 15);
            var node = analysis.LastModel.Nodes.Single(n => n.Tag == load.NodeTag);
            Assert.Equal(6.0, node.X, 9);
            Assert.False(node.Fixed[2], "sp и fix на одном DOF в OpenSees конфликтуют.");
            Assert.True(node.Fixed[0]);
        });

        Assert.Equal([new StripPrescribedDisplacement(4, 2, -0.005)], result.Prescribed);
    }

    [Fact]
    public async Task ConflictingKinematicInterfaces_StopBeforeMeshing()
    {
        var mesher = new RecordingMesher();

        var result = await Run(mesher,
            interfaces: [Settlement("a", 4.5, -0.005), Settlement("b", 4.5, -0.01)]);

        Assert.Contains(result.DomainDiagnostics, d => d.Code == "plate_strip_kinematic_conflict");
        Assert.Equal(0, mesher.CallCount);
    }

    static Task<PlateStripAnalogyResult> Run(
        RecordingMesher mesher,
        FakeAnalysisRunner? analysis = null,
        IReadOnlyList<StripSupportCandidate>? supports = null,
        IReadOnlyList<double>? stations = null,
        string? regionConstraint = null,
        IReadOnlyList<StripBoundaryInterface>? interfaces = null,
        PlateStripSupportMode mode = PlateStripSupportMode.DerivedFromParent,
        double regionLength = RegionLength)
    {
        mesher.Length = regionLength;
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, regionLength, regionLength, 0], Y = [-WidthM / 2, -WidthM / 2, WidthM / 2, WidthM / 2] },
            frame: Frame3D.Identity);
        region.Id = 1;
        if (regionConstraint != null)
            region.ConstraintObjects.Add(PlanarConstraintObject.Point(
                regionConstraint, new PlanarPoint2D(9.0, 0.0),
                new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint)));

        var start = new SupportLocus();
        var end = new SupportLocus
        {
            Frame = new Frame3D(new PlanarVector3(StripLength, 0, 0),
                Frame3D.Identity.LocalX, Frame3D.Identity.LocalY, Frame3D.Identity.LocalZ)
        };
        var source = LinearSource();
        var request = new PlateStripAnalogyRequest
        {
            Region = region,
            Section = new PlateSection { H = 0.3, NLayers = 4, TensionConcrete = true, ConcreteMaterialId = 1 },
            Analogy = new PlateStripBeamAnalogy
            {
                Id = "strip", SourceRegionId = 1, ExplicitWidthM = WidthM,
                Fingerprint = "fp", StripFrame = Frame3D.Identity,
                StartSupportLocus = start, EndSupportLocus = end,
                Geometry = new PlateStripGeometry { LengthM = StripLength }
            },
            Loads = [new PlanarLoad { Tag = "q", Kind = PlanarLoadKind.Surface, Components = new PlanarVector3(0, 0, -5.0) }],
            StationFractions = stations ?? [0.0, 0.25, 0.5, 0.75, 1.0],
            WidthSources = [source, source],
            SupportMode = mode,
            ParentSupports = supports ?? [Wall(0), Wall(6)],
            Interfaces = interfaces ?? []
        };

        return new PlateStripAnalogyRunner().RunAsync(
            request, mesher, new ConcreteOnlyResolver(), analysis ?? new FakeAnalysisRunner(),
            "opensees.exe", CancellationToken.None);
    }

    static ConstantLinearPlateSectionResponse LinearSource()
    {
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        var ass = new double[2, 2];
        a[0, 0] = a[1, 1] = ConcreteEValue * 0.3;
        d[0, 0] = d[1, 1] = ConcreteEValue * 0.027 / 12.0;
        ass[0, 0] = ass[1, 1] = 400.0;
        return new ConstantLinearPlateSectionResponse(a, b, d, ass, "runner-linear");
    }

    sealed class FakeAnalysisRunner : IShellAnalysisRunner
    {
        public int CallCount { get; private set; }
        public ShellOpenSeesModel? LastModel { get; private set; }

        public Task<ShellAnalysisRunResult> RunAsync(
            ShellOpenSeesModel model, string executablePath, CancellationToken cancellationToken)
        {
            CallCount++;
            LastModel = model;
            var resultants = model.Elements
                .Select(e => new ShellSectionResultants(e.Tag, 0, 0, 0, 0, 20_000.0, 0, 0, 0, 0))
                .ToList();
            var displacements = model.Nodes
                .Select(n => new ShellNodeDisplacement(n.Tag, 0, 0, -0.004, 0, 0, 0))
                .ToList();
            var step = new RCShellStepResult(0, 0, 1.0, true, displacements, [], [], resultants, []);
            return Task.FromResult(new ShellAnalysisRunResult(
                ShellAnalysisOutcome.Completed,
                new Structural.ShellResult { Status = "done", Steps = [step] }, null, null));
        }
    }

    /// <summary>Регулярная сетка шагом 1,5 × 1 м на всём регионе; запоминает запрос.</summary>
    sealed class RecordingMesher : IPlanarMesher
    {
        public double Length { get; set; } = RegionLength;
        public int CallCount { get; private set; }
        public PlanarMeshingRequest? LastRequest { get; private set; }

        public Task<PlanarMeshSnapshot> BuildAsync(
            PlanarMeshingRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            int nx = (int)Math.Round(Length / 1.5), ny = 2;
            double sizeU = Length / nx, sizeV = WidthM / ny;
            var nodes = new List<PlanarMeshNode>();
            for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                double u = i * sizeU, v = j * sizeV - WidthM / 2.0;
                nodes.Add(new PlanarMeshNode(j * (nx + 1) + i, u, v, u, v, 0));
            }

            var elements = new List<PlanarMeshElement>();
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int n0 = j * (nx + 1) + i;
                elements.Add(new PlanarMeshElement(
                    j * nx + i, PlanarMeshElementKind.Quadrangle4,
                    [n0, n0 + 1, n0 + nx + 2, n0 + nx + 1]));
            }

            return Task.FromResult(new PlanarMeshSnapshot
            {
                RegionId = 1, IsCalculable = true, Nodes = nodes, Elements = elements
            });
        }
    }

    sealed class ConcreteOnlyResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}",
                new ElasticIsotropicShellMaterialSpec(ConcreteEValue * 1000.0, 0.0))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
            throw new NotSupportedException("Тест не использует армирование.");
    }
}
