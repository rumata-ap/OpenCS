using CScore;
using CScore.Fire;

namespace CSfea.Tests;

/// <summary>Собственный предел огнестойкости: sweep по температурным снимкам (п. 8.5 СП 468).</summary>
public static class FireRTimeTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireRTime: собственный предел огнестойкости");
        FirstCrossing_IsFound();
        NonMonotone_IsFlagged();
        Refinement_StaysInsideBracket();
        LimitNotReached_ReportsLowerBound();
        FailedAtStart_IsFlagged();
        IsolatedUnreliableSnapshot_DoesNotBreakCrossing();
        ConsecutiveUnreliableSnapshots_AreMarkedPartial();
        MismatchedArrays_Throw();
        EmptyHistory_Throws();
    }

    // Синтетический sweep по заранее заданным значениям factor — отвязан от решателя,
    // чтобы проверялась именно логика поиска перехода.
    static FireRTimeResult Sweep(double[] times, double[] factors, bool refine)
        => FireRTime.FromFactors(times, factors, refine);

    static void FirstCrossing_IsFound()
    {
        var r = Sweep([0, 30, 60, 90], [2.0, 1.5, 0.8, 0.4], refine: false);
        TestHarness.Check("FireRTime_FirstCrossing",
            r.RMin.HasValue && Math.Abs(r.RMin.Value - 60.0) < 1e-9 && !r.LimitNotReached,
            $"rMin={r.RMin}");
    }

    static void NonMonotone_IsFlagged()
    {
        // После первого отказа factor снова поднимается выше единицы.
        var r = Sweep([0, 30, 60, 90], [2.0, 0.9, 1.2, 0.5], refine: false);
        TestHarness.Check("FireRTime_NonMonotoneFlagged",
            r.NonMonotone && r.RMin.HasValue && Math.Abs(r.RMin.Value - 30.0) < 1e-9,
            $"rMin={r.RMin}, nonMonotone={r.NonMonotone}");
    }

    static void Refinement_StaysInsideBracket()
    {
        // Между 30 и 60 мин factor падает с 1,5 до 0,5 -> переход при 45 мин.
        var r = Sweep([0, 30, 60], [2.0, 1.5, 0.5], refine: true);
        bool inside = r.RMin is >= 30.0 and <= 60.0;
        TestHarness.Check("FireRTime_RefinementInsideBracket",
            inside && Math.Abs(r.RMin!.Value - 45.0) < 0.05
                   && r.BracketMin == 30.0 && r.BracketMax == 60.0,
            $"rMin={r.RMin}, bracket=[{r.BracketMin}, {r.BracketMax}]");
    }

    static void LimitNotReached_ReportsLowerBound()
    {
        var r = Sweep([0, 30, 60], [2.0, 1.8, 1.2], refine: true);
        TestHarness.Check("FireRTime_LimitNotReached",
            r.LimitNotReached && r.RMin is null
            && r.RMinLowerBound.HasValue && Math.Abs(r.RMinLowerBound.Value - 60.0) < 1e-9,
            $"lowerBound={r.RMinLowerBound}");
    }

    static void FailedAtStart_IsFlagged()
    {
        var r = Sweep([0, 30], [0.7, 0.4], refine: true);
        TestHarness.Check("FireRTime_FailedAtStart",
            r.FailedAtStart && r.RMin.HasValue && r.RMin.Value == 0.0,
            $"rMin={r.RMin}, failedAtStart={r.FailedAtStart}");
    }

    static void IsolatedUnreliableSnapshot_DoesNotBreakCrossing()
    {
        var r = Sweep([0, 30, 60, 90], [2.0, double.NaN, 0.8, 0.4], refine: false);
        TestHarness.Check("FireRTime_IsolatedUnreliableIsWarning",
            r.RMin == 60.0 && r.UnreliableSnapshots.SequenceEqual([1])
            && !r.HasConsecutiveUnreliableSnapshots,
            $"rMin={r.RMin}, unreliable=[{string.Join(',', r.UnreliableSnapshots)}]");
    }

    static void ConsecutiveUnreliableSnapshots_AreMarkedPartial()
    {
        var r = Sweep([0, 30, 60, 90, 120], [2.0, double.NaN, double.NaN, 0.8, 0.4], refine: false);
        TestHarness.Check("FireRTime_ConsecutiveUnreliableIsPartial",
            r.RMin == 90.0 && r.HasConsecutiveUnreliableSnapshots,
            $"rMin={r.RMin}, unreliable=[{string.Join(',', r.UnreliableSnapshots)}]");
    }

    static void MismatchedArrays_Throw()
    {
        bool threw = false;
        try { Sweep([0, 30], [2.0], refine: false); }
        catch (ArgumentException) { threw = true; }
        TestHarness.Check("FireRTime_MismatchedArraysThrow", threw);
    }

    static void EmptyHistory_Throws()
    {
        bool threw = false;
        try { Sweep([], [], refine: false); }
        catch (InvalidOperationException) { threw = true; }
        TestHarness.Check("FireRTime_EmptyHistoryThrows", threw);
    }
}
