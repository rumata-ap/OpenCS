namespace CScore.LimitSurface;

/// <summary>Узел границы на колонне (N, ψ): модуль момента m*, точка, плоскость, активное ограничение.</summary>
/// <param name="M">Модуль полного момента на границе, кН·м.</param>
/// <param name="Plane">Полная пост-трещинная плоскость (ten = false); у <see cref="ConstraintKind.CrackBody"/> — плоскость тела ПС2.</param>
/// <param name="Converged">Ньютон сошёлся во всех пробах до границы; иначе M — последний сошедшийся (нижняя оценка).</param>
/// <param name="ByAxialForce">Точка трещинообразования — на луче полной нагрузки (N за растянутым полюсом тела ПС2).</param>
internal readonly record struct ColumnPoint(double M, ForcePoint Force, Kurvature Plane, ActiveConstraint Active,
    bool Converged, bool ByAxialForce = false);

/// <summary>
/// Построитель поверхности ширины раскрытия трещин по колоннам (N, ψ): на колонне — постоянная N и
/// направление полного момента (cos ψ, sin ψ); граница — наименьший |M| ≥ Mcrc, при котором
/// g(m) = max(acrc,long/ult, acrc,short/ult, σs/Rs,ser, εb/εb,ult) = 1. Mcrc, плоскость трещинообразования
/// и вид границы тела ПС2 — по сетке образования трещин (<see cref="CrackStrainCriterion"/>).
/// </summary>
public sealed class AcrcColumnMesher
{
    const int BisectIters = 60;
    const int ExpandIters = 40;

    readonly CrossSection _section;
    readonly AcrcSurfaceCriterion _criterion;
    readonly LimitSurfaceOptions _opt;
    readonly double _mRef;
    readonly ColumnSolver _main;

