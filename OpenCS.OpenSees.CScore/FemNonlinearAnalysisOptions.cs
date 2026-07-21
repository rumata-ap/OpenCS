namespace OpenCS.OpenSees.CScore;

/// <summary>Настройки нелинейного расчёта, задаваемые на постановке (FemAnalysisParams):
/// формулировка geomTransf, число шагов нагрузки, критерий сходимости, точки интегрирования.</summary>
public sealed record FemNonlinearAnalysisOptions(
    string GeomTransfKind,
    int LoadSteps,
    double Tolerance,
    int MaxIterations,
    int IntegrationPoints,
    string ConvergenceTest = "EnergyIncr");
