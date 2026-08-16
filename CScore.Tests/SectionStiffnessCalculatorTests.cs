using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>Проверки передачи эффективных напряжений в расчёт жёсткости сечения.</summary>
public sealed class SectionStiffnessCalculatorTests
{
    [Fact]
    public void Compute_UsesEffectiveStressCallbackForRebar()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);
        var plane = new Kurvature { e0 = 0.001, ky = 0.0, kz = 0.0 };
        section.SetEps(plane, CalcType.N, ten: false);

        var plain = SectionStiffnessCalculator.Compute(section, plane, CalcType.N, ten: false);
        var corrected = SectionStiffnessCalculator.Compute(
            section, plane, CalcType.N, ten: false,
            effectiveStressKpaByFiber: fiber => fiber.Y < 0.0
                ? fiber.Sig / 0.5
                : fiber.Sig);

        Assert.NotNull(plain);
        Assert.NotNull(corrected);
        Assert.True(corrected.Value.EA_kN > plain.Value.EA_kN);
        Assert.True(corrected.Value.Yc_mm < plain.Value.Yc_mm);
    }
}
