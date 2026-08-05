using CScore.PlateStrip;
using CSfea.CScoreBridge;

namespace CSfea.Tests;

/// <summary>Проверки адаптера эквивалентного сечения в единицы собственного МКЭ.</summary>
public static class EquivalentBeamResponseTests
{
    public static void RunAll()
    {
        TestHarness.Section("Эквивалентная балка: масштаб сил и касательной");
        RunUnitScale();
        TestHarness.Section("Эквивалентная балка: проверка кручения");
        RunTorsionValidation();
    }

    static void RunUnitScale()
    {
        var section = Section();
        var response = new EquivalentBeamResponse(section);
        var state = new BeamStrainState(0.001, 0.002, 0.003);
        var domain = section.Forces(state);
        var force = response.Forces(state.Eps0, state.KappaY, state.KappaZ);
        var tangent = response.Tangent(state.Eps0, state.KappaY, state.KappaZ);

        TestHarness.CheckRel("N кН→Н", force.N, domain[0] * 1000.0, 1e-12);
        TestHarness.CheckRel("My кН·м→Н·м", force.My, domain[1] * 1000.0, 1e-12);
        TestHarness.CheckRel("Mz кН·м→Н·м", force.Mz, domain[2] * 1000.0, 1e-12);
        TestHarness.CheckRel("K row N", tangent[0, 2], section.BeamTangent[0, 2] * 1000.0, 1e-12);
        TestHarness.CheckRel("K row My", tangent[1, 1], section.BeamTangent[1, 1] * 1000.0, 1e-12);
        TestHarness.CheckRel("K row Mz", tangent[2, 2], section.BeamTangent[2, 2] * 1000.0, 1e-12);
        TestHarness.Check("GJ эквивалентной балки равен нулю", response.TorsionalStiffness() == 0.0);
    }

    static void RunTorsionValidation()
    {
        var section = Section();
        var noTorsion = EquivalentBeamAnalysisValidator.Validate(section, torsionActive: false);
        var withTorsion = EquivalentBeamAnalysisValidator.Validate(section, torsionActive: true);

        TestHarness.Check("Без кручения ошибок нет", noTorsion.All(d => !d.IsError));
        TestHarness.Check("Кручение блокируется", withTorsion.Any(d =>
            d.Code == "equivalent_section_torsion_unsupported" && d.IsError));
    }

    static EquivalentSection Section() => new()
    {
        BeamTangent = new[,]
        {
            { 2000.0, 40.0, 0.0 },
            { 40.0, 600.0, 0.0 },
            { 0.0, 0.0, 666.6666666666666 }
        },
        EA = 2000.0,
        EIy = 600.0,
        EIz = 666.6666666666666,
        IsCalculable = true
    };
}
