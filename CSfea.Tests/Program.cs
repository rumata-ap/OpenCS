using CSfea.Tests;

Console.WriteLine("CSfea — проверки порта GreenSectionPy/fea");
Console.WriteLine(TestHarness.IncludeSlowTests
    ? "Режим: полный (CSFEA_SLOW=1) — включает R60 parity"
    : "Режим: быстрый — R60 parity пропущен; CSFEA_SLOW=1 для полного прогона");

ShellTests.RunElementChecks();
ShellTests.RunClampedPlateLinear();
ShellTests.RunVonKarman();

CrShellTests.RunRigidRotation();
CrShellTests.RunAgreementWithVonKarman();

BeamTests.RunLinearCantilever2D();
BeamTests.RunCrRollup2D();
BeamTests.RunLinearCantilever3D();
BeamTests.RunCrRollup3D();

SolverTests.RunCrossValidation();

CScoreBridgeTests.RunAll();
EquivalentBeamResponseTests.RunAll();
EquivalentBeamAnalogyE2ETests.RunAll();

FireCurvesTests.RunAll();

Sp468TablesTests.RunAll();

FireRebarClassResolverTests.RunAll();

FireMeshStepValidatorTests.RunAll();

FireInputSnapshotTests.RunAll();

FireMeshBuilderTests.RunAll();

FireThermalServiceTests.RunAll();

// TODO(fire-parity): отключено — нет фикстура tools/fire-parity/fixtures/rectangle_200x400_5min_3sided.json.
// После восстановления фикстура эталоны придётся пересчитать: с 2026-08-26 действуют
// таблицы 5.1/5.6 СП 468 с Изм. № 1 и исключение растянутого бетона по п. 8.42.
// FireParityTests.RunAll();

FireFiberSectionTests.RunAll();

FireRCheckTests.RunAll();

FireMvpVsFiberTests.RunAll();

FireRTimeTests.RunAll();

FireTemperatureProfileTests.RunAll();

FireCompressionZoneTests.RunAll();

FireThermalCurvatureTests.RunAll();

FireRParityTests.RunAll();

CustomDiagramTests.RunAll();

HeatMaterialTests.RunAll();

Sp468MaterialsTests.RunAll();

HeatTri3Tests.RunAll();

HeatTri6Tests.RunAll();

HeatMeshTests.RunAll();

HeatMeshQuadraticTests.RunAll();

FireT6ParityTests.RunAll();

HeatSteadyTests.RunAll();

HeatBoundaryTests.RunAll();

HeatTransientTests.RunAll();

LimitForceSolverTests.RunAll();

PlateModelTests.RunAll();

PlateRebarFieldShellResponseFactoryTests.RunAll();

PlanarMeshSnapshotShellMeshAdapterTests.RunAll();
PlanarLoadShellMeshAdapterTests.RunAll();
PlanarBoundaryActionShellMeshAdapterTests.RunAll();

PlanarMeshCSfeaPatchTests.RunAll();

LinearDirichletSystemTests.RunAll();

Shell3Tests.RunAll();

ShellMeshPatchPostprocessorTests.RunAll();

ShellMeshPatchCSfeaTests.RunAll();

ShellStrainSolverTests.RunAll();

BucklingTests.RunSimplySupportedPlate();

SparseOrderingTests.RunAll();

SparseCholeskyTests.RunAll();

HeatAssemblyTests.RunAll();

ThermalBenchmark.RunAll();

SteelSectionTests.RunGeoPropsDirect();
SteelSectionTests.RunIBeamProperties();
SteelSectionTests.RunPlasticModulusRectangle();
SteelSectionTests.RunPlasticModulusIBeam();
SteelCheckerTests.RunSimpleCompressionCheck();

SteelClassifierTests.RunAll();
SteelStrengthTests.RunAll();
SteelStabilityTests.RunAll();

