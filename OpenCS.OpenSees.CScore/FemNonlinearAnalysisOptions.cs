namespace OpenCS.OpenSees.CScore;

/// <summary>Настройки нелинейного расчёта, задаваемые на постановке (FemAnalysisParams):
/// формулировка geomTransf, шаг/максимум коэффициента нагрузки, уточнение, критерий сходимости,
/// точки интегрирования.</summary>
public sealed record FemNonlinearAnalysisOptions(
    string GeomTransfKind,
    double LoadFactorStep,
    double MaxLoadFactor,
    int RefinementDivisions,
    double Tolerance,
    int MaxIterations,
    int IntegrationPoints,
    string ConvergenceTest = "EnergyIncr")
{
    /// <summary>Legacy-конструктор для старых вызывающих мест: LoadSteps → шаг 1/LoadSteps, λmax=1.</summary>
    public FemNonlinearAnalysisOptions(
        string geomTransfKind,
        int loadSteps,
        double tolerance,
        int maxIterations,
        int integrationPoints,
        string convergenceTest = "EnergyIncr")
        : this(geomTransfKind, 1.0 / loadSteps, 1.0, 10, tolerance, maxIterations, integrationPoints, convergenceTest)
    {
    }
}
