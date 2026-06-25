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

FireCurvesTests.RunAll();

FireMeshBuilderTests.RunAll();

FireThermalServiceTests.RunAll();

FireParityTests.RunAll();

FireFiberSectionTests.RunAll();

FireRCheckTests.RunAll();

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

ShellStrainSolverTests.RunAll();

BucklingTests.RunSimplySupportedPlate();

SparseOrderingTests.RunAll();

SparseCholeskyTests.RunAll();

HeatAssemblyTests.RunAll();

ThermalBenchmark.RunAll();

SteelSectionTests.RunGeoPropsDirect();
SteelSectionTests.RunIBeamProperties();
SteelCheckerTests.RunSimpleCompressionCheck();

FemCheckRunnerTests.RunExtractCalcType();
FemCheckRunnerTests.RunExtractWorstDetail();
FemCheckRunnerTests.RunExtractWorstDetailNoDetails();

FemInfraTests.RunAll();

LiraCsvSchemaParserTests.RunAll();

return TestHarness.Summary();
