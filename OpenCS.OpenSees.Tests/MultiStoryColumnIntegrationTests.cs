using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using CSmath;
using OpenCS.Gmsh;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tests.Fixtures;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>Реальный end-to-end прогон многоэтажной колонны (3 перекрытия + 2 балочных сегмента)
/// через внешние Gmsh и OpenSees. В отличие от MultiStoryColumnRunnerTests (fake mesher +
/// injected analysis runner) здесь используется production-конвейер целиком: real
/// GmshPlanarMesher (N независимых запусков по уровням), MultiStoryColumnShellModelAssembler,
/// boundary/load pipeline и MultiStoryColumnRunner с default (concrete) ShellAnalysisRunner.</summary>
public sealed class MultiStoryColumnIntegrationTests
{
    [Fact]
    public async Task RunAsync_ThreeLevelColumn_WithDoorOpeningOnMiddleLevel_ConvergesWithinCodeLimits()
    {
        string openSeesExecutable = OpenSeesTestExecutable.ResolveOrSkip();
        string gmshRoot = Path.Combine(
            Path.GetTempPath(), "opencs-multistory-column-e2e", Guid.NewGuid().ToString("N"));
        try
        {
            var fragment = BuildThreeLevelFragment(withOpening: true, overload: false);

            var result = await RunColumn(fragment, gmshRoot, openSeesExecutable);

            Assert.Empty(result.MeshDiagnostics);
            Assert.Empty(result.AssemblyDiagnostics);
            Assert.Empty(result.AnalysisDiagnostics);
            Assert.True(result.IsConverged, string.Join("; ", result.AnalysisDiagnostics));
            Assert.NotNull(result.ForceBalance);
            Assert.True(result.ForceBalance!.RelativeUnbalance <= 1e-3,
                $"Относительная невязка баланса {result.ForceBalance.RelativeUnbalance * 100:F3}% превышает допуск 0.1%.");
            Assert.NotEmpty(result.FiberStates);
            Assert.Equal(FragmentAuditVerdict.Valid, result.AuditReport.Verdict);
            Assert.False(string.IsNullOrEmpty(result.GmshArtifactDirectory),
                "Реальный Gmsh-конвейер должен заполнить GmshArtifactDirectory.");
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ThreeLevelColumn_WithExcessiveLoad_FailsWithRealNonConvergence()
    {
        string openSeesExecutable = OpenSeesTestExecutable.ResolveOrSkip();
        string gmshRoot = Path.Combine(
            Path.GetTempPath(), "opencs-multistory-column-e2e-invalid", Guid.NewGuid().ToString("N"));
        try
        {
            var fragment = BuildThreeLevelFragment(withOpening: true, overload: true);

            var result = await RunColumn(fragment, gmshRoot, openSeesExecutable);

            Assert.False(result.IsConverged);
            Assert.Equal(FragmentAuditVerdict.Invalid, result.AuditReport.Verdict);
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_AnchorOutsideHull_BlocksBeforeOpenSeesInvocation()
    {
        string openSeesExecutable = OpenSeesTestExecutable.ResolveOrSkip();
        string gmshRoot = Path.Combine(
            Path.GetTempPath(), "opencs-multistory-column-e2e-anchor", Guid.NewGuid().ToString("N"));
        try
        {
            var fragment = BuildThreeLevelFragment(withOpening: false, overload: false);
            fragment.Levels[1].ColumnAnchorLocalXY = (100, 100);
            var invocationCounter = new CountingShellAnalysisRunner();

            var result = await RunColumn(fragment, gmshRoot, openSeesExecutable, invocationCounter);

            Assert.False(result.IsConverged);
            Assert.NotEmpty(result.AssemblyDiagnostics);
            Assert.Equal(0, invocationCounter.CallCount);
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    static async Task<MultiStoryColumnResult> RunColumn(
        MultiStoryColumnFragment fragment,
        string gmshRoot,
        string openSeesExecutable,
        IShellAnalysisRunner? analysisRunner = null)
    {
        var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
        {
            ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
            ArtifactRoot = gmshRoot
        });
        var meshSettings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);

        return await new MultiStoryColumnRunner(analysisRunner).RunAsync(
            fragment, mesher, level => meshSettings,
            LookupMaterial, CalcType.C, openSeesExecutable, CancellationToken.None);
    }

    /// <summary>3 перекрытия 4x4 м на Z=0/3/6, среднее — опционально с проёмом 1x2 м вне
    /// anchor-точки; заделка низа Fixed; 2 сегмента колонны (CrossSectionFixtures.RectangularSection,
    /// явный GJ); поверхностная нагрузка на всех плитах + осевая точечная нагрузка на верхнем
    /// anchor-узле (overload — на порядок больше).</summary>
    static MultiStoryColumnFragment BuildThreeLevelFragment(bool withOpening, bool overload)
    {
        var level1 = MakeLevel("level-1", 1, originZ: 0, withOpening: false);
        var level2 = MakeLevel("level-2", 2, originZ: 3, withOpening: withOpening);
        var level3 = MakeLevel("level-3", 3, originZ: 6, withOpening: false);

        double surfaceLoad = -5; // кПа (кН/м^2), собственный вес + отделка перекрытий
        foreach (var level in new[] { level1, level2, level3 })
            level.Loads.Add(new PlanarLoad
            {
                Tag = $"{level.Id}-surface",
                Kind = PlanarLoadKind.Surface,
                CoordinateSystem = PlanarLoadCoordinateSystem.Global,
                Components = new PlanarVector3(0, 0, surfaceLoad)
            });

        double axialLoad = overload ? -3_000_000 : -3_000; // кН, точечная нагрузка на колонну
        level3.Loads.Add(new PlanarLoad
        {
            Tag = "top-axial",
            Kind = PlanarLoadKind.Point,
            CoordinateSystem = PlanarLoadCoordinateSystem.Global,
            Components = new PlanarVector3(0, 0, axialLoad),
            PointU = 2,
            PointV = 2
        });

        var (section, _, _) = CrossSectionFixtures.RectangularSection();
        return new MultiStoryColumnFragment
        {
            FragmentId = 1000,
            Name = "Three-Level Column E2E",
            Levels = { level1, level2, level3 },
            Segments =
            {
                new ColumnSegment { Id = "seg-1", Section = section, GJ = 5000, Vecxz = (1, 0, 0) },
                new ColumnSegment { Id = "seg-2", Section = section, GJ = 5000, Vecxz = (1, 0, 0) }
            },
            BaseSupport = ColumnBaseFixity.Fixed,
            StageConfig = FragmentStageConfig.CreateDefault1Stage()
        };
    }

    /// <summary>Уровень 4x4 м; при withOpening — проём 1x2 м в углу (0.5..1.5, 0.5..2.5),
    /// не касающийся anchor-точки (2,2) и границ контура.</summary>
    static ColumnFloorLevel MakeLevel(string id, int regionId, double originZ, bool withOpening)
    {
        var contour = new Contour
        {
            Id = regionId * 10, Tag = $"{id}-contour", X = [0, 4, 4, 0], Y = [0, 0, 4, 4]
        };
        List<Contour> holes = [];
        if (withOpening)
            holes.Add(new Contour
            {
                Id = regionId * 10 + 1, Tag = $"{id}-opening", Type = ContourType.Hole,
                X = [0.5, 1.5, 1.5, 0.5], Y = [0.5, 0.5, 2.5, 2.5]
            });

        var region = PlanarRegion.CreateFromContour(
            contour, holes: holes,
            frame: new Frame3D(
                new PlanarVector3(0, 0, originZ), new PlanarVector3(1, 0, 0),
                new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1)),
            tag: id);
        region.Id = regionId;

        return new ColumnFloorLevel
        {
            Id = id,
            Name = id,
            PlateRegion = region,
            PlateSection = new PlateSection
            {
                H = 0.2,
                NLayers = 6,
                ConcreteMaterialId = 1,
                RebarMaterialId = 2,
                RebarLayers =
                [
                    new PlateRebarLayer { Asx = 0.0006, Asy = 0.0006, Zsx = 0.08, Zsy = 0.08, MaterialId = 2 },
                    new PlateRebarLayer { Asx = 0.0006, Asy = 0.0006, Zsx = -0.08, Zsy = -0.08, MaterialId = 2 }
                ]
            },
            ColumnAnchorLocalXY = (2, 2)
        };
    }

    // id 1/2 — shell-материалы плиты (ConcreteMaterialId/RebarMaterialId в MakeLevel);
    // id 10/20 — балочные материалы сегмента (CrossSectionFixtures.RectangularSection).
    static (Material Concrete, Material Rebar) ShellMaterials() =>
    (
        new Material { Id = 1, Tag = "B25", Type = MatType.Concrete,
            C = new MaterialChars { E = 30_000_000, Fc = -17_000, Ft = 1_150, Ec0 = -0.002, Ec2 = -0.0035 } },
        new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelF,
            C = new MaterialChars { E = 200_000_000, Ft = 355_000, Ru = 500_000, Et2 = 0.05 } }
    );

    static Material? LookupMaterial(int id)
    {
        if (id == 1) return ShellMaterials().Concrete;
        if (id == 2) return ShellMaterials().Rebar;
        var (_, concrete, steel) = CrossSectionFixtures.RectangularSection();
        return CrossSectionFixtures.Materials(concrete, steel).GetValueOrDefault(id);
    }

    sealed class CountingShellAnalysisRunner : IShellAnalysisRunner
    {
        public int CallCount { get; private set; }

        public Task<ShellAnalysisRunResult> RunAsync(
            ShellOpenSeesModel model, string executablePath, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ShellAnalysisRunResult(
                ShellAnalysisOutcome.Completed,
                new ShellResult { Steps = [], Status = "completed" },
                "artifacts", null));
        }
    }
}
