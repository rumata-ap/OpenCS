using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using OpenCS.Gmsh;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>Реальный end-to-end прогон floor junction «плита + стена» через внешние Gmsh и
/// OpenSees. В отличие от FloorJunctionRunnerTests (fake mesher + injected analysis runner)
/// здесь используется production-конвейер целиком: real GmshPlanarMesher, ConformingPartition
/// двухстороннего workflow, FloorJunctionShellModelAssembler, boundary pipeline и
/// FloorJunctionRunner с default (concrete) ShellAnalysisRunner. Проверяются не только
/// пустые диагностики и сходимость, но и жёсткие критерии качества результата:
/// непрерывность интерфейса (1e-6 м / 1e-6 рад), силовой баланс (0.1%), boundary unbalance
/// (0.1%) и Valid-вердикт аудита.</summary>
public sealed class FloorJunctionIntegrationTests
{
    [Fact]
    public async Task EndToEnd_PlateWallJunction_RunsRealGmshAndOpenSees_PassesAllChecks()
    {
        string openSeesExecutable = OpenSeesTestExecutable.ResolveOrSkip();
        string gmshRoot = Path.Combine(
            Path.GetTempPath(), "opencs-floor-junction-e2e", Guid.NewGuid().ToString("N"));
        try
        {
            var fragment = BuildPlateWallJunctionFragment();
            FloorJunctionResult result = await RunJunction(fragment, gmshRoot, openSeesExecutable);

            // Preflight-конвейер не должен оставить ни одной blocking-диагностики.
            Assert.Empty(result.MeshDiagnostics);
            Assert.Empty(result.AssemblyDiagnostics);
            Assert.Empty(result.BoundaryDiagnostics);
            Assert.Empty(result.AnalysisDiagnostics);

            // Полная нагрузка достигнута последним сошедшимся шагом нелинейного расчёта.
            Assert.True(result.IsConverged,
                "Реальный нелинейный расчёт плита+стена должен сойтись при полной нагрузке.");

            // Аудит не должен фиксировать ни одной блокирующей проблемы.
            Assert.Equal(FragmentAuditVerdict.Valid, result.AuditReport.Verdict);

            // Непрерывность интерфейса: по каждой junction-паре есть запись, и дельты
            // перемещений/вращений не превышают жёсткие 1e-6 (equalDOF master->slave).
            Assert.NotEmpty(result.InterfaceContinuity);
            Assert.All(result.InterfaceContinuity, item =>
            {
                Assert.True(item.MaxDisplacementDeltaM < 1e-6,
                    $"Junction-пара {item.PlateNodeTag}->{item.WallNodeTag}: |ΔU|={item.MaxDisplacementDeltaM:G6} м >= 1e-6.");
                Assert.True(item.MaxRotationDeltaRad < 1e-6,
                    $"Junction-пара {item.PlateNodeTag}->{item.WallNodeTag}: |ΔR|={item.MaxRotationDeltaRad:G6} рад >= 1e-6.");
            });

            // Силовой баланс финального шага: приложенная нагрузка и реакции ненулевые,
            // относительная невязка в пределах 0.1%.
            Assert.NotNull(result.ForceBalance);
            Assert.True(result.ForceBalance!.AppliedLoadMagnitude > 0,
                "Приложенная нагрузка финального шага должна быть ненулевой.");
            Assert.True(result.ForceBalance.ReactionMagnitude > 0,
                "Суммарные реакции должны быть ненулевыми.");
            Assert.True(result.ForceBalance.RelativeUnbalance < 1e-3,
                $"Относительная невязка баланса {result.ForceBalance.RelativeUnbalance * 100:F3}% превышает допуск 0.1%.");

            // Сборка: provenance материалов/секций и junction-пары обязаны быть непустыми.
            Assert.NotEmpty(result.ProvenanceMap);
            Assert.NotEmpty(result.JunctionPairs);

            // Boundary pipeline: mapped нагрузки сходятся с applied в пределах 0.1%.
            Assert.True(result.BoundaryForceUnbalanceRatio <= 1e-3,
                $"Boundary unbalance ratio {result.BoundaryForceUnbalanceRatio * 100:F3}% превышает допуск 0.1%.");

            // Gmsh-артефакты: реальный GmshPlanarMesher всегда пишет файлы в каталог,
            // фиксируемый в Provenance.ArtifactDirectory plate snapshot-а.
            Assert.False(string.IsNullOrEmpty(result.GmshArtifactDirectory),
                "Реальный Gmsh-конвейер должен заполнить GmshArtifactDirectory.");
        }
        finally
        {
            // Уникальный каталог артефактов Gmsh удаляется всегда; finally не должен
            // маскировать падение ассертов в теле try.
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    static async Task<FloorJunctionResult> RunJunction(
        FloorJunctionFragment fragment, string gmshRoot, string openSeesExecutable)
    {
        var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
        {
            ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
            ArtifactRoot = gmshRoot
        });
        var (concrete, rebar) = NonlinearMaterials();
        var lookup = new Dictionary<int, Material> { [1] = concrete, [2] = rebar };
        // Идентичные mesh settings обеих сторон — единственный способ получить согласованное
        // разбиение независимых embedded curves (см. Risks #5 FloorJunctionMeshingTests).
        var meshSettings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);

        // Production runner без инъекций: default (concrete) ShellAnalysisRunner.
        return await new FloorJunctionRunner().RunAsync(
            fragment, mesher, meshSettings, meshSettings,
            id => lookup.GetValueOrDefault(id), CalcType.C,
            openSeesExecutable, CancellationToken.None);
    }

