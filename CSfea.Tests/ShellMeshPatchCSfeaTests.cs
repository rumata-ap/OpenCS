using CScore;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using CSfea.CScoreBridge;
using OpenCS.Gmsh;

namespace CSfea.Tests;

public static class ShellMeshPatchCSfeaTests
{
    public static void RunAll()
    {
        TestHarness.Section("ShellMeshPatchPlateSectionResponse (CSfea): реальный Gmsh + линейный решатель");
        HomogeneousPatch_AxialState_MatchesPointwiseSnapshot().GetAwaiter().GetResult();
        ControlCheck_HomogeneousPatch_ReportsConsistentWithinTolerance().GetAwaiter().GetResult();

        TestHarness.Section("ShellMeshPatchPlateSectionResponse (CSfea): precondition-тесты через CreateAsync");
        AngledRebar_NotAtCenter_Blocked().GetAwaiter().GetResult();
        FlippedStripFrame_Rejected_ThroughCreateAsync().GetAwaiter().GetResult();
        PatchNearHullEdge_Outside_Blocked().GetAwaiter().GetResult();

        TestHarness.Section("ShellMeshPatchPlateSectionResponse (CSfea): RVE-convergence, независимый sweep RveSizeM × elementSize");
        RveConvergence_Csfea_IndependentSizeAndDensitySweep().GetAwaiter().GetResult();
    }

