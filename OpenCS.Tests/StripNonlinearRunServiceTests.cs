using CScore;
using CScore.Planar;
using CScore.PlateStrip;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Срез 8d: нелинейный прогон полосы эквивалентного сечения из проекта с сохранением в SQLite.</summary>
public sealed class StripNonlinearRunServiceTests
{
    const string OpenSeesPath = @"C:\Tools\OpenSees\bin\OpenSees.exe";
    const double LengthM = 6.0;
    const double WidthM = 2.0;

    static StripNonlinearRunInput Input(double qKnPerM, bool fiber = false) => new()
    {
        LoadSteps = 6,
        ElementCount = 6,
        FiberCheck = fiber,
        Loads = [new StripNonlinearLoadItem
        {
            Kind = StripNonlinearLoadKind.UniformQz, FromFraction = 0.0, ToFraction = 1.0, Value = qKnPerM
        }]
    };

    [Fact]
    public async Task Run_Elastic_SimplySupported_MomentIsQL2Over8_AndResultIsSaved()
    {
        using var fixture = new Fixture();
        var equivalent = fixture.BuildEquivalent();
        var service = new StripNonlinearRunService(fixture.Db, fixture.Materials);

        var result = await service.RunAsync(equivalent, Input(-2.0), CalcType.C, null, CancellationToken.None);

        Assert.True(result.IsConverged, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotEqual(0, result.Id);
        Assert.Equal(7, result.StationFractions.Length);
        Assert.Equal(2.0 * LengthM * LengthM / 8.0, Math.Abs(result.BeamResultants[3][1]), 1);
        Assert.True(result.BeamDeflections[3] < 0.0);
        Assert.Equal(result.MaxDeflectionM, Math.Abs(result.BeamDeflections[3]), 12);
        Assert.False(result.FiberChecked);

        var stored = Assert.Single(fixture.Db.StripNonlinearResults);
        Assert.Same(result, stored);
        Assert.Equal(equivalent.Id, stored.EquivalentSectionId);
        Assert.False(stored.IsStaleFor(equivalent));
    }

    [Fact]
    public async Task Run_BeyondYield_DeflectsMoreThanLinearScaling_AndConverges()
    {
        using var fixture = new Fixture();
        var equivalent = fixture.BuildEquivalent();
        var service = new StripNonlinearRunService(fixture.Db, fixture.Materials);

        var elastic = await service.RunAsync(equivalent, Input(-1.0), CalcType.C, null, CancellationToken.None);
        var beyond = await service.RunAsync(equivalent, Input(-5.5), CalcType.C, null, CancellationToken.None);

        Assert.True(elastic.IsConverged);
        Assert.True(beyond.IsConverged, string.Join("; ", beyond.Diagnostics.Select(d => d.Message)));
        Assert.True(beyond.MaxDeflectionM > 5.5 * elastic.MaxDeflectionM * 1.02,
            "за пределом упругости прогиб обязан расти быстрее нагрузки");
        Assert.Equal(2, fixture.Db.StripNonlinearResults.Count);
    }

    [Fact]
    public async Task Run_CapacityExhausted_SavesPartialStateWithAchievedLoadFactor()
    {
        using var fixture = new Fixture();
        var equivalent = fixture.BuildEquivalent();
        var service = new StripNonlinearRunService(fixture.Db, fixture.Materials);

        var result = await service.RunAsync(equivalent, Input(-8.0, fiber: true), CalcType.C, null, CancellationToken.None);

        Assert.False(result.IsConverged);
        Assert.InRange(result.AchievedLoadFactor, 0.3, 0.99);
        Assert.NotEmpty(result.BeamResultants);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_nonlinear_not_converged");
        Assert.Contains(result.Diagnostics, d => d.Code == "strip_run_fiber_skipped_partial");
        Assert.Single(fixture.Db.StripNonlinearResults);
    }

    [Fact]
    public async Task Run_InvalidInput_IsNotSavedAndReportsDiagnostic()
    {
        using var fixture = new Fixture();
        var equivalent = fixture.BuildEquivalent();
        var service = new StripNonlinearRunService(fixture.Db, fixture.Materials);

        var result = await service.RunAsync(
            equivalent, new StripNonlinearRunInput(), CalcType.C, null, CancellationToken.None);

        Assert.False(result.IsConverged);
        Assert.Equal(0, result.Id);
        Assert.Contains(result.Diagnostics, d => d.Code == "strip_run_invalid_input");
        Assert.Empty(fixture.Db.StripNonlinearResults);
    }

    [Fact]
    public async Task Run_FiberCheckWithoutOpenSees_WarnsAndStillSavesBeamResult()
    {
        using var fixture = new Fixture();
        var equivalent = fixture.BuildEquivalent();
        var service = new StripNonlinearRunService(fixture.Db, fixture.Materials);

        var result = await service.RunAsync(
            equivalent, Input(-2.0, fiber: true), CalcType.C, openSeesExecutablePath: null, CancellationToken.None);

        Assert.True(result.IsConverged);
        Assert.False(result.FiberChecked);
        Assert.Contains(result.Diagnostics, d => d.Code == "strip_run_opensees_not_found" && !d.IsError);
        Assert.Single(fixture.Db.StripNonlinearResults);
    }

    [Fact]
    public async Task Run_RegionMissing_ReportsBlockingDiagnostic()
    {
        using var fixture = new Fixture();
        var equivalent = fixture.BuildEquivalent();
        equivalent.SourceRegionId = 9999;
        var service = new StripNonlinearRunService(fixture.Db, fixture.Materials);

        var result = await service.RunAsync(equivalent, Input(-2.0), CalcType.C, null, CancellationToken.None);

        Assert.False(result.IsConverged);
        Assert.Contains(result.Diagnostics, d => d.Code == "strip_run_region_not_found");
        Assert.Empty(fixture.Db.StripNonlinearResults);
    }

    [Fact]
    public async Task Run_WithFiberCheck_AgreesWithOwnBeam()
    {
        if (!File.Exists(OpenSeesPath))
            throw Xunit.Sdk.SkipException.ForSkip($"OpenSees не найден: {OpenSeesPath}");
        using var fixture = new Fixture();
        var equivalent = fixture.BuildEquivalent();
        var service = new StripNonlinearRunService(fixture.Db, fixture.Materials);

        var result = await service.RunAsync(
            equivalent, Input(-5.5, fiber: true), CalcType.C, OpenSeesPath, CancellationToken.None);

        Assert.True(result.IsConverged, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.True(result.FiberChecked);
        Assert.Equal(result.StationFractions.Length, result.FiberResultants.Length);
        Assert.InRange(result.FiberMomentMismatch, 0.0, 0.02);
        Assert.InRange(result.FiberDeflectionMismatch, 0.0, 0.03);
    }

    sealed class Fixture : IDisposable
    {
        readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-strip-run-{Guid.NewGuid():N}.db");
        const int SchemaId = 4;

        public DatabaseService Db { get; }
        public Dictionary<int, Material> Materials { get; }

        public Fixture()
        {
            Db = new DatabaseService(_path);
            Materials = new() { [1] = Linear(1), [2] = Linear(2) };
        }

        public EquivalentSection BuildEquivalent()
        {
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 10, 10, 0], Y = [0, 0, 10, 10] });
            int regionId = Db.AddPlanarRegion(region, SchemaId);

            // ConcreteDiagramType = L2: LinearMaterial — ReSteelF, трёхлинейная диаграмма для него недопустима.
            var plate = new PlateSection
            {
                H = 0.3, NLayers = 20, TensionConcrete = true, PlateModel = "layered",
                ConcreteMaterialId = 1, RebarMaterialId = 2, ConcreteDiagramType = DiagrammType.L2,
                RebarLayers =
                [
                    new PlateRebarLayer { InputMode = "direct", Asx = 0.002, Zsx = -0.12 },
                    new PlateRebarLayer { InputMode = "direct", Asx = 0.001, Zsx = 0.12 }
                ]
            };
            Db.SavePlateSection(plate);
            Db.SaveFemMember(new CScore.Fem.FemMember
            {
                SchemaId = SchemaId, ElemTag = $"plate-{regionId}", ElemType = "shell",
                PlanarRegionId = regionId, PlateSectionId = plate.Id
            });

            var strip = new PlateStripBeamAnalogy
            {
                Id = "strip-run", SourceRegionId = regionId, ExplicitWidthM = WidthM, Fingerprint = "strip-fp",
                Geometry = new PlateStripGeometry
                {
                    CenterLine = [new PlanarPoint2D(2, 5), new PlanarPoint2D(8, 5)],
                    LeftBoundary = [new PlanarPoint2D(2, 6), new PlanarPoint2D(8, 6)],
                    RightBoundary = [new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4)],
                    Polygon = [new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4),
                               new PlanarPoint2D(8, 6), new PlanarPoint2D(2, 6)],
                    LengthM = LengthM
                }
            };

            var service = new EquivalentSectionProjectService(Db, Materials);
            var built = service.BuildAndSave(
                strip, SchemaId, regionId, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 2);
            Assert.True(built.IsCalculable, string.Join("; ", built.Diagnostics.Select(d => d.Message)));
            return built.Section!;
        }

        static Material Linear(int id)
        {
            MaterialChars Ch(CalcType ct) => new(ct)
            {
                E = 30_000, Ry = 600, Ru = 660, Ft = 600, Fc = -600,
                Ec2 = -0.5, Et2 = 0.5, Type = MatType.ReSteelF,
            };
            var m = new Material { Id = id, E = 30_000, Type = MatType.ReSteelF, Tag = $"lin-{id}" };
            m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
            return m;
        }

        public void Dispose()
        {
            Db.Dispose();
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        }
    }
}
