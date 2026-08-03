using System.Collections.Generic;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using Xunit;

namespace CScore.Tests.Planar
{
    public class FloorJunctionDomainTests
    {
        [Fact]
        public void CreationAndProperties_AreSettable()
        {
            var plate = PlanarRegion.CreateFromContour(
                new Contour { Id = 1, Tag = "plate", X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
                frame: Frame3D.Identity, tag: "plate");
            plate.Id = 10;
            var wall = PlanarRegion.CreateFromContour(
                new Contour { Id = 2, Tag = "wall", X = [0, 4, 4, 0], Y = [0, 0, 3, 3] },
                frame: new Frame3D(
                    new PlanarVector3(2, 0, 0), new PlanarVector3(0, 1, 0),
                    new PlanarVector3(0, 0, 1), new PlanarVector3(1, 0, 0)),
                tag: "wall");
            wall.Id = 20;
            var connection = new PlanarConnection
            {
                Id = 7,
                MeshMode = PlanarConnectionMeshMode.ConformingPartition,
                SideA = new ConnectionLocus(10, [new PlanarPoint2D(2, 0), new PlanarPoint2D(2, 4)]),
                SideB = new ConnectionLocus(20, [new PlanarPoint2D(0, 0), new PlanarPoint2D(4, 0)])
            };

            var fragment = new FloorJunctionFragment
            {
                FragmentId = 1,
                Name = "Junction 1",
                PlateRegion = plate,
                WallRegion = wall,
                PlateSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                WallSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                Connection = connection,
                StageConfig = FragmentStageConfig.CreateDefault1Stage()
            };

            Assert.Equal(1, fragment.FragmentId);
            Assert.Equal("Junction 1", fragment.Name);
            Assert.Same(plate, fragment.PlateRegion);
            Assert.Same(wall, fragment.WallRegion);
            Assert.Equal(10, fragment.PlateRegion.Id);
            Assert.Equal(20, fragment.WallRegion.Id);
            Assert.Same(connection, fragment.Connection);
            Assert.Equal(7, fragment.Connection.Id);
            Assert.Equal(PlanarConnectionMeshMode.ConformingPartition, fragment.Connection.MeshMode);
            Assert.Equal(10, fragment.Connection.SideA.RegionId);
            Assert.Equal(20, fragment.Connection.SideB.RegionId);
            Assert.NotNull(fragment.PlateSection);
            Assert.NotNull(fragment.WallSection);
            Assert.Single(fragment.StageConfig.Stages);
            Assert.Equal(1, fragment.StageConfig.Stages[0].StageIndex);
            Assert.NotNull(fragment.Boundaries);
            Assert.Empty(fragment.Boundaries);
            Assert.NotNull(fragment.BoundaryTemplates);
            Assert.Empty(fragment.BoundaryTemplates);
        }

        [Fact]
        public void Validate_AcceptsValidAggregateWithoutErrors()
        {
            var fragment = BuildValidFragment();

            var diagnostics = fragment.Validate();

            Assert.DoesNotContain(diagnostics, d => d.IsError);
        }

        [Fact]
        public void Validate_RejectsEqualRegionIds()
        {
            var fragment = BuildValidFragment();
            fragment.WallRegion = fragment.PlateRegion; // один и тот же объект/ID

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_region_mismatch");
        }

        [Fact]
        public void Validate_RejectsNonConformingMeshMode()
        {
            var fragment = BuildValidFragment();
            fragment.Connection.MeshMode = PlanarConnectionMeshMode.EmbeddedLocus;

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_mesh_mode_unsupported");
        }

        [Fact]
        public void Validate_RejectsConnectionSidesNotReferencingRegions()
        {
            var fragment = BuildValidFragment();
            fragment.Connection.SideB = new ConnectionLocus(999, fragment.Connection.SideB.Points);

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_region_mismatch");
        }

        [Fact]
        public void Validate_RejectsDuplicateBoundaryIds()
        {
            var fragment = BuildValidFragment();
            fragment.Boundaries.Add(fragment.Boundaries[0]); // одинаковый Id

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_duplicate_id");
        }

        [Fact]
        public void Validate_RejectsBoundaryOnUnknownRegion()
        {
            var fragment = BuildValidFragment();
            fragment.Boundaries[0].RegionId = 777;

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_unknown_region");
        }

        [Fact]
        public void Validate_RejectsBoundaryReferencingJunctionAsCut()
        {
            var fragment = BuildValidFragment();
            var cut = fragment.Boundaries[0].Cut;
            fragment.Boundaries[0].Cut = new PlanarCutInterface
            {
                Id = cut.Id,
                Kind = cut.Kind,
                Geometry = cut.Geometry,
                NormalFromFragmentToOmittedSide = cut.NormalFromFragmentToOmittedSide,
                Frame = cut.Frame,
                ModeByDof = cut.ModeByDof,
                MeshConstraintId = "connection:7:region:10",
                BoundaryKey = cut.BoundaryKey,
                OmittedSideReference = cut.OmittedSideReference,
                ToleranceM = cut.ToleranceM
            };

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_uses_junction");
        }

        [Fact]
        public void Validate_RejectsBoundaryWithoutCutMapping()
        {
            var fragment = BuildValidFragment();
            fragment.Boundaries[0].Cut = null!; // намеренно: валидатор должен вернуть mapping_missing

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_mapping_missing");
        }

        [Fact]
        public void Validate_RejectsMissingBoundaryTemplate()
        {
            var fragment = BuildValidFragment();
            fragment.BoundaryTemplates.Clear();

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_template_missing");
        }

        [Fact]
        public void Validate_RejectsTemplateWithoutBoundary()
        {
            var fragment = BuildValidFragment();
            fragment.BoundaryTemplates["orphan"] = new PlanarBoundaryActionSet
                { SourceMode = PlanarBoundaryActionSourceMode.Template };

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_template_missing");
        }

        [Fact]
        public void Validate_RejectsMissingConnection()
        {
            var fragment = BuildValidFragment();
            fragment.Connection = null!; // намеренно: валидатор должен вернуть connection_missing

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_connection_missing");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void Validate_RejectsNonPositivePlateRegionId(int id)
        {
            var fragment = BuildValidFragment();
            fragment.PlateRegion.Id = id;

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_region_mismatch");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void Validate_RejectsNonPositiveWallRegionId(int id)
        {
            var fragment = BuildValidFragment();
            fragment.WallRegion.Id = id;

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_region_mismatch");
        }

        [Fact]
        public void Validate_OrphanTemplateDiagnosticsAreOrdinalOrdered()
        {
            var fragment = BuildValidFragment();
            // Вставляем orphan-ключи в порядке, отличном от Ordinal-сортировки.
            foreach (var key in new[] { "z", "a", "m" })
                fragment.BoundaryTemplates[key] = new PlanarBoundaryActionSet
                    { SourceMode = PlanarBoundaryActionSourceMode.Template };

            var diagnostics = fragment.Validate();

            var orphanMessages = diagnostics
                .Where(d => d.Code == "floor_junction_boundary_template_missing")
                .Select(d => d.Message)
                .ToArray();
            Assert.Equal(new[]
            {
                "Template 'a' не соответствует ни одному boundary.",
                "Template 'm' не соответствует ни одному boundary.",
                "Template 'z' не соответствует ни одному boundary.",
            }, orphanMessages);
        }

        [Fact]
        public void Audit_WithNoBlockingDiagnosticsAndConverged_ReturnsValid()
        {
            var fragment = BuildValidFragment();
            var result = new FloorJunctionResult { FragmentId = 1, IsConverged = true };

            var report = new FloorJunctionAuditReport().Audit(fragment, result);

            Assert.Equal(FragmentAuditVerdict.Valid, report.Verdict);
            Assert.Empty(report.Issues);
        }

        [Fact]
        public void Audit_WithNullResult_ReturnsInvalid()
        {
            var fragment = BuildValidFragment();

            var report = new FloorJunctionAuditReport().Audit(fragment, null!);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.NotEmpty(report.Issues);
        }

        [Fact]
        public void Audit_WithMeshDiagnostics_ReturnsInvalid()
        {
            var fragment = BuildValidFragment();
            var result = new FloorJunctionResult { FragmentId = 1, IsConverged = true };
            result.MeshDiagnostics.Add("planar_connection_conforming_partition_mismatch: узлы не совпадают.");

            var report = new FloorJunctionAuditReport().Audit(fragment, result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.NotEmpty(report.Issues);
        }

        [Fact]
        public void Audit_WithIncompleteAnalysis_ReturnsInvalid()
        {
            var fragment = BuildValidFragment();
            var result = new FloorJunctionResult { FragmentId = 1, IsConverged = false };
            result.AnalysisDiagnostics.Add("floor_junction_analysis_incomplete: LoadFactor=0.5.");

            var report = new FloorJunctionAuditReport().Audit(fragment, result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
        }

        [Fact]
        public void Result_DefaultAuditReportIsNotValid()
        {
            var result = new FloorJunctionResult { FragmentId = 1 };

            Assert.Equal(FragmentAuditVerdict.Invalid, result.AuditReport.Verdict);
            Assert.Empty(result.AuditReport.Issues);
        }

        [Fact]
        public void GetFingerprint_IsDeterministic()
        {
            var fragment = BuildValidFragment();
            var settings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);

            var first = fragment.GetFingerprint(settings, settings);
            var second = fragment.GetFingerprint(settings, settings);

            Assert.Equal(first, second);
            Assert.NotEmpty(first);
        }

        [Fact]
        public void GetFingerprint_ChangesWhenSectionOrConnectionOrSettingsChange()
        {
            var fragment = BuildValidFragment();
            var settings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);
            var baseline = fragment.GetFingerprint(settings, settings);

            fragment.PlateSection = new PlateSection { H = 0.3, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 };
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));

            fragment.PlateSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 };
            fragment.Connection.MatchingToleranceM = 1e-6;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));