FemCheckRunnerTests.RunExtractCalcType();
FemCheckRunnerTests.RunExtractWorstDetail();
FemCheckRunnerTests.RunExtractWorstDetailNoDetails();
FemCheckRunnerTests.RunLayeredSlsAcrc();
FemCheckRunnerTests.RunLayeredSlsThreeComponent();
FemCheckRunnerTests.RunLayeredSlsLtFraction();
FemCheckRunnerTests.RunMultiAcceptsSingleElementTarget();

FemInfraTests.RunAll();

LiraCsvSchemaParserTests.RunAll();

ScadTextParserTests.RunAll();

TorsionTests.SmokePropsConstruction();
TorsionTests.BoundaryFromMaterialArea();
TorsionTests.PrandtlTri3ElementMatrices();
TorsionTests.MeshBuilderSquare();
TorsionTests.MeshBuilderSquareWithHoleRuppert();
TorsionTests.MeshBuilderFromMaterialAreaMeters();
TorsionTests.MeshBuilderConcaveFrameFine();
TorsionTests.FemCircleItVsAnalytical();
TorsionTests.BoundaryDiscretizeLoops();
TorsionTests.BemKernelSlintcDiagonal();
TorsionTests.BemCircleItVsAnalytical();
TorsionTests.CrossValidationBemVsFem();
TorsionTests.ConvergenceByElementSize();
TorsionTests.RectangleTimoshenko();
TorsionTests.HollowBoxBredt();
TorsionTests.FemHollowCircleItVsExact();
TorsionTests.BemHollowBoxBredt();
TorsionTests.BemHollowCircleItVsExact();
TorsionTests.MinEdgeLengthSquareWithHole();
TorsionTests.MinEdgeLengthCircleApprox();
TorsionTests.MinEdgeLengthIgnoresDegenerateEdges();
TorsionTests.RichardsonExtrapolateMonotonicSeries();
TorsionTests.RichardsonExtrapolateAlreadyConverged();
TorsionTests.RichardsonExtrapolateNonMonotonicSeries();
TorsionTests.RichardsonAutoConvergeConcaveFrame();
TorsionTests.RichardsonBuildRunSizes();
TorsionTests.RichardsonAutoConvergeCustomH0AndTwoRuns();
TorsionTests.RichardsonAutoConvergeParallelMatchesSequentialIt();

TorsionTests.PrandtlTri6ShapeFunctionsPartitionOfUnity();
TorsionTests.PrandtlTri6AreaMatchesTri3();
TorsionTests.PrandtlTri6ElementKSymmetricPositiveDiagonalZeroRowSum();
TorsionTests.PrandtlTri6LoadAndMassVectors();
TorsionTests.PrandtlTri6NodeGradientReproducesLinearField();

TorsionTests.MeshBuilderPromoteSquareNodeCount();
TorsionTests.MeshBuilderPromoteClassifiesBoundaryMidNodes();
TorsionTests.MeshBuilderPromoteRejectsAlreadyQuadratic();

TorsionTests.FemCircleItVsAnalyticalQuadratic();
TorsionTests.RectangleTimoshenkoQuadratic();
TorsionTests.FemHollowCircleItVsExactQuadratic();
TorsionTests.TorsionSolverFemOrderDefaultIsLinear();

TorsionTests.ConvergenceOrderT3VsT6();
TorsionTests.FemT6ConcaveFrameSolvesWithinTimeout();

TorsionTests.FemShearCenterRectangleSymmetricAtCentroid();
TorsionTests.GeoMomentsChannelSymmetricIxyIsZero();
TorsionTests.FemShearCenterChannelVsBem();

TorsionTests.FemWarpingConstantCircleIsZero();
TorsionTests.FemShearUnitFieldsEquilibriumRectangle();

TorsionTests.FemTauUnitFieldXyMatchesMagnitude();
TorsionTests.CombinedStressSigmaZzPureAxial();
TorsionTests.CombinedStressSigmaZzPureBendingSymmetricRectangle();
TorsionTests.CombinedStressCombineKnownCases();

return TestHarness.Summary();
