using System.Runtime.ExceptionServices;

namespace CScore.LimitSurface;

/// <summary>
/// Строит сетку поверхности как образ границы области деформаций. Граница параметризуется сферой:
/// θ ∈ [0, π] — от равномерного растяжения к равномерному сжатию, φ — направление кривизны
/// (ky, kz) ∝ (cos φ, sin φ). На меридиане вставляются изломы (смена активного ограничения) и узлы
/// адаптации по θ; меридианы сшиваются с якорями в общих изломах и вставляются адаптивно по φ.
/// Каждая вершина — один вызов <see cref="CrossSection.Compute"/>; решателя нет.
/// </summary>
public sealed class StrainDomainMesher
{
    const int BisectIters = 48;
    const int MaxKinksPerInterval = 8;
    const double TwoPi = 2 * Math.PI;

    readonly CrossSection _section;
    readonly IStrainSurfaceCriterion _criterion;
    readonly LimitSurfaceOptions _opt;

    /// <summary>Создаёт построитель; диаграммы сечения должны быть уже построены.</summary>
    public StrainDomainMesher(CrossSection section, IStrainSurfaceCriterion criterion, LimitSurfaceOptions? options = null)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _criterion = criterion ?? throw new ArgumentNullException(nameof(criterion));
        _opt = options ?? new LimitSurfaceOptions();
    }

    /// <summary>Масштабы направления луча: выравнивают вытянутую область, на результат не влияют.</summary>
    sealed record Scales(double STen, double SComp, double SK, double L)
    {
        public (double A, double By, double Bz) Direction(double theta, double phi)
        {
            double c = Math.Cos(theta);
            double s = theta <= 0 || theta >= Math.PI ? 0.0 : Math.Sin(theta);   // полюса — без кривизны ровно
            double k = s * SK / L;
            return (c * (c >= 0 ? STen : SComp), k * Math.Cos(phi), k * Math.Sin(phi));
        }
    }

    /// <summary>Строит сетку.</summary>
    public LimitSurfaceMesh Build(CancellationToken ct = default)
    {
        var domain = _criterion.BuildDomain(_section).WithSingleSignRule(_opt.ApplySingleSignRule);
        var g = MakeScales(domain);

        var top = MakeNode(_section, domain, g, 0.0, 0.0, null);
        var bot = MakeNode(_section, domain, g, Math.PI, 0.0, null);

        // Нормировка по грубому проходу четырёх меридианов (без адаптации; результат отбрасывается)
        var pre = Run(domain, g, [0, Math.PI / 2, Math.PI, 1.5 * Math.PI], null, ct);
        double mRef = pre.SelectMany(m => m).Select(n => Math.Sqrt(n.Force.Mx * n.Force.Mx + n.Force.My * n.Force.My)).DefaultIfEmpty(0).Max();
        double nRef = Math.Abs(top.Force.N - bot.Force.N);
        var scale = new ForcePoint(1 / (nRef > 0 ? nRef : 1), 1 / (mRef > 0 ? mRef : 1), 1 / (mRef > 0 ? mRef : 1));

        int n0 = Math.Max(_opt.InitialMeridians, 4);
        var phis0 = Enumerable.Range(0, n0).Select(k => TwoPi * k / n0).ToArray();
        var mer = new SortedDictionary<double, List<MeridianNode>>();
        var first = Run(domain, g, phis0, scale, ct);
        for (int k = 0; k < n0; k++) mer[phis0[k]] = first[k];

        var log = new List<(int Count, double MaxDeviation)>();
        var intervals = Enumerable.Range(0, n0).Select(k => (A: phis0[k], B: k + 1 < n0 ? phis0[k + 1] : TwoPi)).ToList();
        while (intervals.Count > 0 && _opt.RefineTol > 0)
        {
            int room = _opt.MaxMeridians - mer.Count;
            if (room <= 0) break;
            if (intervals.Count > room) intervals = intervals.Take(room).ToList();
            var mids = intervals.Select(iv => 0.5 * (iv.A + iv.B)).ToArray();
            var fresh = Run(domain, g, mids, scale, ct);
            var next = new List<(double Dev, double A, double B)>();
            double maxDev = 0;
            for (int k = 0; k < mids.Length; k++)
            {
                mer[mids[k]] = fresh[k];
                var (a, b) = intervals[k];
                double dev = MeridianStitcher.StripDeviation(top, bot, mer[a], mer[b >= TwoPi ? 0.0 : b], fresh[k], scale);
                maxDev = Math.Max(maxDev, dev);
                if (!(dev <= _opt.RefineTol)) { next.Add((dev, a, mids[k])); next.Add((dev, mids[k], b)); }
            }
            log.Add((mids.Length, maxDev));
            // при нехватке лимита первыми дробятся участки с наибольшим отклонением
            intervals = next.OrderByDescending(e => e.Dev).Select(e => (e.A, e.B)).ToList();
        }

        return MeridianStitcher.Assemble(top, bot, mer.Keys.ToList(), mer.Values.ToList(), log);
    }

    Scales MakeScales(StrainDomain d)
    {
        double l = d.Points.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
        // Масштаб растяжения — наименьший конечный верхний предел: у ПС1 его дают стержни (бетон трещиноват),
        // у ПС2 — бетон (εbt2).
        var ten = d.Points.Where(p => double.IsFinite(p.EpsMax) && p.EpsMax > 0).Select(p => p.EpsMax).ToList();
        if (ten.Count == 0)
            throw new InvalidOperationException(
                $"Поверхность «{_criterion.Name}» не замкнута: в сечении нет арматуры, ограничивающей растяжение.");
        double sTen = ten.Min();
        double sComp = d.Points.Where(p => p.Kind == StrainPointKind.Concrete).Max(p => Math.Abs(p.EpsB2));
        return new Scales(sTen, sComp, 0.5 * (sTen + sComp), l > 0 ? l : 1);
    }

    MeridianNode MakeNode(CrossSection sec, StrainDomain d, Scales g, double theta, double phi, KinkTransition? tr)
    {
        var (a, by, bz) = g.Direction(theta, phi);
        var (lam, act) = d.Boundary(a, by, bz);
        if (!double.IsFinite(lam))
            throw new InvalidOperationException(
                $"Поверхность «{_criterion.Name}» не замкнута: луч деформаций θ={theta:F4}, φ={phi:F4} не упирается в предел " +
                "(нет арматуры, ограничивающей растяжение).");
        var k = new Kurvature { e0 = lam * a, ky = lam * by, kz = lam * bz };
        var r = sec.Compute(k, _criterion.Calc, _criterion.Ten, computeStiffness: false);
        return new MeridianNode(theta, k, new ForcePoint(r.N, r.Mx, r.My), act, tr);
    }

    List<List<MeridianNode>> Run(StrainDomain d, Scales g, IReadOnlyList<double> phis, ForcePoint? scale, CancellationToken ct)
    {
        var res = new List<MeridianNode>[phis.Count];
        if (_opt.Parallel && phis.Count > 1)
        {
            try
            {
                Parallel.For(0, phis.Count, new ParallelOptions { CancellationToken = ct },
                    i => res[i] = Meridian(_section.CloneForCalc(), d, g, phis[i], scale));
            }
            catch (AggregateException ae) when (ae.InnerExceptions.Count > 0)
            {
                ExceptionDispatchInfo.Capture(ae.InnerExceptions[0]).Throw();
                throw;
            }
        }
        else
        {
            for (int i = 0; i < phis.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                res[i] = Meridian(_section, d, g, phis[i], scale);
            }
        }
        return res.ToList();
    }

    List<MeridianNode> Meridian(CrossSection sec, StrainDomain d, Scales g, double phi, ForcePoint? scale)
    {
        ActiveConstraint Key(double th)
        {
            var (a, by, bz) = g.Direction(th, phi);
            return d.Boundary(a, by, bz).Active;
        }

        void Kinks(double ta, ActiveConstraint ka, double tb, ActiveConstraint kb, int depth, List<(double T, KinkTransition? Tr)> outList)
        {
            if (ka.Equals(kb) || depth > MaxKinksPerInterval) return;
            double lo = ta, hi = tb;
            for (int it = 0; it < BisectIters; it++)
            {
                double mid = 0.5 * (lo + hi);
                if (Key(mid).Equals(ka)) lo = mid; else hi = mid;
            }
            var kr = Key(hi);
            outList.Add((0.5 * (lo + hi), new KinkTransition(ka, kr)));
            Kinks(hi, kr, tb, kb, depth + 1, outList);
        }

        int n = Math.Max(_opt.ThetaIntervals, 4);
        var thetas = new List<(double T, KinkTransition? Tr)>();
        double prevT = 0; ActiveConstraint prevK = default;
        for (int j = 1; j < n; j++)
        {
            double t = Math.PI * j / n;
            var k = Key(t);
            if (j > 1 && !k.Equals(prevK)) Kinks(prevT, prevK, t, k, 0, thetas);
            thetas.Add((t, null));
            prevT = t; prevK = k;
        }

        var nodes = thetas.Select(e => MakeNode(sec, d, g, e.T, phi, e.Tr)).ToList();
        if (scale is not { } s || _opt.RefineTol <= 0) return nodes;

        // Адаптация по θ: середина интервала против хорды в нормированных усилиях
        IEnumerable<MeridianNode> Refine(MeridianNode a, MeridianNode b, int depth)
        {
            if (depth >= _opt.MaxThetaDepth) return [];
            var m = MakeNode(sec, d, g, 0.5 * (a.T + b.T), phi, null);
            double dev = SurfaceMeshGeometry.PointSegmentDistance(m.Force.Scale(s), a.Force.Scale(s), b.Force.Scale(s));
            if (dev <= _opt.RefineTol) return [m];
            return Refine(a, m, depth + 1).Append(m).Concat(Refine(m, b, depth + 1)).ToList();
        }

        // Интервалы у полюсов дробятся тоже: у ПС2 на интервале «растянутый полюс — первый узел» лежит почти вся
        // область трещинообразования при растяжении, и хорда до первого кольца срезала её (до 0,8 % M_max, не
        // убывая с RefineTol). Сами полюса в меридиан не входят — они общие вершины сетки.
        var top = MakeNode(sec, d, g, 0.0, phi, null);
        var bot = MakeNode(sec, d, g, Math.PI, phi, null);
        var refined = Refine(top, nodes[0], 0).ToList();
        refined.Add(nodes[0]);
        for (int i = 0; i + 1 < nodes.Count; i++)
        {
            refined.AddRange(Refine(nodes[i], nodes[i + 1], 0));
            refined.Add(nodes[i + 1]);
        }
        refined.AddRange(Refine(nodes[^1], bot, 0));
        return refined;
    }

}
