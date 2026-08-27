using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Регрессия: быстрый решатель предельных усилий на ПРЕДНАПРЯЖЁННОМ сечении не должен
/// скатываться в бисекционный fallback.
///
/// Пин-вершина выбирается по упругому приближению <see cref="CrossSection.Guess"/> при
/// k = 1, тогда как искомая точка лежит при k = k_lim. Усилие обжатия не масштабируется
/// вместе с моментом, поэтому для преднапряжённого сечения знак эффективного момента
/// (а с ним и сжатая грань) при k = 1 и при k = k_lim может быть РАЗНЫМ. Ньютон с пином
/// на грани, которая в предельном состоянии растянута, сходится к математически точному,
/// но физически недопустимому корню (ε крайнего волокна ≪ ε_cu); IsValidSolution его
/// отбрасывает, и весь расчёт уходит в бисекцию.
/// </summary>
public class LimitForceSolverFastPrestressTests
{
    const double N = -500.0, Mx = -100.0, My = 25.0;
    const double NewtonTol = 0.1;
    const int NewtonMaxIter = 25;

    [Fact]
    public void MomentFactor_EccentricPrestress_NoBisectFallback()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar();

        var traces = CaptureTrace(out var restore);
        LimitForceResult fast;
        try
        {
            fast = new LimitForceSolverFast(section, CalcType.C,
                newtonTol: NewtonTol, newtonMaxIter: NewtonMaxIter, ten: false)
                .MomentFactor(N, Mx, My);
        }
        finally { restore(); }

        var reference = LimitForceSolver.ForCrossSection(section, CalcType.C,
            solverTol: NewtonTol, solverMaxIter: NewtonMaxIter, ten: false)
            .MomentFactor(N, Mx, My);

        Assert.True(fast.Converged);
        Assert.DoesNotContain(traces, t => t.StartsWith("BisectFallback"));
        Assert.Equal(reference.Factor, fast.Factor, 2);
        // Бюджет итераций: собственный Ньютон укладывается в единицы, fallback — в сотни.
        Assert.True(fast.NewtonIterations < 50,
            $"NewtonIterations={fast.NewtonIterations} — похоже на бисекционный fallback.");
    }

    /// <summary>
    /// Контроль: то же сечение без преднапряжения решается быстрым Ньютоном и раньше —
    /// тест защищает от «починки» через безусловный уход в бисекцию.
    /// </summary>
    [Fact]
    public void MomentFactor_WithoutPrestress_NoBisectFallback()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar(sigSp: 0.0);

        var traces = CaptureTrace(out var restore);
        LimitForceResult fast;
        try
        {
            fast = new LimitForceSolverFast(section, CalcType.C,
                newtonTol: NewtonTol, newtonMaxIter: NewtonMaxIter, ten: false)
                .MomentFactor(N, Mx, My);
        }
        finally { restore(); }

        Assert.True(fast.Converged);
        Assert.DoesNotContain(traces, t => t.StartsWith("BisectFallback"));
    }

    /// <summary>
    /// Перехват трассы решателя. <see cref="LimitForceSolverFast.DebugTrace"/> статичен, а
    /// xUnit параллелит коллекции — поэтому пишем только записи своего потока (решатель
    /// однопоточный и работает в потоке теста).
    /// </summary>
    static List<string> CaptureTrace(out Action restore)
    {
        var traces = new List<string>();
        int threadId = Environment.CurrentManagedThreadId;
        var previous = LimitForceSolverFast.DebugTrace;
        LimitForceSolverFast.DebugTrace = s =>
        {
            if (Environment.CurrentManagedThreadId != threadId) return;
            lock (traces) traces.Add(s);
        };
        restore = () => LimitForceSolverFast.DebugTrace = previous;
        return traces;
    }
}
