using CSfea.Tests;

Console.WriteLine("CSfea — проверки порта GreenSectionPy/fea");

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

BucklingTests.RunSimplySupportedPlate();

return TestHarness.Summary();
