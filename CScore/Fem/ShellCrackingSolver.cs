using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore.Fem;

/// <summary>Результат поиска момента трещинообразования плитного сечения.</summary>
public sealed class ShellCrackingResult
{
    /// <summary>Момент трещинообразования по модулю, кН·м/м, в запрошенном направлении:
    /// M_crc = k_crc·|M|, где M — момент этого направления в заданных усилиях. 0 — сечение
    /// трещит от одной продольной силы либо момента этого направления нет.</summary>
    public double Mcrc { get; init; }

    /// <summary>Множитель моментов k_crc: при усилиях (N, k·M) бетон на грани доходит до
    /// ε_bt,ult. k_crc &lt; 1 — при заданных усилиях сечение с трещиной; 0 — трещит от одной
    /// продольной силы.</summary>
    public double MomentFactor { get; init; }

    /// <summary>true — поиск остановлен тем, что бетон на грани дошёл до ε_bt,ult (трещина).
    /// false — раньше исчерпалось равновесие сечения (сильно обжатая стена: бетон раздавливается
    /// до того, как грань растянется до предела); тогда <see cref="MomentFactor"/> — множитель
    /// потери равновесия, трещин до него нет.</summary>
    public bool CrackingReached { get; init; } = true;

    public bool Converged { get; init; }

    /// <summary>НДС непосредственно ПЕРЕД образованием трещины: растянутый бетон ещё работает
    /// и несёт почти всё усилие. Именно на нём останавливается поиск. Null, если не сошлось.</summary>
    public ShellStrainState? StrainState { get; init; }

    /// <summary>НДС сечения С ТРЕЩИНОЙ при том же M = M_crc (растянутый бетон выключен,
    /// п. 8.2.16). Именно отсюда берётся σs,crc: п. 8.2.18 определяет её как напряжение
    /// «сразу ПОСЛЕ образования нормальных трещин», а не перед ним — до трещины арматура
    /// напряжена слабо, усилие несёт бетон. Null, если это решение не сошлось.</summary>
    public ShellStrainState? CrackedStrainState { get; init; }

    /// <summary>Максимальная растягивающая деформация бетона в найденном состоянии.</summary>
    public double MaxTensileStrain { get; init; }

    public int Iterations { get; init; }

    /// <summary>Пояснение, когда Converged = false.</summary>
    public string Description { get; init; } = "";
}