    static async Task RveConvergence_Csfea_IndependentSizeAndDensitySweep()
    {
        string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-shell-mesh-patch-convergence", Guid.NewGuid().ToString("N"));
        try
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: Frame3D.Identity);
            var section = new PlateSection { H = 0.2, NLayers = 20, TensionConcrete = true };
            var concrete = LinearElasticDiagram();
            var materials = new PlateSectionMaterials { ConcreteDiagram = concrete, RebarDiagram = concrete, ConcreteE_MPa = 30000.0 };
            var field = new PlateRebarField([], []);
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = gmshRoot
            });

            var pointwiseSnapshot = PlateSectionTangentSnapshot.Create(section, concrete, concrete);
            TestHarness.Check("Поточечный snapshot построен (convergence)", pointwiseSnapshot.IsCalculable, "");
            if (!pointwiseSnapshot.IsCalculable) return;

            double[] sizes = [1.0, 0.5];
            double[] elementSizes = [0.3, 0.15];
            var residuals = new Dictionary<(double Size, double ElementSize), double>();

            foreach (double size in sizes)
            foreach (double elementSize in elementSizes)
            {
                var buildResult = await ShellMeshPatchPlateSectionResponse.CreateAsync(
                    region, stripFrame: region.Frame, centerU: 2.0, centerV: 2.0, field, section, materials,
                    new ShellMeshPatchStateBounds(1e-4, 0.01), size,
                    new PlanarMeshSettings(elementSize, 6, PlanarMeshElementMode.Mixed), mesher);
                TestHarness.Check($"RVE построен (size={size}, elementSize={elementSize})", buildResult.IsCalculable, "");
                if (!buildResult.IsCalculable) continue;

                var state = new ShellStrainState(5e-5, 0, 0, 0, 0, 0);
                var rveForces = buildResult.Source!.Forces(state);
                var pointwiseForces = pointwiseSnapshot.Source!.Forces(state);
                residuals[(size, elementSize)] = Math.Abs(rveForces.Nx - pointwiseForces.Nx) / Math.Abs(pointwiseForces.Nx);
            }

            // Измельчение сетки при ФИКСИРОВАННОМ размере RVE не должно ухудшать невязку.
            if (residuals.ContainsKey((1.0, 0.3)) && residuals.ContainsKey((1.0, 0.15)))
                TestHarness.Check("Невязка не растёт при измельчении сетки (RveSizeM=1.0 фиксирован)",
                    residuals[(1.0, 0.15)] <= residuals[(1.0, 0.3)] * 1.5,
                    $"coarse={residuals[(1.0, 0.3)]:e3}, fine={residuals[(1.0, 0.15)]:e3}");

            TestHarness.Check("Итоговая невязка (минимальный RVE/сетка) в разумных пределах",
                residuals.Values.Count == 0 || residuals.Values.Min() < 0.05,
                $"min residual={(residuals.Count > 0 ? residuals.Values.Min() : double.NaN):e3}");
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    static async Task AngledRebar_NotAtCenter_Blocked()
    {
        string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-shell-mesh-patch-angled", Guid.NewGuid().ToString("N"));
        try
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: Frame3D.Identity);
            var section = new PlateSection { H = 0.2, NLayers = 4, TensionConcrete = true };
            var concrete = LinearElasticDiagram();
            var materials = new PlateSectionMaterials { ConcreteDiagram = concrete, RebarDiagram = concrete, ConcreteE_MPa = 30000.0 };
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = gmshRoot
            });

            // Зона с Angle=30 смещена от центра патча (2.0, 2.0) — не содержит центр, но
            // пересекает угол RVE-патча размером 1.0 м вокруг него. Проверка только по центру
            // пропустила бы этот случай.
            var zone = new RebarZone
            {
                Face = RebarFace.PlusN, Priority = 1, Operation = RebarZoneOperation.Add,
                Polygon = [new() { U = 2.3, V = 2.3 }, new() { U = 3.0, V = 2.3 }, new() { U = 3.0, V = 3.0 }, new() { U = 2.3, V = 3.0 }],
                Layout = new PlateRebarLayer { Asx = 0.001, Angle = 30.0 },
            };
            var field = new PlateRebarField(BaseLayout: [], Zones: [zone]);

            var result = await ShellMeshPatchPlateSectionResponse.CreateAsync(
                region, stripFrame: region.Frame, centerU: 2.0, centerV: 2.0, field, section, materials,
                new ShellMeshPatchStateBounds(1e-4, 0.01), rveSizeM: 1.0,
                new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed), mesher);

            TestHarness.Check("Angle≠0 вне центра всё равно блокирует построение адаптера", !result.IsCalculable, "");
            TestHarness.Check("Диагностика shell_mesh_patch_angled_rebar_unsupported присутствует",
                result.Diagnostics.Any(d => d.Code == "shell_mesh_patch_angled_rebar_unsupported"), "");
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    static async Task FlippedStripFrame_Rejected_ThroughCreateAsync()
    {
        string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-shell-mesh-patch-flipped", Guid.NewGuid().ToString("N"));
        try
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: Frame3D.Identity);
            var section = new PlateSection { H = 0.2, NLayers = 4, TensionConcrete = true };
            var concrete = LinearElasticDiagram();
            var materials = new PlateSectionMaterials { ConcreteDiagram = concrete, RebarDiagram = concrete, ConcreteE_MPa = 30000.0 };
            var field = new PlateRebarField([], []);
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = gmshRoot
            });
            var flippedFrame = new Frame3D(
                PlanarVector3.Zero,
                new PlanarVector3(-1, 0, 0),
                new PlanarVector3(0, -1, 0),
                new PlanarVector3(0, 0, 1));

            var result = await ShellMeshPatchPlateSectionResponse.CreateAsync(
                region, stripFrame: flippedFrame, centerU: 2.0, centerV: 2.0, field, section, materials,
                new ShellMeshPatchStateBounds(1e-4, 0.01), rveSizeM: 1.0,
                new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed), mesher);

            TestHarness.Check("Развёрнутый на 180° StripFrame блокирует построение адаптера через CreateAsync",
                !result.IsCalculable, "");
            TestHarness.Check("Диагностика shell_mesh_patch_frame_mismatch присутствует",
                result.Diagnostics.Any(d => d.Code == "shell_mesh_patch_frame_mismatch"), "");
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    static async Task PatchNearHullEdge_Outside_Blocked()
    {
        string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-shell-mesh-patch-outside", Guid.NewGuid().ToString("N"));
        try
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: Frame3D.Identity);
            var section = new PlateSection { H = 0.2, NLayers = 4, TensionConcrete = true };
            var concrete = LinearElasticDiagram();
            var materials = new PlateSectionMaterials { ConcreteDiagram = concrete, RebarDiagram = concrete, ConcreteE_MPa = 30000.0 };
            var field = new PlateRebarField([], []);
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = gmshRoot
            });

            // Центр в 0.2 м от края (x=0) — половина RVE-патча (0.5 м) выходит за Hull.
            var result = await ShellMeshPatchPlateSectionResponse.CreateAsync(
                region, stripFrame: region.Frame, centerU: 0.2, centerV: 2.0, field, section, materials,
                new ShellMeshPatchStateBounds(1e-4, 0.01), rveSizeM: 1.0,
                new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed), mesher);

            TestHarness.Check("RVE у края региона блокирует построение адаптера", !result.IsCalculable, "");
            TestHarness.Check("Диагностика shell_mesh_patch_outside_region присутствует",
                result.Diagnostics.Any(d => d.Code == "shell_mesh_patch_outside_region"), "");
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    static async Task ControlCheck_HomogeneousPatch_ReportsConsistentWithinTolerance()
    {
        string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-shell-mesh-patch-csfea-controlcheck", Guid.NewGuid().ToString("N"));
        try
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: Frame3D.Identity);
            var section = new PlateSection { H = 0.2, NLayers = 20, TensionConcrete = true };
            var concrete = LinearElasticDiagram();
            var materials = new PlateSectionMaterials { ConcreteDiagram = concrete, RebarDiagram = concrete, ConcreteE_MPa = 30000.0 };
            var field = new PlateRebarField([], []);
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = gmshRoot
            });

            // EquivalentSection строится из PlateSectionTangentSnapshot (Срез 2 путь, НЕ из RVE) —
            // widthSources контрольной проверки — из ShellMeshPatchPlateSectionResponse (Task 8).
            var strip = new PlateStripBeamAnalogy { ExplicitWidthM = 4.0, Geometry = new PlateStripGeometry { LengthM = 4.0 } };
            var pointwise = PlateSectionTangentSnapshot.Create(section, concrete, concrete);
            TestHarness.Check("Поточечный snapshot построен (control check)", pointwise.IsCalculable, "");
            if (!pointwise.IsCalculable) return;

            var buildEquiv = EquivalentSectionCalculator.Build(
                strip, pointwise.Source, widthSources: [pointwise.Source!, pointwise.Source!],
                ReductionPolicy.ConstitutiveIntegration, widthIntegrationPoints: 2);
            TestHarness.Check("EquivalentSection построен", buildEquiv.IsCalculable,
                string.Join("; ", buildEquiv.Diagnostics.Select(d => d.Message)));
            if (!buildEquiv.IsCalculable) return;

            // Два RVE-патча на двухточечной квадратуре Гаусса ширины 4 м. WidthGaussPoints
            // возвращает v ОТНОСИТЕЛЬНО осевой линии полосы (v=0 в центре ширины, диапазон
            // [-width/2, +width/2]) — абсолютная V-координата региона получается смещением на
            // центр региона по V (2.0, т.к. регион [0,4]×[0,4] с полосой по его середине).
            var (widthV, _) = EquivalentSectionCalculator.WidthGaussPoints(4.0, 2);
            const double regionCenterV = 2.0;
            var rve1 = await ShellMeshPatchPlateSectionResponse.CreateAsync(
                region, stripFrame: region.Frame, centerU: 2.0, centerV: regionCenterV + widthV[0], field, section, materials,
                new ShellMeshPatchStateBounds(1e-4, 0.01), 1.0,
                new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed), mesher);
            var rve2 = await ShellMeshPatchPlateSectionResponse.CreateAsync(
                region, stripFrame: region.Frame, centerU: 2.0, centerV: regionCenterV + widthV[1], field, section, materials,
                new ShellMeshPatchStateBounds(1e-4, 0.01), 1.0,
                new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed), mesher);
            TestHarness.Check("Оба RVE построены",
                rve1.IsCalculable && rve2.IsCalculable,
                string.Join("; ", rve1.Diagnostics.Concat(rve2.Diagnostics).Select(d => d.Message)));
            if (!rve1.IsCalculable || !rve2.IsCalculable) return;

            var beamState = new BeamStrainState(Eps0: 5e-5, KappaY: 0, KappaZ: 0);
            var check = EquivalentSectionControlCheck.Run(
                buildEquiv.Section, [rve1.Source!, rve2.Source!], beamState,
                relativeTolerance: 0.08, absoluteTolerance: 1e-6);

            TestHarness.Check("Контрольная проверка выполнена", check.IsCalculable, string.Join("; ", check.Diagnostics.Select(d => d.Message)));
            TestHarness.Check("Невязка в пределах допуска (однородный RVE ≈ поточечный snapshot)",
                check.IsConsistent, $"residual=[{string.Join(",", check.Residual)}]");
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    static async Task HomogeneousPatch_AxialState_MatchesPointwiseSnapshot()
    {
        string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-shell-mesh-patch-csfea", Guid.NewGuid().ToString("N"));
        try
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: Frame3D.Identity);

            var section = new PlateSection { H = 0.2, NLayers = 20, TensionConcrete = true };
            var concrete = LinearElasticDiagram();
            var materials = new PlateSectionMaterials { ConcreteDiagram = concrete, RebarDiagram = concrete, ConcreteE_MPa = 30000.0 };
            var field = new PlateRebarField([], []);

            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = gmshRoot
            });

            var buildResult = await ShellMeshPatchPlateSectionResponse.CreateAsync(
                region, stripFrame: region.Frame, centerU: 2.0, centerV: 2.0, field, section, materials,
                bounds: new ShellMeshPatchStateBounds(1e-4, 0.01),
                rveSizeM: 1.0,
                meshSettings: new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed),
                mesher: mesher);

            TestHarness.Check("Адаптер построен (нет блокирующих диагностик)",
                buildResult.IsCalculable, string.Join("; ", buildResult.Diagnostics.Select(d => d.Message)));
            if (!buildResult.IsCalculable) return;

            var snapshotResult = PlateSectionTangentSnapshot.Create(section, concrete, concrete);
            TestHarness.Check("Поточечный snapshot построен", snapshotResult.IsCalculable, "");
            if (!snapshotResult.IsCalculable) return;

            var state = new ShellStrainState(5e-5, 0, 0, 0, 0, 0);
            var rveForces = buildResult.Source!.Forces(state);
            var pointwiseForces = snapshotResult.Source!.Forces(state);

            double scale = Math.Max(Math.Abs(rveForces.Nx), Math.Abs(pointwiseForces.Nx));
            TestHarness.Check("Nx RVE ≈ Nx поточечного snapshot (однородный патч, чисто осевое состояние)",
                Math.Abs(rveForces.Nx - pointwiseForces.Nx) < 0.05 * scale,
                $"rve.Nx={rveForces.Nx:e6}, pointwise.Nx={pointwiseForces.Nx:e6}");
        }
        finally
        {
            if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
        }
    }

    /// <summary>E используется в том же внутреннем смысле, что PlateSection.Compute()/
    /// ComputeTangent() (Nx=E·eps·h без множителя ×1000) — по образцу
    /// CSfea.Tests/EquivalentBeamAnalogyE2ETests.cs. НЕ физически реалистичное значение E бетона
    /// (для этого использовалось бы 30e9/1000 в кПа) — намеренно: числа такого порядка (~3e7)
    /// делают шум ComputeTangent'а собственной FD-схемы (fdStep=1e-7 по умолчанию) сравнимым по
    /// величине с теоретически нулевыми элементами B-блока (mасштаб шума растёт линейно с E),
    /// из-за чего ShellMeshPatchPreflight ложно отклоняет заведомо линейный материал.</summary>
    internal static Diagramm LinearElasticDiagram()
    {
        const double e = 30_000.0;
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = e, Ry = e / 50, Ru = e / 50, Ft = e / 50, Fc = -e / 50,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = e, Type = MatType.ReSteelF, Tag = "shell-mesh-patch-linear" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.C];
    }
}
