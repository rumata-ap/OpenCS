using System;
using System.Linq;

namespace CScore.Fem;

/// <summary>Результат поиска момента трещинообразования плитного сечения.</summary>
public sealed class ShellCrackingResult
{
    /// <summary>Момент трещинообразования по модулю, кН·м/м. 0 — сечение трещит от одной
    /// продольной силы, без момента.</summary>
    public double Mcrc { get; init; }

    public bool Converged { get; init; }

    /// <summary>НДС при M = M_crc. Именно это состояние нужно для σs,crc по п. 8.2.18,
    /// поэтому один поиск закрывает обе величины. Null, если не сошлось.</summary>
    public ShellStrainState? StrainState { get; init; }

    /// <summary>Максимальная растягивающая деформация бетона в найденном состоянии.</summary>
    public double MaxTensileStrain { get; init; }

    public int Iterations { get; init; }

    /// <summary>Пояснение, когда Converged = false.</summary>
    public string Description { get; init; } = "";
}

/// <summary>
/// Момент трещинообразования плитного (оболочечного) сечения по ДЕФОРМАЦИОННОЙ МОДЕЛИ,
/// п. 8.2.14 СП 63.13330: M_crc — тот уровень момента, при котором относительная деформация
/// бетона у растянутой грани достигает предельного значения ε_bt,ult (п. 8.1.30).
///
/// П. 8.2.8 делает этот путь основным, а формульный M_crc = Rbt,ser·γ·Wred (п. 8.2.10–8.2.12)
/// — лишь допускаемым упрощением для прямоугольных, тавровых и двутавровых сечений. Слоистой
/// модели нужен именно основной путь: иначе в деформационный расчёт затягивается эмпирический
/// коэффициент пластичности γ, который эта модель и так учитывает диаграммой бетона.
///
/// Стержневой аналог — <see cref="CScore.CrackingSolver"/> (работает по CrossSection); здесь
/// повторены его идеи: предел из диаграммы, бисекция по масштабу момента.
/// </summary>
public sealed class ShellCrackingSolver
{
    readonly PlateSection _section;
    readonly Diagramm _cDiag;
    readonly Diagramm _rDiag;
    readonly double? _epsTensionLimitOverride;
    readonly double _bisectTol;
    readonly int _bisectMaxIter;