    /// <summary>Перпендикулярная пара «плита 4x4 м в z=0 + стена 4x2 м в x=2» с явным
    /// Frame3D, ConformingPartition junction вдоль y=1..3, fix на кромке плиты x=0 и
    /// вертикальной силой Z=-1500 Н/м на верхней кромке стены (V=1).</summary>
    static FloorJunctionFragment BuildPlateWallJunctionFragment()
    {
        var plate = PlanarRegion.CreateFromContour(
            new Contour { Id = 1, Tag = "plate", X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
            frame: Frame3D.Identity, tag: "plate");
        plate.Id = 10;
        var wall = PlanarRegion.CreateFromContour(
            new Contour { Id = 2, Tag = "wall", X = [0, 4, 4, 0], Y = [-1, -1, 1, 1] },
            frame: new Frame3D(new(2, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0)),
            tag: "wall");
        wall.Id = 20;

        var fragment = new FloorJunctionFragment
        {
            FragmentId = 1,
            Name = "Plate-Wall Junction E2E",
            PlateRegion = plate,
            WallRegion = wall,
            PlateSection = JunctionSection(),
            WallSection = JunctionSection(),
            Connection = new PlanarConnection
            {
                Id = 7,
                MeshMode = PlanarConnectionMeshMode.ConformingPartition,
                MatchingToleranceM = 1e-8,
                SideA = new ConnectionLocus(10, [new(2, 1), new(2, 3)]),
                SideB = new ConnectionLocus(20, [new(1, 0), new(3, 0)])
            },
            StageConfig = FragmentStageConfig.CreateDefault1Stage()
        };
        fragment.Boundaries.Add(new FloorJunctionBoundary
        {
            Id = "plate-fix",
            RegionId = 10,
            Cut = new PlanarCutInterface
            {
                Id = "plate-fix",
                Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
                    [new(0, 0), new(0, 4)]),
                NormalFromFragmentToOmittedSide = new(-1, 0, 0),
                BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 3, 0),
                ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.PreserveSupport)
            }
        });
        fragment.Boundaries.Add(new FloorJunctionBoundary
        {
            Id = "wall-top",
            RegionId = 20,
            Cut = new PlanarCutInterface
            {
                Id = "wall-top",
                Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
                    [new(4, 1), new(0, 1)]),
                NormalFromFragmentToOmittedSide = new(0, 0, 1),
                BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 2, 3),
                ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Force)
            }
        });
        fragment.BoundaryTemplates["plate-fix"] = new PlanarBoundaryActionSet
        {
            SourceMode = PlanarBoundaryActionSourceMode.Template
        };
        fragment.BoundaryTemplates["wall-top"] = new PlanarBoundaryActionSet
        {
            SourceMode = PlanarBoundaryActionSourceMode.Template,
            ForceActions =
            [
                new PlanarBoundaryForceAction
                {
                    InterfaceId = "wall-top",
                    DofMask = PlanarDofMask.UZ,
                    Frame = Frame3D.Identity,
                    Samples =
                    [
                        new(0, new PlanarVector3(0, 0, -1500), PlanarVector3.Zero),
                        new(1, new PlanarVector3(0, 0, -1500), PlanarVector3.Zero)
                    ]
                }
            ]
        };
        return fragment;
    }

    /// <summary>Слоистая нелинейная секция плиты/стены: 6 слоёв бетона + верхняя и нижняя
    /// арматура (как в VerticalPlanarFragmentIntegrationTests).</summary>
    static PlateSection JunctionSection() => new()
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
    };

    static (Material Concrete, Material Rebar) NonlinearMaterials() =>
    (
        new Material { Id = 1, Tag = "B25", Type = MatType.Concrete,
            C = new MaterialChars { E = 30_000_000, Fc = -17_000, Ft = 1_150, Ec0 = -0.002, Ec2 = -0.0035 } },
        new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelF,
            C = new MaterialChars { E = 200_000_000, Ft = 355_000, Ru = 500_000, Et2 = 0.05 } }
    );
}
