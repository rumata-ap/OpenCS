using System;
using System.Linq;
using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Сечение, у которого выгиб от преднапряжения растягивает противоположную грань настолько,
/// что она трескается ещё до приложения нагрузки. Критерий трещинообразования обязан смотреть
/// на грань, которую растягивает ВНЕШНЯЯ нагрузка: трещина от обжатия к моменту
/// трещинообразования от нагрузки уже закрыта (проверено на реальной задаче с σsp = 900 МПа —
/// грань, растянутая обжатием, переходит в сжатие задолго до этого). Тот же дефект, что был
/// исправлен в задаче «кривизна-момент», проверяется здесь для остальных задач, которые ищут
/// трещину через <see cref="CrackingSolver"/>: cracking, crack_width, total_curvature.
/// </summary>
public class PrestressCrackedFaceZoneTests
{
    const double N = -100.0, Mx = -100.0, My = 20.0;

    /// <summary>
    /// σsp = 850 МПа — грань, растянутая обжатием, уже треснула (ε = 1,88e-4 при пределе
    /// 1,50e-4), но дотрещинная модель ещё сходится. При больших σsp свободное состояние само
    /// перестаёт считаться (бетон на нисходящей ветви растяжения) — это отдельное ограничение
    /// <see cref="CrackingSolver"/>, к разбираемому дефекту отношения не имеющее.
    /// </summary>
    static CrossSection Section() => TestSections.RectWithEccentricPrestressedRebar(sigSp: 850.0);

    static Func<double, double, bool> Zone(CrossSection section) =>
        CrackingSolver.LoadedTensionZone(section, N, Mx, My, nAtZeroMoment: N);

    [Fact]
    public void LoadedTensionZone_SelectsTheFaceTheLoadStretches()
    {
        var zone = Zone(Section());

        // Mx < 0 растягивает y < 0, My > 0 растягивает x > 0.
        Assert.True(zone(0.15, -0.25));
        Assert.False(zone(-0.15, 0.25));
    }

    [Fact]
    public void CrackingSolver_WithoutZone_ReportsZeroMomentOnTheAlreadyCrackedFace()
    {
        // Зафиксировано как исходное поведение: без фильтра критерий видит грань, треснувшую
        // от обжатия, и рапортует, что трещина уже есть при нулевом моменте.
        var section = Section();
        var result = new CrackingSolver(section, CalcType.N, solverTol: 0.1, solverMaxIter: 25)
            .CrackingMoment(N, Mx, My);

        Assert.True(result.Converged);
        Assert.True(Math.Abs(result.Mx) < 1e-6, $"ожидался нулевой момент, получено {result.Mx:F6}");
    }

    [Fact]
    public void CrackingSolver_WithLoadedZone_FindsTheMomentOnTheLoadedFace()
    {
        var section = Section();
        var solver = new CrackingSolver(section, CalcType.N, solverTol: 0.1, solverMaxIter: 25,
            tensionZone: Zone(section));

        var result = solver.CrackingMoment(N, Mx, My);

        Assert.True(result.Converged);
        Assert.True(Math.Abs(result.Mx) > 100.0,
            $"Mx={result.Mx:F3} — похоже на грань, растянутую обжатием, а не нагрузкой");

        // Предел достигнут именно на грани, которую растягивает нагрузка. Допуск — точность
        // внешней бисекции по масштабу момента (bisectTol = 1e-6), а не «ноль».
        var plane = result.StrainPlane!.Value;
        double limit = solver.TensionLimit();
        double bottomRight = plane.e0 + plane.ky * -0.25 + plane.kz * 0.15;
        Assert.True(Math.Abs(bottomRight - limit) < 1e-5 * limit + 1e-6,
            $"на нагруженной грани ε={bottomRight:G6} при пределе {limit:G6}");
    }

    [Fact]
    public void TotalCurvatureSolver_UsesTheFaceStretchedByTheLoad()
    {
        var section = Section();
        var solver = new TotalCurvatureSolver(section, calcCrc: CalcType.N,
            solverTol: 0.1, solverMaxIter: 25, centralJacobian: false);

        var result = solver.Compute(N, Mx * 0.5, My * 0.5, Mx, My);

        Assert.True(result.CrcConverged);
        Assert.True(result.Mcrc > 100.0,
            $"Mcrc={result.Mcrc:F3} — похоже на грань, растянутую обжатием, а не нагрузкой");
    }
}
