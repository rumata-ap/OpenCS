namespace CScore.Hysteresis;

/// <summary>
/// Зафиксированные состояния волокон для задачи с гистерезисом: по области — модели и массивы состояний, параллельные
/// area.Fibers (для стержня в бетоне — второй массив бетона-носителя). Пробное интегрирование ничего не меняет;
/// <see cref="Commit"/> записывает пробные состояния как зафиксированные.
/// </summary>
public sealed class FiberStateField
{
    sealed class Slot
    {
        public required AreaModels Models;
        public required HystState[] Main;
        public HystState[]? Host;
    }

    readonly Dictionary<MaterialArea, Slot> _slots = [];

    public FiberStateField(IEnumerable<MaterialArea> areas, CalcType calc, HysteresisOptions o,
        Func<MaterialArea, double?> rebarRupture, double softening)
    {
        foreach (var area in areas)
        {
            if (!MaterialArea.IsCalcActive(area) || _slots.ContainsKey(area)) continue;
            if (area.Fibers.Count == 0)
                throw new InvalidOperationException($"Область «{area.Tag}» без волокон: гистерезис требует режима «по волокнам».");
            var models = HysteresisModelFactory.Create(area, calc, o, rebarRupture(area), softening);
            _slots[area] = new Slot
            {
                Models = models,
                Main = Enumerable.Repeat(models.Main.Initial, area.Fibers.Count).ToArray(),
                Host = models.Host is null ? null : Enumerable.Repeat(models.Host.Initial, area.Fibers.Count).ToArray(),
            };
        }
    }

    /// <summary>Сброс всех состояний к начальным.</summary>
    public void Reset()
    {
        foreach (var s in _slots.Values)
        {
            Array.Fill(s.Main, s.Models.Main.Initial);
            if (s.Host is not null) Array.Fill(s.Host, s.Models.Host!.Initial);
        }
    }

    /// <summary>
    /// σ волокна i области при плоскости ka (деформация за вычетом <see cref="CScore.Fiber.Eps_stage"/>);
    /// sigOwn — σ стали без вычета бетона-носителя; next — состояния для фиксации.
    /// </summary>
    static double Fiber(Slot s, Fiber f, int i, Kurvature ka, bool ten, out double eps, out double e2,
        out double sigOwn, out HystState nextMain, out HystState nextHost)
    {
        eps = ka.e0 + ka.ky * f.Y + ka.kz * f.X - f.Eps_stage;
        nextHost = default;
        if (s.Models.IsConcrete)
        {
            sigOwn = s.Models.Main.Trial(s.Main[i], eps, ten, out e2, out nextMain);
            return sigOwn;
        }
        double sig = s.Models.Main.Trial(s.Main[i], eps + f.Eps_p, ten, out e2, out nextMain);
        sigOwn = sig;
        if (s.Host is not null)
        {
            sig -= s.Models.Host!.Trial(s.Host[i], eps, ten, out double eh, out nextHost);
            e2 -= eh;
        }
        return sig;
    }

    /// <summary>Пробный интеграл N, Mx, My по областям (состояния не меняются); writeFibers — записать волокнам
    /// Eps, Sig, E, E2, усилия (поля и карты).</summary>
    public Load Integrate(IEnumerable<(MaterialArea area, Kurvature k)> areas, bool ten, bool writeFibers)
    {
        double n = 0, mx = 0, my = 0;
        foreach (var (area, ka) in areas)
        {
            if (!_slots.TryGetValue(area, out var s)) continue;
            for (int i = 0; i < area.Fibers.Count; i++)
            {
                var f = area.Fibers[i];
                double sig = Fiber(s, f, i, ka, ten, out double eps, out double e2, out double sigOwn, out _, out _);
                double a = f.Area;
                n += sig * a; mx += sig * a * f.Y; my += sig * a * f.X;
                if (writeFibers)
                {
                    f.Eps = eps; f.Sig = sig; f.SigOwn = sigOwn;
                    f.N = sig * a; f.Mx = sig * a * f.Y; f.My = sig * a * f.X;
                    double et = eps + f.Eps_p;
                    f.E = Math.Abs(et) > 1e-20 ? sig / et : 0; f.E2 = e2;
                }
            }
        }
        return new Load { N = n, Mx = mx, My = my };
    }

    /// <summary>Фиксирует пробные состояния волокон при плоскостях областей.</summary>
    public void Commit(IEnumerable<(MaterialArea area, Kurvature k)> areas, bool ten)
    {
        foreach (var (area, ka) in areas)
        {
            if (!_slots.TryGetValue(area, out var s)) continue;
            for (int i = 0; i < area.Fibers.Count; i++)
            {
                Fiber(s, area.Fibers[i], i, ka, ten, out _, out _, out _, out var nm, out var nh);
                s.Main[i] = nm;
                if (s.Host is not null) s.Host[i] = nh;
            }
        }
    }

    /// <summary>
    /// Остаточные деформации после пробного шага при плоскостях trialAt: бетон — наибольшая |εp| (P2 Karsan–Jirsa),
    /// арматура — наибольшая |ε − σ/Eразгр|, Eразгр — касательный модуль пробы малого шага назад от нового состояния.
    /// </summary>
    public (double Concrete, double Rebar) Residual(IEnumerable<(MaterialArea area, Kurvature k)> trialAt, bool ten)
    {
        double rc = 0, rs = 0;
        foreach (var (area, ka) in trialAt)
        {
            if (!_slots.TryGetValue(area, out var s)) continue;
            for (int i = 0; i < area.Fibers.Count; i++)
            {
                Fiber(s, area.Fibers[i], i, ka, ten, out _, out _, out _, out var nm, out _);
                if (s.Models.IsConcrete) rc = Math.Max(rc, Math.Abs(nm.P2));
                else
                {
                    s.Models.Main.Trial(nm, nm.Eps - 1e-7, ten, out double eUnload, out _);
                    double res = eUnload > 0 ? nm.Eps - nm.Sig / eUnload : 0;
                    rs = Math.Max(rs, Math.Abs(res));
                }
            }
        }
        return (rc, rs);
    }
}
