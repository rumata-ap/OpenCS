using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>Срез 7, Task 11: оркестратор сверки полосы. Реальные бинарники не запускаются —
/// мешер и analysis runner инъецируются фейками.</summary>
public sealed class PlateStripAnalogyRunnerTests
{
    const double LengthM = 6.0;
    const double WidthM = 2.0;
    const double ConcreteEValue = 30_000.0;

    [Fact]
    public async Task MeshDiagnostics_StopRunAndDoNotCallOpenSees()
    {
        var analysis = new FakeAnalysisRunner(_ => Completed(1.0));

        var result = await Run(new FakeMesher { Calculable = false }, analysis);

        Assert.NotEmpty(result.MeshDiagnostics);
        Assert.False(result.IsConverged);
        Assert.Equal(0, analysis.CallCount);
    }

    [Fact]
    public async Task FailedOutcome_IsReportedAndStopsRun()
    {
        var analysis = new FakeAnalysisRunner(_ =>
            new ShellAnalysisRunResult(ShellAnalysisOutcome.NotConverged, null, null, "OpenSees упал"));

        var result = await Run(new FakeMesher(), analysis);

        Assert.False(result.IsConverged);
        Assert.Contains(result.BoundaryDiagnostics, d => d.Contains("упал"));
        Assert.Empty(result.BeamResultants);
    }

    /// <summary>Ловушка Runner-ов: Completed ещё не значит, что достигнута полная нагрузка.</summary>
    [Fact]
    public async Task CompletedAtPartialLoadFactor_IsNotConverged()
    {
        var analysis = new FakeAnalysisRunner(_ => Completed(0.4));

        var result = await Run(new FakeMesher(), analysis);

        Assert.False(result.IsConverged);
        Assert.Contains(result.BoundaryDiagnostics, d => d.Contains("LoadFactor"));
    }

    [Fact]
    public async Task NoConvergedSteps_IsReported()
    {
        var analysis = new FakeAnalysisRunner(_ => new ShellAnalysisRunResult(
            ShellAnalysisOutcome.Completed,
            new Structural.ShellResult { Status = "done", Steps = [] }, null, null));

        var result = await Run(new FakeMesher(), analysis);

        Assert.False(result.IsConverged);
        Assert.Contains(result.BoundaryDiagnostics, d => d.Contains("сошедшегося"));
    }

    /// <summary>Ловушка Runner-ов: Algorithm не наследуется, его задаёт оркестратор.</summary>
    [Fact]
    public async Task AlgorithmIsSetExplicitlyOnModelPolicy()
    {
        var analysis = new FakeAnalysisRunner(_ => Completed(1.0));

        await Run(new FakeMesher(), analysis);

        Assert.NotNull(analysis.LastModel);
        Assert.False(string.IsNullOrWhiteSpace(analysis.LastModel!.Policy.Algorithm));
        Assert.Equal("Newton", analysis.LastModel.Policy.Algorithm);
    }

    [Fact]
    public async Task SupportsAreDerivedFromStripScheme()
    {
        var analysis = new FakeAnalysisRunner(_ => Completed(1.0));

        await Run(new FakeMesher(), analysis);

        var fixedNodes = analysis.LastModel!.Nodes.Where(n => n.Fixed.Any(f => f)).ToList();
        Assert.NotEmpty(fixedNodes);
        Assert.All(fixedNodes, n => Assert.True(
            Math.Abs(n.X) < 1e-6 || Math.Abs(n.X - LengthM) < 1e-6,
            "Закрепления обязаны стоять только на концах полосы."));
    }

    [Fact]
    public async Task HappyPath_FillsBothEpuresAndMismatches()
    {
        var analysis = new FakeAnalysisRunner(_ => Completed(1.0));

        var result = await Run(new FakeMesher(), analysis);

        Assert.True(result.IsConverged);
        Assert.True(result.BeamIsCalculable,
            string.Join("; ", result.DomainDiagnostics.Select(d => d.Message)));
        Assert.NotEmpty(result.ShellResultants);
        Assert.NotEmpty(result.BeamResultants);
        Assert.Equal(result.ShellResultants.Count, result.BeamResultants.Count);
        Assert.True(double.IsFinite(result.MaxRelativeMomentMismatch));
        Assert.True(double.IsFinite(result.RelativeDeflectionMismatch));
        Assert.True(result.BeamMaxDeflectionM > 0.0);
    }