    public ShellCrackingSolver(
        PlateSection section,
        Diagramm cDiag,
        Diagramm rDiag,
        double? epsTensionLimit = null,
        double bisectTol = 1e-6,
        int bisectMaxIter = 60)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _cDiag = cDiag ?? throw new ArgumentNullException(nameof(cDiag));
        _rDiag = rDiag ?? throw new ArgumentNullException(nameof(rDiag));
        _epsTensionLimitOverride = epsTensionLimit;
        _bisectTol = bisectTol;
        _bisectMaxIter = bisectMaxIter;
    }

    /// <summary>Предельная растягивающая деформация бетона ε_bt,ult — из растянутой ветви
    /// диаграммы состояния (п. 6.1.22), так же как в <see cref="CScore.CrackingSolver.TensionLimit"/>.</summary>
    public double TensionLimit()
    {
        if (_epsTensionLimitOverride.HasValue) return _epsTensionLimitOverride.Value;
        if (_cDiag.It?.X == null || _cDiag.It.X.Length == 0)
            throw new InvalidOperationException(
                "У диаграммы бетона не построена растянутая ветвь: ε_bt,ult определить нечем.");
        return _cDiag.It.X.Max();
    }

    /// <summary>
    /// Ищет момент трещинообразования по направлению (x или y) при неизменных остальных пяти
    /// компонентах вектора усилий. Масштабируется только момент своего направления: п. 8.2.18
    /// требует «принимая в соответствующих формулах значения M = M_crc», то есть продольная
    /// сила остаётся той, что действует.
    /// </summary>
    /// <param name="target">Усилия (Nx, Ny, Nxy, Mx, My, Mxy).</param>
    /// <param name="alongX">true — момент Mx (деформации ε_x), false — My (ε_y).</param>
    public ShellCrackingResult Solve(double[] target, bool alongX)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Length != 6) throw new ArgumentException("Ожидается 6 компонент усилий", nameof(target));

        double limit = TensionLimit();
        int idx = alongX ? 3 : 4;
        double m0 = target[idx];
        double sign = m0 < 0.0 ? -1.0 : 1.0;
        double h = _section.H;
        int iterations = 0;

        // Растянутая ветвь бетона включается принудительно: при TensionConcrete = false
        // растянутый бетон не работает вовсе и трещинообразование обнаружить нечем.
        var solver = new ShellStrainSolver(_section, _cDiag, _rDiag, tensionOverride: true);

        (bool ok, double eps, ShellStrainState? st) At(double magnitude)
        {
            iterations++;
            var t = (double[])target.Clone();
            t[idx] = sign * magnitude;
            var res = solver.Solve(t);
            if (!res.Converged) return (false, double.NaN, null);
            var s = res.StrainState;
            double e = alongX
                ? Math.Max(s.EpsX(h / 2.0), s.EpsX(-h / 2.0))
                : Math.Max(s.EpsY(h / 2.0), s.EpsY(-h / 2.0));
            return (true, e, s);
        }

        // Трещит ли сечение от одной продольной силы, без момента.
        var zero = At(0.0);
        if (zero.ok && zero.eps >= limit)
            return new ShellCrackingResult
            {
                Mcrc = 0.0, Converged = true, StrainState = zero.st,
                MaxTensileStrain = zero.eps, Iterations = iterations,
                Description = "сечение трещит от продольной силы"
            };

        // Расширяем верхнюю границу, пока деформация не дойдёт до предела ЛИБО пока решение
        // не перестанет сходиться. Несходимость здесь — не сбой, а признак: до трещины отклик
        // гладкий и решается устойчиво, а за скачком (растянутый бетон сорвался) равновесие
        // при растягивающей N найти не удаётся. Значит предел уже позади, и верхняя граница
        // найдена. Ту же трактовку применяет бисекция ниже.
        double lo = 0.0, hi = Math.Max(Math.Abs(m0), 1.0);
        var atHi = At(hi);
        int grow = 0;
        while (atHi.ok && atHi.eps < limit)
        {
            lo = hi;
            hi *= 2.0;
            if (hi > 1e6 || grow++ > 40)
                return new ShellCrackingResult
                {
                    Converged = false, Iterations = iterations,
                    Description = "предел растяжения не достигнут при M → ∞"
                };
            atHi = At(hi);
        }

        // Бисекция по модулю момента. Отклик у трещинообразования РАЗРЫВНЫЙ: за пределом
        // растянутый бетон срывается, и деформация скачком уходит на порядок выше ε_bt,ult.
        // Поэтому искомое состояние — последнее ДО скачка: наибольший момент, при котором
        // предел ещё не достигнут. Его и возвращаем вместе с его НДС, иначе σs,crc считалась
        // бы по уже раскрывшемуся сечению.
        ShellStrainState? best = zero.ok ? zero.st : null;
        double bestEps = zero.ok ? zero.eps : 0.0;
        for (int i = 0; i < _bisectMaxIter && hi - lo > _bisectTol * Math.Max(1.0, hi); i++)
        {
            double mid = 0.5 * (lo + hi);
            var at = At(mid);
            if (!at.ok) { hi = mid; continue; }   // нет сходимости — считаем, что скачок уже позади
            if (at.eps >= limit) hi = mid;
            else { lo = mid; best = at.st; bestEps = at.eps; }
        }

        return new ShellCrackingResult
        {
            Mcrc = lo,
            Converged = best != null,
            StrainState = best,
            MaxTensileStrain = bestEps,
            Iterations = iterations,
            Description = best == null ? "не удалось найти состояние до образования трещины" : ""
        };
    }
}
