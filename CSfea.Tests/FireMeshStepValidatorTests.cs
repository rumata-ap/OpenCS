using CScore;
using CScore.Fire;

namespace CSfea.Tests;

/// <summary>Проверка шага тепловой сетки по п. 6.2 СП 468.</summary>
public static class FireMeshStepValidatorTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireMeshStepValidator: п. 6.2");
        StepBelowMaxDiameter_Blocks();
        StepAboveMaxDiameter_DoesNotBlock();
        StepOutOfRecommendedRange_WarnsOnly();
        ZeroDiameter_RestoredFromArea();
        NoDiameterNoArea_Counted();
        TooCoarseForSection_Blocks();
        FineStep_NotCoarse();
    }

    static CrossSection SectionWithRebar(double diameterM, double areaM2)
    {
        var section = FireFiberSectionTests.CreateSectionForTests();
        var area = section.Areas[^1];
        area.Fibers.Clear();
        area.Fibers.Add(new Fiber
        {
            TypeFiber = FiberType.point,
            X = 0.05,
            Y = 0.05,
            Diameter = diameterM,
            Area = areaM2
        });
        return section;
    }

    static void StepBelowMaxDiameter_Blocks()
    {
        var check = FireMeshStepValidator.Check(SectionWithRebar(0.012, 1.13e-4), meshStepM: 0.010);
        TestHarness.Check("FireMeshStep_BlocksWhenBelowDiameter",
            check.BlocksRun && check.BelowRebarDiameter && !check.TooCoarse
            && Math.Abs(check.MaxRebarDiameterM - 0.012) < 1e-12,
            $"blocks={check.BlocksRun}, maxD={check.MaxRebarDiameterM:F4}");
    }

    static void StepAboveMaxDiameter_DoesNotBlock()
    {
        var check = FireMeshStepValidator.Check(SectionWithRebar(0.012, 1.13e-4), meshStepM: 0.015);
        TestHarness.Check("FireMeshStep_PassesWhenAboveDiameter", !check.BlocksRun);
    }

    static void StepOutOfRecommendedRange_WarnsOnly()
    {
        var check = FireMeshStepValidator.Check(SectionWithRebar(0.012, 1.13e-4), meshStepM: 0.050);
        TestHarness.Check("FireMeshStep_WarnsOutOfRange",
            !check.BlocksRun && check.OutOfRecommendedRange);
    }

    static void ZeroDiameter_RestoredFromArea()
    {
        // Площадь 1,13e-4 м² соответствует диаметру ~0,012 м.
        var check = FireMeshStepValidator.Check(SectionWithRebar(0.0, 1.13e-4), meshStepM: 0.010);
        TestHarness.Check("FireMeshStep_DiameterFromArea",
            check.BlocksRun && Math.Abs(check.MaxRebarDiameterM - 0.012) < 5e-4,
            $"maxD={check.MaxRebarDiameterM:F5}");
    }

    static void NoDiameterNoArea_Counted()
    {
        var check = FireMeshStepValidator.Check(SectionWithRebar(0.0, 0.0), meshStepM: 0.015);
        TestHarness.Check("FireMeshStep_UnknownDiameterCounted",
            check.UnknownDiameterCount >= 1 && !check.BlocksRun,
            $"unknown={check.UnknownDiameterCount}");
    }

    // Сечение 1×1 м: шаг 0,2 м — 5 элементов по стороне (случай 26.09: 0,4 м на 500×500).
    static void TooCoarseForSection_Blocks()
    {
        var check = FireMeshStepValidator.Check(SectionWithRebar(0.012, 1.13e-4), meshStepM: 0.2);
        TestHarness.Check("FireMeshStep_TooCoarseBlocks",
            check.TooCoarse && check.BlocksRun && !check.BelowRebarDiameter
            && Math.Abs(check.MinSideM - 1.0) < 1e-9,
            $"coarse={check.TooCoarse}, minSide={check.MinSideM:F3}");
    }

    static void FineStep_NotCoarse()
    {
        var check = FireMeshStepValidator.Check(SectionWithRebar(0.012, 1.13e-4), meshStepM: 0.02);
        TestHarness.Check("FireMeshStep_FineNotCoarse", !check.TooCoarse && !check.BlocksRun);
    }
}
