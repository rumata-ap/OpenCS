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

HeatMaterialTests.RunAll();

Sp468MaterialsTests.RunAll();

HeatTri3Tests.RunAll();

HeatMeshTests.RunAll();

HeatSteadyTests.RunAll();

HeatBoundaryTests.RunAll();

HeatTransientTests.RunAll();

LimitForceSolverTests.RunAll();

BucklingTests.RunSimplySupportedPlate();

return TestHarness.Summary();