/// <summary>
/// Поиск состояния трещинообразования для одного направления: (усилия, направление) →
/// (M_crc, НДС при M = M_crc). Один поиск закрывает обе величины, нужные слоистой модели
/// (п. 8.2.14 и п. 8.2.18). Реализация по умолчанию — <see cref="ShellCrackingSolver.Solve"/>;
/// параметром делегата она передаётся, чтобы CScore.Fem не зависел от того, откуда берутся
/// диаграммы, и чтобы вызывающий мог кэшировать результат между наборами усилий.
/// </summary>
/// <param name="target6">Усилия (Nx, Ny, Nxy, Mx, My, Mxy).</param>
/// <param name="alongX">true — момент Mx, false — My.</param>
public delegate ShellCrackingResult? ShellCrackingProbe(double[] target6, bool alongX);

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
    readonly double _solverTolRes;

    /// <param name="solverTolRes">Допуск по невязке решений НДС внутри поиска (относительно
    /// 1 + ‖S‖). Штатный 1e-3 для поиска порога слишком груб: у стены норму усилий задаёт
    /// большое Ny, и допуск выходит в единицы процентов от момента — решение со старта в
    /// соседней точке принимается без единой итерации, и порог «уплывает» на размер допуска.</param>
    public ShellCrackingSolver(
        PlateSection section,
        Diagramm cDiag,
        Diagramm rDiag,
        double? epsTensionLimit = null,
        double bisectTol = 1e-6,
        int bisectMaxIter = 60,
        double solverTolRes = 1e-7)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _cDiag = cDiag ?? throw new ArgumentNullException(nameof(cDiag));
        _rDiag = rDiag ?? throw new ArgumentNullException(nameof(rDiag));
        _epsTensionLimitOverride = epsTensionLimit;
        _bisectTol = bisectTol;
        _bisectMaxIter = bisectMaxIter;
        _solverTolRes = solverTolRes;
    }

    /// <summary>
    /// Пробник с кэшем по вектору усилий: одинаковые усилия ищутся один раз. Нужен там, где
    /// одно сочетание проверяется несколько раз с разными φ1 (п. 8.2.7: длительное — и для
    /// acrc,1 при φ1 = 1,4, и для acrc,3 при φ1 = 1,0), — поиск от φ1 не зависит, а стоит
    /// десятков решений 6×6. Кэш не потокобезопасен: один пробник — на один поток.
    /// </summary>
    public ShellCrackingProbe CachedProbe()
    {
        var cache = new Dictionary<(double, double, double, double, double, double, bool), ShellCrackingResult>();
        return (target, alongX) =>
        {
            var key = (target[0], target[1], target[2], target[3], target[4], target[5], alongX);
            if (!cache.TryGetValue(key, out var r))
                cache[key] = r = Solve(target, alongX);
            return r;
        };
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
    /// Ищет состояние трещинообразования по лучу моментов: все три момента (Mx, My, Mxy)
    /// масштабируются общим множителем k, продольные силы (Nx, Ny, Nxy) остаются заданными —
    /// п. 8.2.18 требует «принимая в соответствующих формулах значения M = M_crc», то есть
    /// продольная сила та, что действует. Трещина — когда главная растягивающая деформация
    /// бетона на любой из граней доходит до ε_bt,ult.
    ///
    /// Масштабировать только момент своего направления нельзя: кручение Mxy растягивает грань
    /// наравне с Mx и My (на стене с заметным Mxy главная деформация у арматуры идёт под
    /// 20–30° к оси), и тогда порог по одному Mx пропускает трещину, а σs,crc, взятая при
    /// Mx = M_crc, но полном Mxy, выходит почти равной σs — ψs занижается вдвое. Луч моментов
    /// даёт M_crc и σs,crc в одном и том же состоянии и совпадает со схемой Кисп слоистой
    /// модели по прочности (M/M_пред при неизменных N).
    /// </summary>
    /// <param name="target">Усилия (Nx, Ny, Nxy, Mx, My, Mxy).</param>
    /// <param name="alongX">Только для <see cref="ShellCrackingResult.Mcrc"/>: true — пересчёт
    /// в Mx, false — в My. Сам поиск от направления не зависит.</param>
    public ShellCrackingResult Solve(double[] target, bool alongX)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Length != 6) throw new ArgumentException("Ожидается 6 компонент усилий", nameof(target));

        double limit = TensionLimit();
        double mDir = Math.Abs(target[alongX ? 3 : 4]);
        double h = _section.H;
        int iterations = 0;

        // Растянутая ветвь бетона включается принудительно: при TensionConcrete = false
        // растянутый бетон не работает вовсе и трещинообразование обнаружить нечем.
        var solver = new ShellStrainSolver(_section, _cDiag, _rDiag,
            tolRes: _solverTolRes, tensionOverride: true);

        // Сечение С ТРЕЩИНОЙ (п. 8.2.16) — для σs,crc по п. 8.2.18: растянутый бетон выключен
        // независимо от TensionConcrete сечения.
        var crackedSolver = new ShellStrainSolver(_section, _cDiag, _rDiag,
            tolRes: _solverTolRes, tensionOverride: false);

        // Поиск идёт от k = 0 (одни продольные силы): если бетон уже за ε_bt,ult от них, это
        // ловит проверка ниже, а дальше по лучу отклик непрерывен до первой трещины. Поэтому
        // двухосный поиск здесь устойчив — несходимость на нём бывает лишь за скачком, где её
        // и трактует бисекция.
        double[] Scaled(double k)
        {
            var t = (double[])target.Clone();
            t[3] *= k; t[4] *= k; t[5] *= k;
            return t;
        }

        // Наибольшая главная растягивающая деформация бетона на гранях.
        static double MaxPrincipalAtFaces(ShellStrainState s, double h)
        {
            double m = double.NegativeInfinity;
            foreach (double z in new[] { h / 2.0, -h / 2.0 })
            {
                PlateSection.PrincipalStrains2D(s.EpsX(z), s.EpsY(z), s.GammaXY(z), out double e1, out _, out _);
                m = Math.Max(m, e1);
            }
            return m;
        }

        // Начальное приближение — последнее состояние НИЖЕ предела (нижняя граница поиска).
        // Отклик растянутого бетона у предела негладкий, и «холодный» упругий старт на нём
        // разваливается (двухосное сочетание с растягивающей N не решалось вовсе); шаг же от
        // соседней точки продолжает решение по параметру и сходится. Старт именно от нижней
        // границы, а не от последней сошедшейся точки: у предела растянутая ветвь диаграммы
        // почти горизонтальна, и равновесий при одном k бывает несколько. Старт от точки за
        // порогом уводил бисекцию на соседнюю ветвь (стена 177: порог k = 0,9992, найдено
        // 0,999999); от нижней границы решение продолжается по k монотонно, как нагружение.
        double[]? warm = null;

        (bool ok, double eps, ShellStrainState? st) At(double k)
        {
            iterations++;
            var t = Scaled(k);
            var res = solver.Solve(t, warm);
            // Запасная стратегия — каскад с продолжением по λ (SolveRobust): им же пользуются
            // вызывающие для рабочего НДС.
            if (!res.Converged) res = solver.SolveRobust(t);
            if (!res.Converged) return (false, double.NaN, null);
            var s = res.StrainState;
            return (true, MaxPrincipalAtFaces(s, h), s);
        }

        /// <summary>НДС сечения с трещиной при найденном k_crc — источник σs,crc.</summary>
        ShellStrainState? Cracked(double k)
        {
            var t = Scaled(k);
            var res = crackedSolver.Solve(t);
            if (!res.Converged) res = crackedSolver.SolveRobust(t);
            return res.Converged ? res.StrainState : null;
        }

        // Трещит ли сечение от одной продольной силы, без момента.
        var zero = At(0.0);
        if (zero.ok && zero.eps >= limit)
            return new ShellCrackingResult
            {
                Mcrc = 0.0, MomentFactor = 0.0, Converged = true, StrainState = zero.st,
                CrackedStrainState = Cracked(0.0),
                MaxTensileStrain = zero.eps, Iterations = iterations,
                Description = "сечение трещит от продольной силы"
            };
        ShellStrainState? best = zero.ok ? zero.st : null;
        double bestEps = zero.ok ? zero.eps : 0.0;
        void Accept(ShellStrainState s, double eps)
        {
            best = s; bestEps = eps; warm = s.ToArray();
        }
        if (zero.ok) Accept(zero.st!, zero.eps);

        // Расширяем верхнюю границу, пока деформация не дойдёт до предела ЛИБО пока решение
        // не перестанет сходиться. Несходимость здесь — не сбой, а признак: до трещины отклик
        // гладкий и решается устойчиво, а за скачком (растянутый бетон сорвался) равновесие
        // при растягивающей N найти не удаётся. Значит предел уже позади, и верхняя граница
        // найдена. Ту же трактовку применяет бисекция ниже.
        double lo = 0.0, hi = 1.0;
        var atHi = At(hi);
        int grow = 0;
        while (atHi.ok && atHi.eps < limit)
        {
            Accept(atHi.st!, atHi.eps);
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

        // Внутри вилки — проход шагами от нижней границы, каждый шаг продолжает предыдущий,
        // как при пропорциональном нагружении: к пределу решение подходит монотонно, а
        // бисекция ниже работает уже на одном малом шаге. Точность порога задаёт прежде всего
        // допуск решения (solverTolRes): у предела растянутая ветвь почти горизонтальна,
        // деформация грани меняется с k медленно, и грубый допуск сдвигал порог на проценты.
        const int marchSteps = 20;
        double step = (hi - lo) / marchSteps;
        for (int j = 1; j < marchSteps; j++)
        {
            double k = lo + step;
            var at = At(k);
            if (!at.ok || at.eps >= limit) { hi = k; break; }
            Accept(at.st!, at.eps);
            lo = k;
        }

        // Бисекция по множителю моментов. Отклик у трещинообразования РАЗРЫВНЫЙ: за пределом
        // растянутый бетон срывается, и деформация скачком уходит на порядок выше ε_bt,ult.
        // Поэтому искомое состояние — последнее ДО скачка: наибольший множитель, при котором
        // предел ещё не достигнут. Его и возвращаем вместе с его НДС, иначе σs,crc считалась
        // бы по уже раскрывшемуся сечению.
        for (int i = 0; i < _bisectMaxIter && hi - lo > _bisectTol * Math.Max(1.0, hi); i++)
        {
            double mid = 0.5 * (lo + hi);
            var at = At(mid);
            if (!at.ok) { hi = mid; continue; }   // нет сходимости — считаем, что скачок уже позади
            if (at.eps >= limit) hi = mid;
            else { lo = mid; Accept(at.st!, at.eps); }
        }

        // Чем остановлен поиск: трещиной или потерей равновесия. У трещины последнее состояние
        // до порога лежит вплотную к ε_bt,ult (бисекция сжимает вилку, а деформация до скачка
        // непрерывна). Если же равновесие исчезло, когда грань ещё далека от предела, сечение
        // исчерпало прочность раньше, чем образовалась трещина.
        bool reached = bestEps >= 0.99 * limit;

        return new ShellCrackingResult
        {
            Mcrc = lo * mDir,
            MomentFactor = lo,
            CrackingReached = reached,
            Converged = best != null,
            StrainState = best,
            CrackedStrainState = best != null ? Cracked(lo) : null,
            MaxTensileStrain = bestEps,
            Iterations = iterations,
            Description = best == null ? "не удалось найти состояние до образования трещины"
                : !reached ? $"равновесие исчерпано при k = {lo:G4} раньше образования трещины" : ""
        };
    }
}