            fragment.Connection.MatchingToleranceM = 1e-8;
            var otherSettings = new PlanarMeshSettings(0.4, 6, PlanarMeshElementMode.Quads);
            Assert.NotEqual(baseline, fragment.GetFingerprint(otherSettings, settings));
        }

        [Fact]
        public void GetFingerprint_IsIndependentOfBoundaryAndTemplateOrder()
        {
            var fragment = BuildValidFragment();
            fragment.Boundaries.Add(new FloorJunctionBoundary
            {
                Id = "wall-side",
                RegionId = 20,
                Cut = new PlanarCutInterface
                {
                    Id = "wall-side",
                    Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
                        [new PlanarPoint2D(0, 0), new PlanarPoint2D(4, 0)]),
                    NormalFromFragmentToOmittedSide = new PlanarVector3(0, -1, 0),
                    BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 3, 0),
                    ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.PreserveSupport)
                }
            });
            fragment.BoundaryTemplates["wall-side"] = new PlanarBoundaryActionSet
                { SourceMode = PlanarBoundaryActionSourceMode.Template };
            var settings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);
            var ordered = fragment.GetFingerprint(settings, settings);

            fragment.Boundaries.Reverse();
            var templates = fragment.BoundaryTemplates;
            var reversedKeys = templates.Keys.OrderByDescending(k => k).ToArray();
            fragment.BoundaryTemplates = new Dictionary<string, PlanarBoundaryActionSet>();
            foreach (var key in reversedKeys)
                fragment.BoundaryTemplates[key] = templates[key];

            Assert.Equal(ordered, fragment.GetFingerprint(settings, settings));
        }

        [Fact]
        public void GetFingerprint_ChangesWhenSectionConstitutiveParametersChange()
        {
            var fragment = BuildValidFragment();
            var settings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);
            var baseline = fragment.GetFingerprint(settings, settings);

            // TensionConcrete
            fragment.PlateSection.TensionConcrete = true;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            fragment.PlateSection.TensionConcrete = false;

            // SofteningModel
            fragment.PlateSection.SofteningModel = "vecchio_collins";
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            fragment.PlateSection.SofteningModel = "";

            // SofteningEpsC2
            fragment.PlateSection.SofteningEpsC2 = 0.0025;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            fragment.PlateSection.SofteningEpsC2 = 0.002;

            // PlateModel
            fragment.PlateSection.PlateModel = "char1d_principal";
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            fragment.PlateSection.PlateModel = "layered";

            // ConcreteDiagramType
            fragment.PlateSection.ConcreteDiagramType = DiagrammType.L2;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
        }

        [Fact]
        public void GetFingerprint_ChangesWhenWallSectionConstitutiveParametersChange()
        {
            var fragment = BuildValidFragment();
            var settings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);
            var baseline = fragment.GetFingerprint(settings, settings);

            fragment.WallSection.SofteningModel = "vecchio_collins";

            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
        }

        [Fact]
        public void GetFingerprint_ChangesWhenStageParametersChange()
        {
            var fragment = BuildValidFragment();
            var settings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);
            var baseline = fragment.GetFingerprint(settings, settings);
            var stage = fragment.StageConfig.Stages[0];

            // StageIndex
            stage.StageIndex = 2;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            stage.StageIndex = 1;

            // Name
            stage.Name = "Renamed stage";
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            stage.Name = "Full Monotonic Stage";

            // SurfaceLoadScale
            stage.SurfaceLoadScale = 0.75;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            stage.SurfaceLoadScale = 1.0;

            // CutInterfaceScale
            stage.CutInterfaceScale = 0.5;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            stage.CutInterfaceScale = 1.0;
        }

        [Fact]
        public void GetFingerprint_ChangesWhenSolverParametersChange()
        {
            var fragment = BuildValidFragment();
            var settings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);
            var baseline = fragment.GetFingerprint(settings, settings);
            var solver = fragment.StageConfig.Stages[0].Solver;

            // Algorithm
            solver.Algorithm = "Newton";
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            solver.Algorithm = "NewtonWithLineSearch";

            // EnergyTolerance
            solver.EnergyTolerance = 1e-7;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            solver.EnergyTolerance = 1e-6;

            // UnbalanceTolerance
            solver.UnbalanceTolerance = 1e-5;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            solver.UnbalanceTolerance = 1e-4;

            // MaxIterations
            solver.MaxIterations = 50;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            solver.MaxIterations = 40;

            // InitialStep
            solver.InitialStep = 0.05;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            solver.InitialStep = 0.1;

            // MinStep
            solver.MinStep = 1e-6;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            solver.MinStep = 1e-5;

            // MaxStep
            solver.MaxStep = 0.1;
            Assert.NotEqual(baseline, fragment.GetFingerprint(settings, settings));
            solver.MaxStep = 0.2;
        }

        [Fact]
        public void GetFingerprint_NullSolverIsHandledDeterministically()
        {
            var fragment = BuildValidFragment();
            fragment.StageConfig.Stages[0].Solver = null!;
            var settings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);

            var first = fragment.GetFingerprint(settings, settings);
            var second = fragment.GetFingerprint(settings, settings);

            Assert.Equal(first, second);
            Assert.NotEmpty(first);
        }

        [Fact]
        public void Result_ContainsExpectedSections()
        {
            var result = new FloorJunctionResult { FragmentId = 7 };
            result.InterfaceContinuity.Add(new InterfaceContinuityItem(5, 11, 1e-9, 1e-10));
            result.ForceBalance = new FloorJunctionForceBalance(8000, 8000.0001, 1e-8);
            result.ProvenanceMap[5] = "plate|source:concrete:1";

            Assert.False(result.IsConverged);
            Assert.Single(result.InterfaceContinuity);
            Assert.Equal(8000, result.ForceBalance!.AppliedLoadMagnitude);
            Assert.Equal("plate|source:concrete:1", result.ProvenanceMap[5]);
        }

        static FloorJunctionFragment BuildValidFragment()
        {
            var plate = PlanarRegion.CreateFromContour(
                new Contour { Id = 1, Tag = "plate", X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
                frame: Frame3D.Identity, tag: "plate");
            plate.Id = 10;
            var wall = PlanarRegion.CreateFromContour(
                new Contour { Id = 2, Tag = "wall", X = [0, 4, 4, 0], Y = [0, 0, 3, 3] },
                frame: new Frame3D(
                    new PlanarVector3(2, 0, 0), new PlanarVector3(0, 1, 0),
                    new PlanarVector3(0, 0, 1), new PlanarVector3(1, 0, 0)),
                tag: "wall");
            wall.Id = 20;
            var fragment = new FloorJunctionFragment
            {
                FragmentId = 1,
                Name = "Junction 1",
                PlateRegion = plate,
                WallRegion = wall,
                PlateSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                WallSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                Connection = new PlanarConnection
                {
                    Id = 7,
                    MeshMode = PlanarConnectionMeshMode.ConformingPartition,
                    SideA = new ConnectionLocus(10, [new PlanarPoint2D(2, 0), new PlanarPoint2D(2, 4)]),
                    SideB = new ConnectionLocus(20, [new PlanarPoint2D(0, 0), new PlanarPoint2D(4, 0)])
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
                        [new PlanarPoint2D(0, 0), new PlanarPoint2D(0, 4)]),
                    NormalFromFragmentToOmittedSide = new PlanarVector3(-1, 0, 0),
                    BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 3, 0),
                    ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.PreserveSupport)
                }
            });
            fragment.BoundaryTemplates["plate-fix"] = new PlanarBoundaryActionSet
                { SourceMode = PlanarBoundaryActionSourceMode.Template };
            return fragment;
        }
    }
}
