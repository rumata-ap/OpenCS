using CScore.CalculationTrace;
using Xunit;

namespace CScore.Tests.CalculationTrace;

/// <summary>Проверяет переносимый контракт шагов расчётной трассировки.</summary>
public sealed class CalculationTraceContractTests
{
    [Fact]
    public void TraceStep_keeps_raw_values_and_stable_identity()
    {
        var step = new CalculationTraceStep
        {
            StepId = "sp63.normal.bending.compression-zone",
            CodeReference = "8.1.8",
            FormulaLatex = "x = \\frac{R_s A_s - R_{sc} A'_s}{R_b b}",
            Values = new Dictionary<string, TraceValue>
            {
                ["Rs"] = new(4434, CalculationUnit.KilogramForcePerSquareCentimeter),
                ["As"] = new(12, CalculationUnit.SquareCentimeter)
            }
        };

        Assert.Equal("sp63.normal.bending.compression-zone", step.StepId);
        Assert.Equal(4434, step.Values["Rs"].Value);
        Assert.Equal(CalculationUnit.KilogramForcePerSquareCentimeter,
            step.Values["Rs"].Unit);
    }
}