    [Fact]
    public async Task MissingWidthSources_AreDiagnosed()
    {
        var analysis = new FakeAnalysisRunner(_ => Completed(1.0));

        var result = await Run(new FakeMesher(), analysis, widthSources: []);

        Assert.True(result.IsConverged);
        Assert.False(result.BeamIsCalculable);
        Assert.Contains(result.DomainDiagnostics, d => d.Code == "plate_strip_source_grid_shape_mismatch");
    }

    static Task<PlateStripAnalogyResult> Run(
        FakeMesher mesher, FakeAnalysisRunner analysis,
        IReadOnlyList<IPlateSectionResponse>? widthSources = null)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, LengthM, LengthM, 0], Y = [-WidthM / 2, -WidthM / 2, WidthM / 2, WidthM / 2] },
            frame: Frame3D.Identity);
        region.Id = 1;

        var source = LinearSource();
        var request = new PlateStripAnalogyRequest
        {
            Region = region,
            Section = new PlateSection { H = 0.3, NLayers = 4, TensionConcrete = true, ConcreteMaterialId = 1 },
            Analogy = new PlateStripBeamAnalogy
            {
                Id = "strip-1", SourceRegionId = 1, ExplicitWidthM = WidthM,
                Fingerprint = "fp", StripFrame = Frame3D.Identity,
                Geometry = new PlateStripGeometry { LengthM = LengthM }
            },
            Loads = [new PlanarLoad { Tag = "q", Kind = PlanarLoadKind.Surface, Components = new PlanarVector3(0, 0, -5.0) }],
            StationFractions = [0.0, 0.25, 0.5, 0.75, 1.0],
            WidthSources = widthSources ?? [source, source],
        };

        return new PlateStripAnalogyRunner().RunAsync(
            request, mesher, new ConcreteOnlyResolver(), analysis, "opensees.exe", CancellationToken.None);
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

    static ShellAnalysisRunResult Completed(double loadFactor)
    {
        var snapshot = FakeMesher.BuildSnapshot();
        var resultants = snapshot.Elements
            .Select(e => new ShellSectionResultants(e.Index + 1, 0, 0, 0, 0, -20_000.0, 0, 0, 0, 0))
            .ToList();
        var displacements = snapshot.Nodes
            .Select(n => new ShellNodeDisplacement(n.Index + 1, 0, 0, -0.004, 0, 0, 0))
            .ToList();

        var step = new RCShellStepResult(
            0, 0, loadFactor, true, displacements, [], [], resultants, []);
        return new ShellAnalysisRunResult(
            ShellAnalysisOutcome.Completed,
            new Structural.ShellResult { Status = "done", Steps = [step] }, null, null);
    }

    sealed class FakeAnalysisRunner(Func<ShellOpenSeesModel, ShellAnalysisRunResult> factory)
        : IShellAnalysisRunner
    {
        public int CallCount { get; private set; }
        public ShellOpenSeesModel? LastModel { get; private set; }

        public Task<ShellAnalysisRunResult> RunAsync(
            ShellOpenSeesModel model, string executablePath, CancellationToken cancellationToken)
        {
            CallCount++;
            LastModel = model;
            return Task.FromResult(factory(model));
        }
    }

    sealed class FakeMesher : IPlanarMesher
    {
        public bool Calculable { get; init; } = true;

        public Task<PlanarMeshSnapshot> BuildAsync(
            PlanarMeshingRequest request, CancellationToken cancellationToken = default)
        {
            if (!Calculable)
                return Task.FromResult(new PlanarMeshSnapshot
                {
                    RegionId = 1,
                    IsCalculable = false,
                    Diagnostics = [new FemValidationDiagnostic("gmsh_failed", "Сетка не построена.")]
                });
            return Task.FromResult(BuildSnapshot());
        }

        /// <summary>Регулярная сетка 4 × 2 на коридоре полосы.</summary>
        internal static PlanarMeshSnapshot BuildSnapshot()
        {
            const int nx = 4, ny = 2;
            double sizeU = LengthM / nx, sizeV = WidthM / ny;
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

            return new PlanarMeshSnapshot
            {
                RegionId = 1, IsCalculable = true, Nodes = nodes, Elements = elements
            };
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