    /// <summary>Создаёт построитель; строит тело образования трещин, если оно не передано.</summary>
    public AcrcColumnMesher(CrossSection section, AcrcSurfaceCriterion criterion, LimitSurfaceOptions? options = null,
        LimitSurfaceMesh? crackMesh = null, CancellationToken ct = default)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _criterion = criterion ?? throw new ArgumentNullException(nameof(criterion));
        _opt = options ?? new LimitSurfaceOptions();
        CrackMesh = crackMesh ?? new StrainDomainMesher(section, criterion.CrackCriterion, _opt).Build(ct);
        double mMax = CrackMesh.Vertices.Select(v => Math.Sqrt(v.Mx * v.Mx + v.My * v.My)).DefaultIfEmpty(0).Max();
        _mRef = mMax > 0 ? mMax : 1.0;
        _main = new ColumnSolver(this, section);
    }

    /// <summary>Тело образования трещин (ПС2), по которому берутся Mcrc и плоскость трещинообразования.</summary>
    public LimitSurfaceMesh CrackMesh { get; }

    /// <summary>Наибольший модуль момента тела ПС2 — масштаб моментов построителя, кН·м.</summary>
    public double MomentScale => _mRef;

    /// <summary>Граница колонны (N, ψ); φ3 — по знаку N, если не задан.</summary>
    internal ColumnPoint Column(double n, double psi, double? phi3 = null) => _main.Column(n, psi, phi3);

    /// <summary>g(m) на колонне — для проверки монотонности; NaN — расчёт не сошёлся или трещин нет.</summary>
    internal double ConditionAt(double n, double psi, double m, double? phi3 = null) => _main.ConditionAt(n, psi, m, phi3);

    /// <summary>Расчёт колонн на одном экземпляре сечения (не потокобезопасен).</summary>
    sealed class ColumnSolver(AcrcColumnMesher owner, CrossSection section)
    {
        readonly CrackWidthCore _core = owner._criterion.CreateCore(section);

        /// <summary>Проба g(m): результат ядра и вид наибольшего условия.</summary>
        readonly record struct Probe(bool Ok, double G, ConstraintKind Kind, CrackWidthResult? Result);

        /// <summary>Трещинообразование на колонне по телу ПС2 (режим Moment).</summary>
        readonly record struct Start(bool Axial, bool CompressionBody, double Mcrc, ForcePoint Capacity,
            Kurvature CrackPlane, ActiveConstraint Active, CrackOnset? Onset);

        Start MomentStart(double n, double c, double s)
        {
            double r = 10.0 * owner._mRef;
            var u = owner.CrackMesh.Utilization(n, r * c, r * s, UtilizationMode.Moment);
            if (u.Capacity is not { } cap || u.StrainPlane is not { } plane)
                return new Start(Axial: true, false, 0.0, new ForcePoint(n, 0, 0), default, default, null);
            var act = u.Active ?? default;
            double mcrc = Math.Sqrt(cap.Mx * cap.Mx + cap.My * cap.My);
            if (act.Side < 0)
                return new Start(false, CompressionBody: true, mcrc, cap, plane, act, null);
            var onset = _core.Onset(cap.N, cap.Mx, cap.My, plane, byAxialForce: false);
            return new Start(false, false, mcrc, cap, plane, act, onset);
        }

        /// <summary>Точка трещинообразования на луче полной нагрузки (N за растянутым полюсом тела ПС2).</summary>
        CrackOnset? AxialOnset(double n, double mx, double my)
        {
            var u = owner.CrackMesh.Utilization(n, mx, my, UtilizationMode.All);
            if (u.Inside || u.Capacity is not { } cap || u.StrainPlane is not { } plane) return null;
            return _core.Onset(cap.N, cap.Mx, cap.My, plane, byAxialForce: true);
        }

        Probe Evaluate(Start st, double n, double c, double s, double m, double phi3, Probe seed)
        {
            var onset = st.Axial ? AxialOnset(n, m * c, m * s) : st.Onset;
            if (onset is null) return default;
            double k = owner._criterion.LongShare;
            var res = _core.Evaluate(onset, n, k * m * c, k * m * s, m * c, m * s, phi3,
                seed.Result?.PlaneLong, seed.Result?.PlaneTotal);
            if (!res.Cracked) return default;
            var conds = new (double G, ConstraintKind Kind)[]
            {
                (res.AcrcLong / owner._criterion.AcrcUltLong, ConstraintKind.AcrcLong),
                (res.AcrcShort / owner._criterion.AcrcUltShort, ConstraintKind.AcrcShort),
                (res.UtilSteel, ConstraintKind.SteelStress),
                (res.UtilConcrete, ConstraintKind.ConcreteCompression),
            };
            var worst = conds.MaxBy(x => x.G);
            return new Probe(true, worst.G, worst.Kind, res);
        }

        public double ConditionAt(double n, double psi, double m, double? phi3)
        {
            double c = Math.Cos(psi), s = Math.Sin(psi);
            var st = MomentStart(n, c, s);
            if (st.CompressionBody || (!st.Axial && st.Onset is null)) return double.NaN;
            var p = Evaluate(st, n, c, s, m, phi3 ?? Phi3(n), default);
            return p.Ok ? p.G : double.NaN;
        }

        static double Phi3(double n) => n > 0 ? 1.2 : 1.0;

        static ActiveConstraint KindConstraint(ConstraintKind kind) =>
            new(-1, kind == ConstraintKind.ConcreteCompression ? -1 : 1, false, kind);

        ColumnPoint Point(double n, double c, double s, double m, Kurvature plane, ActiveConstraint act, bool conv, bool axial) =>
            new(m, new ForcePoint(n, m * c, m * s), plane, act, conv, axial);

        public ColumnPoint Column(double n, double psi, double? phi3Override)
        {
            double c = Math.Cos(psi), s = Math.Sin(psi);
            double phi3 = phi3Override ?? Phi3(n);
            var st = MomentStart(n, c, s);
            var crackBody = st.Active with { Kind = ConstraintKind.CrackBody };

            // Граница тела ПС2 — сжатие бетона раньше трещины: дальше по моменту не пройти.
            if (st.CompressionBody)
                return Point(n, c, s, st.Mcrc, st.CrackPlane, crackBody, true, false);
            if (!st.Axial && st.Onset is null)
                return Point(n, c, s, st.Mcrc, st.CrackPlane, crackBody, false, false);

            double m0 = st.Axial ? 0.0 : st.Mcrc * (1.0 + 1e-9);
            var lo = Evaluate(st, n, c, s, m0, phi3, default);
            if (!lo.Ok)
                return Point(n, c, s, st.Mcrc, st.CrackPlane, crackBody, false, st.Axial);
            // Раскрытие сразу при образовании трещин превышает предел — граница совпадает с телом ПС2.
            if (lo.G >= 1.0)
            {
                var plane = st.Axial ? lo.Result!.PlaneTotal ?? default : st.CrackPlane;
                var act = st.Axial ? KindConstraint(ConstraintKind.CrackBody) : crackBody;
                return Point(n, c, s, m0, plane, act, true, st.Axial);
            }

            double mLo = m0, mHi = double.NaN;
            Probe hi = default;
            bool hiFailed = false;
            double step = Math.Max(st.Mcrc, 0.05 * owner._mRef);
            for (int it = 0; it < ExpandIters; it++)
            {
                double m = mLo + step;
                var p = Evaluate(st, n, c, s, m, phi3, lo);
                if (!p.Ok) { mHi = m; hiFailed = true; break; }
                if (p.G >= 1.0) { mHi = m; hi = p; break; }
                mLo = m; lo = p; step *= 2.0;
            }
            if (double.IsNaN(mHi))
                return Point(n, c, s, mLo, lo.Result!.PlaneTotal ?? default, KindConstraint(lo.Kind), false, st.Axial);

            double tol = 1e-6 * owner._mRef;
            for (int it = 0; it < BisectIters && mHi - mLo > tol; it++)
            {
                double m = 0.5 * (mLo + mHi);
                var p = Evaluate(st, n, c, s, m, phi3, lo);
                if (!p.Ok) { mHi = m; hiFailed = true; }
                else if (p.G >= 1.0) { mHi = m; hi = p; hiFailed = false; }
                else { mLo = m; lo = p; }
            }

            // Верхняя граница — несошедшийся расчёт: граница не найдена, берётся последняя сошедшаяся проба.
            if (hiFailed || !hi.Ok)
                return Point(n, c, s, mLo, lo.Result!.PlaneTotal ?? default, KindConstraint(lo.Kind), false, st.Axial);
            return Point(n, c, s, mHi, hi.Result!.PlaneTotal ?? default, KindConstraint(hi.Kind), true, st.Axial);
        }
    }
}
