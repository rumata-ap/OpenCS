namespace CScore.ShellSimpl;

/// <summary>Найденное НДС полосы.</summary>
public sealed record StripNdmState(double Eps0, double Kappa, bool Converged, int Iterations, string Reason,
    double N = 0, double M = 0)
{
    public double EpsAt(double z) => Eps0 + Kappa * z;

    /// <summary>Координата нейтральной оси (ε = 0) от срединной плоскости; NaN при κ ≈ 0.</summary>
    public double NeutralAxis => Math.Abs(Kappa) > 1e-14 ? -Eps0 / Kappa : double.NaN;

    /// <summary>Высота сжатой зоны от сжатой грани: h — всё сечение сжато, 0 — сжатия нет.</summary>
    public double CompressionHeight(double h)
    {
        double top = EpsAt(h / 2.0), bot = EpsAt(-h / 2.0);
        if (top >= 0.0 && bot >= 0.0) return 0.0;
        if (top <= 0.0 && bot <= 0.0) return h;
        double comp = Math.Min(top, bot), tens = Math.Max(top, bot);
        return h * -comp / (tens - comp);
    }
}

/// <summary>Шарнир предельной плоскости деформаций (п. 8.1.30).</summary>
public enum StripNdmPivot
{
    None,
    /// <summary>Растянутый ряд на εs,ult.</summary>
    A,
    /// <summary>Сжатая грань на εb2 (двузначная эпюра).</summary>
    B,
    /// <summary>Однозначная эпюра сжатия, εb,ult интерполяцией между εb2 и εb0.</summary>
    C,
}

/// <summary>Результат ПС1 полосы по лучу усилий.</summary>
/// <param name="Lambda">Предельный множитель нагрузки (∞ при нулевой нагрузке).</param>
/// <param name="Eta">Кисп = 1/λ; <see cref="double.MaxValue"/>, если луч не пересекает границу.</param>
/// <param name="TopFace">Шарнир на верхней грани/ряду (B, C — сжатая грань; A — растянутый ряд).</param>
/// <param name="Ultimate">Предельная плоскость деформаций на луче.</param>
public sealed record StripNdmUls(double Lambda, double Eta, StripNdmPivot Pivot, bool TopFace,
    StripNdmState? Ultimate, string Reason = "");

/// <summary>ПС2 одного ряда полосы.</summary>
public sealed record StripNdmSlsRow(double Z, double As, double SigmaS, double SigmaSCrc, double PsiS,
    double HBt, double LsM, double Phi3, double AcrcMm);

/// <summary>Результат ПС2 полосы.</summary>
/// <param name="Mcrc">Момент трещинообразования грани, растянутой моментом (модуль), кН·м/м.</param>
/// <param name="CrackFaceTop">Грань, для которой искался M_crc.</param>
/// <param name="HBt">Высота растянутой зоны этой грани в состоянии трещинообразования (до ограничений п. 8.2.17).</param>
/// <param name="State">НДС сечения с трещиной при (N, M); null, если трещины нет или НДС не найдено.</param>
public sealed record StripNdmSls(double Mcrc, bool CrackFaceTop, bool Cracked, double HBt,
    IReadOnlyList<StripNdmSlsRow> Rows, double AcrcMm, StripNdmState? State, bool Converged, string Reason = "");

/// <summary>
/// Одноосная плоская НДМ полосы 1 м × h: равновесие при заданных (N, M), предельный множитель по
/// лучу усилий (ПС1, п. 8.1.30) и ширина раскрытия трещин по деформационной модели (пп. 8.2.14–8.2.18).
/// </summary>
public static class StripNdm
{
    // ───────────────────────────── равновесие ─────────────────────────────

    /// <summary>
    /// НДС при заданных (N, M): Ньютон 2×2 с демпфированием, при неудаче — вложенный одномерный
    /// поиск (ε₀ по N при фиксированной κ, затем κ по M). Нет решения — <c>Converged = false</c>
    /// с причиной; молчаливого отката на формулы нет.
    /// </summary>
    public static StripNdmState Solve(StripNdmSection s, double n, double m, bool tensionConcrete)
    {
        double fs = s.ForceScale();
        double tolN = 1e-9 * fs, tolM = 1e-9 * fs * s.H;
        var newton = SolveNewton(s, n, m, tensionConcrete, tolN, tolM);
        if (newton.Converged) return newton;
        var nested = SolveNested(s, n, m, tensionConcrete, 1e3 * tolN, 1e3 * tolM);
        return nested with { Iterations = nested.Iterations + newton.Iterations };
    }

    static double Norm(double rn, double rm, double tolN, double tolM) => Math.Abs(rn) / tolN + Math.Abs(rm) / tolM;

    static StripNdmState SolveNewton(StripNdmSection s, double n, double m, bool ten, double tolN, double tolM)
    {
        var el = s.ElasticStiffness();
        double det0 = el.Det;
        double e0 = (el.K11 * n - el.K01 * m) / det0;
        double k = (el.K00 * m - el.K01 * n) / det0;

        var f = s.Integrate(e0, k, ten);
        double rn = n - f.N, rm = m - f.M;
        double norm = Norm(rn, rm, tolN, tolM);
        int it = 0;
        for (; it < 60; it++)
        {
            if (Math.Abs(rn) <= tolN && Math.Abs(rm) <= tolM)
                return new StripNdmState(e0, k, true, it, "", f.N, f.M);

            double k00 = f.K00, k01 = f.K01, k11 = f.K11, det = f.Det;
            if (!(k00 > 0) || !(det > 1e-10 * k00 * k11) || !(k11 > 0))
            {
                // Вырожденная касательная (сечение потекло или раскрылось) — шаг по упругой матрице.
                k00 = el.K00; k01 = el.K01; k11 = el.K11; det = det0;
            }
            double de0 = (k11 * rn - k01 * rm) / det;
            double dk = (k00 * rm - k01 * rn) / det;

            double alpha = 1.0;
            StripNdmResponse fNew = default;
            double rnNew = 0, rmNew = 0, normNew = double.PositiveInfinity;
            for (int ls = 0; ls < 12; ls++)
            {
                fNew = s.Integrate(e0 + alpha * de0, k + alpha * dk, ten);
                rnNew = n - fNew.N; rmNew = m - fNew.M;
                normNew = Norm(rnNew, rmNew, tolN, tolM);
                if (normNew < norm) break;
                alpha *= 0.5;
            }
            if (!(normNew < norm)) break;   // нет спуска — к запасному пути
            e0 += alpha * de0; k += alpha * dk;
            f = fNew; rn = rnNew; rm = rmNew; norm = normNew;
        }
        return new StripNdmState(e0, k, false, it, "Ньютон не сошёлся", f.N, f.M);
    }

    static StripNdmState SolveNested(StripNdmSection s, double n, double m, bool ten, double tolN, double tolM)
    {
        int evals = 0;
        double h = s.H;

        // ε₀ при фиксированной κ: N(ε₀) неубывающая для монотонных диаграмм.
        double? Eps0For(double kappa)
        {
            double lo = -0.1 - Math.Abs(kappa) * h, hi = 0.1 + Math.Abs(kappa) * h;
            double nLo = s.Integrate(lo, kappa, ten).N - n, nHi = s.Integrate(hi, kappa, ten).N - n;
            evals += 2;
            if (nLo > tolN || nHi < -tolN) return null;
            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5 * (lo + hi);
                double r = s.Integrate(mid, kappa, ten).N - n;
                evals++;
                if (Math.Abs(r) <= tolN || hi - lo < 1e-15) return mid;
                if (r < 0) lo = mid; else hi = mid;
            }
            return 0.5 * (lo + hi);
        }

        double? G(double kappa, out double e0)
        {
            e0 = 0;
            var e = Eps0For(kappa);
            if (e == null) return null;
            e0 = e.Value;
            evals++;
            return s.Integrate(e0, kappa, ten).M - m;
        }

        var g0 = G(0.0, out double e00);
        if (g0 == null)
            return new StripNdmState(0, 0, false, evals, "Продольная сила вне несущей способности полосы");
        if (Math.Abs(g0.Value) <= tolM)
            return Final(e00, 0.0);

        double dir = g0.Value < 0 ? 1.0 : -1.0;   // M(κ) возрастает с κ
        double kLo = 0.0, gLo = g0.Value, eLo = e00;
        double step = 1e-4 / h, kHi = 0, gHi = double.NaN, eHi = 0;
        bool bracket = false;
        while (step < 2.0 / h)
        {
            kHi = dir * step;
            var g = G(kHi, out eHi);
            if (g == null) break;
            gHi = g.Value;
            if (Math.Sign(gHi) != Math.Sign(gLo) || Math.Abs(gHi) <= tolM) { bracket = true; break; }
            kLo = kHi; gLo = gHi; eLo = eHi;
            step *= 2.0;
        }
        if (!bracket)
            return new StripNdmState(eLo, kLo, false, evals, "Момент вне несущей способности полосы при данной продольной силе");
        if (Math.Abs(gHi) <= tolM) return Final(eHi, kHi);

        double kA = kLo, kB = kHi, gA = gLo;
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (kA + kB);
            var g = G(mid, out double eMid);
            if (g == null) { kB = mid; continue; }
            if (Math.Abs(g.Value) <= tolM || Math.Abs(kB - kA) * h < 1e-15) return Final(eMid, mid);
            if (Math.Sign(g.Value) == Math.Sign(gA)) { kA = mid; gA = g.Value; } else kB = mid;
        }
        var last = G(0.5 * (kA + kB), out double eLast);
        return last != null ? Final(eLast, 0.5 * (kA + kB))
            : new StripNdmState(eLo, kLo, false, evals, "Поиск κ не сошёлся");

        StripNdmState Final(double e0, double kappa)
        {
            var f = s.Integrate(e0, kappa, ten);
            bool ok = Math.Abs(f.N - n) <= tolN && Math.Abs(f.M - m) <= tolM;
            return new StripNdmState(e0, kappa, ok, evals,
                ok ? "" : "Равновесие не найдено: отклик полосы разрывный (срыв растянутого бетона) или за пределом", f.N, f.M);
        }
    }

    // ───────────────────────────── ПС1 ─────────────────────────────

    /// <summary>
    /// Использование деформаций по п. 8.1.30: max(|εb|/εb,ult, εs/εs,ult). Двузначная эпюра —
    /// εb,ult = εb2; однозначная — εb2 − (εb2 − εb0)·ε₁/ε₂.
    /// </summary>
    public static double StrainUtilization(StripNdmSection s, StripNdmState st, StripNdmLimits lim)
    {
        double top = st.EpsAt(s.H / 2.0), bot = st.EpsAt(-s.H / 2.0);
        double e2 = Math.Min(top, bot), e1 = Math.Max(top, bot);
        double uc = 0.0;
        if (e2 < 0.0)
        {
            double ult = e1 >= 0.0 ? lim.EpsB2 : lim.EpsB2 - (lim.EpsB2 - lim.EpsB0) * Math.Clamp(e1 / e2, 0.0, 1.0);
            uc = -e2 / ult;
        }
        double us = 0.0;
        foreach (var row in s.Rows)
            if (row.As > 0.0) us = Math.Max(us, st.EpsAt(row.Z) / lim.EpsSUlt);
        return Math.Max(uc, us);
    }

    readonly record struct Segment(StripNdmPivot Pivot, bool Top, Func<double, (double Top, double Bot)> Plane);

    /// <summary>
    /// Замкнутый контур предельных плоскостей (п. 8.1.30) в деформациях граней: шарнир A у нижнего
    /// ряда, B у верхней грани, C (однозначное сжатие) сверху и снизу, B у нижней грани, A у верхнего
    /// ряда. Ряд со стороны, где арматуры нет, заменяется условным рядом на грани с тем же εs,ult —
    /// это лишь сужает контур (в запас).
    /// </summary>
    static Segment[] Contour(StripNdmSection s, StripNdmLimits lim)
    {
        double h = s.H, half = h / 2.0;
        double zt = half, zb = -half;
        bool hasTop = false, hasBot = false;
        foreach (var r in s.Rows)
        {
            if (r.Z > 0) { if (!hasTop || r.Z > zt) zt = r.Z; hasTop = true; }
            else { if (!hasBot || r.Z < zb) zb = r.Z; hasBot = true; }
        }
        if (!hasTop) zt = half;
        if (!hasBot) zb = -half;
        double eu = lim.EpsSUlt, b2 = lim.EpsB2, b0 = lim.EpsB0;

        // Плоскость через две точки (z, ε) → деформации граней.
        static (double, double) Through(double z1, double e1, double z2, double e2, double half)
        {
            double k = (e2 - e1) / (z2 - z1);
            return (e1 + k * (half - z1), e1 + k * (-half - z1));
        }

        var (uTop, uBot) = Through(zb, eu, zt, eu, half);   // равномерное растяжение рядов

        // A у нижнего ряда: верхняя грань от растяжения через 0 до −εb2.
        (double, double) ABot(double t)
        {
            double et = t < 0.5 ? uTop * (1 - 2 * t) : -b2 * (2 * t - 1);
            return Through(zb, eu, half, et, half);
        }
        var (_, bStartBot) = ABot(1.0);
        // B у верхней грани: высота сжатой зоны от конца A до h (низ от растяжения до 0).
        double xStartTop = h * b2 / (b2 + bStartBot);
        (double, double) BTop(double t)
        {
            double x = xStartTop + (h - xStartTop) * t;
            return (-b2, -b2 + b2 * h / x);
        }
        (double, double) CTop(double t)
        {
            double a = b2 - (b2 - b0) * t;
            return (-a, -a * (b2 - a) / (b2 - b0));
        }
        (double, double) CBot(double t) { var (x, y) = CTop(1 - t); return (y, x); }

        (double, double) ATop(double t)
        {
            double eb = t < 0.5 ? -b2 * (1 - 2 * t) : uBot * (2 * t - 1);
            return Through(zt, eu, -half, eb, half);
        }
        var (aTopStartTop, _) = ATop(0.0);
        double xStartBot = h * b2 / (b2 + aTopStartTop);
        (double, double) BBot(double t)
        {
            double x = h - (h - xStartBot) * t;
            return (-b2 + b2 * h / x, -b2);
        }

        return
        [
            new(StripNdmPivot.A, false, ABot),
            new(StripNdmPivot.B, true, BTop),
            new(StripNdmPivot.C, true, CTop),
            new(StripNdmPivot.C, false, CBot),
            new(StripNdmPivot.B, false, BBot),
            new(StripNdmPivot.A, true, ATop),
        ];
    }

    static StripNdmState PlaneState(double top, double bot, double h, StripNdmResponse f) =>
        new((top + bot) / 2.0, (top - bot) / h, true, 0, "", f.N, f.M);

    /// <summary>
    /// ПС1: предельный множитель λ на луче (N, M) и Кисп = 1/λ. Граница строится по контуру предельных
    /// плоскостей (<see cref="Contour"/>); пересечение с лучом ищется по углу в нормированных
    /// координатах (N·h/6, M). Если контур не звёздный и пересечений несколько — берётся наименьший λ.
    /// </summary>
    public static StripNdmUls UltimateFactor(StripNdmSection s, double n, double m, StripNdmLimits lim, int samplesPerSegment = 24)
    {
        double h = s.H, sc = h / 6.0;
        double tu = n * sc, tv = m;
        double tr = Math.Sqrt(tu * tu + tv * tv);
        if (tr < 1e-12)
            return new StripNdmUls(double.PositiveInfinity, 0.0, StripNdmPivot.None, false, null, "нулевая нагрузка");
        double phi = Math.Atan2(tv, tu);

        var segs = Contour(s, lim);
        double bestLambda = double.PositiveInfinity;
        StripNdmPivot bestPivot = StripNdmPivot.None;
        bool bestTop = false;
        StripNdmState? bestState = null;

        // Угол точки контура относительно луча, (−π, π].
        double Angle(Segment seg, double t, out double radius, out StripNdmState st)
        {
            var (top, bot) = seg.Plane(t);
            var f = s.Integrate((top + bot) / 2.0, (top - bot) / h, false);
            st = PlaneState(top, bot, h, f);
            double u = f.N * sc, v = f.M;
            radius = Math.Sqrt(u * u + v * v);
            double d = Math.Atan2(v, u) - phi;
            while (d <= -Math.PI) d += 2 * Math.PI;
            while (d > Math.PI) d -= 2 * Math.PI;
            return d;
        }

        foreach (var seg in segs)
        {
            double tPrev = 0, dPrev = Angle(seg, 0, out double rPrev, out _);
            for (int i = 1; i <= samplesPerSegment; i++)
            {
                double t = (double)i / samplesPerSegment;
                double d = Angle(seg, t, out double r, out _);
                // Пересечение — смена знака угла без перескока через противоположное направление.
                bool cross = (dPrev <= 0 && d >= 0 || dPrev >= 0 && d <= 0) && Math.Abs(d - dPrev) < Math.PI
                    && (rPrev > 1e-12 || r > 1e-12);
                if (cross)
                {
                    double a = tPrev, b = t, da = dPrev;
                    double tm = a, rm = rPrev;
                    StripNdmState stm = null!;
                    for (int j = 0; j < 50; j++)
                    {
                        tm = 0.5 * (a + b);
                        double dm = Angle(seg, tm, out rm, out stm);
                        if (Math.Abs(dm) < 1e-12) break;
                        if (Math.Sign(dm) == Math.Sign(da)) { a = tm; da = dm; } else b = tm;
                    }
                    if (stm == null) Angle(seg, tm, out rm, out stm);
                    double lambda = rm / tr;
                    if (lambda < bestLambda)
                    {
                        bestLambda = lambda; bestPivot = seg.Pivot; bestTop = seg.Top; bestState = stm;
                    }
                }
                tPrev = t; dPrev = d; rPrev = r;
            }
        }

        if (bestState == null)
            return new StripNdmUls(0.0, double.MaxValue, StripNdmPivot.None, false, null,
                "луч усилий не пересекает предельную границу полосы");
        double eta = bestLambda > 1e-12 ? 1.0 / bestLambda : double.MaxValue;
        return new StripNdmUls(bestLambda, eta, bestPivot, bestTop, bestState);
    }

    /// <summary>
    /// Таблица предельного контура полосы для многократной оценки Кисп при разных площадях рядов
    /// (подбор): плоскости контура от площадей не зависят, бетонная часть усилий считается один раз,
    /// арматура входит линейно по площадям.
    /// </summary>
    /// <param name="geometry">Полоса: толщина, ряды (площади не используются), диаграммы.</param>
    public static StripNdmUlsTable Tabulate(StripNdmSection geometry, StripNdmLimits lim, int samplesPerSegment = 128)
    {
        var segs = Contour(geometry, lim);
        var zero = geometry.WithAreas(new double[geometry.Rows.Count]);
        double h = geometry.H;
        int nRows = geometry.Rows.Count;
        int count = segs.Length * samplesPerSegment;
        var nc = new double[count];
        var mc = new double[count];
        var sig = new double[count * nRows];
        var pivots = new StripNdmPivot[count];
        int k = 0;
        foreach (var seg in segs)
        {
            for (int i = 0; i < samplesPerSegment; i++, k++)
            {
                var (top, bot) = seg.Plane((double)i / samplesPerSegment);
                double e0 = (top + bot) / 2.0, kappa = (top - bot) / h;
                var f = zero.Integrate(e0, kappa, false);
                nc[k] = f.N; mc[k] = f.M; pivots[k] = seg.Pivot;
                for (int r = 0; r < nRows; r++)
                    sig[k * nRows + r] = geometry.Rebar.Sig(e0 + kappa * geometry.Rows[r].Z, out _, true, true);
            }
        }
        var z = new double[nRows];
        for (int r = 0; r < nRows; r++) z[r] = geometry.Rows[r].Z;
        return new StripNdmUlsTable(h, z, nc, mc, sig, pivots);
    }

    // ───────────────────────────── ПС2 ─────────────────────────────

    /// <summary>
    /// ПС2 по деформационной модели: M_crc (п. 8.2.14) — плоскость с растянутой гранью на εbt,ult
    /// при той же N; σs — из НДС сечения с трещиной (растянутый бетон выключен, п. 8.2.16); σs,crc
    /// — то же при M = M_crc (п. 8.2.18) либо по (8.138); ψs, l_s, φ3, a_crc — пп. 8.2.15–8.2.17.
    /// Сквозное растяжение даёт a_crc для каждого растянутого ряда.
    /// </summary>
    /// <param name="s">Полоса с диаграммами для ПС2 (вид расчёта N, п. 6.1.26).</param>
    /// <param name="rowDiameters">Диаметры стержней рядов, м (в порядке <see cref="StripNdmSection.Rows"/>).</param>
    /// <param name="es">Модуль упругости арматуры, кПа.</param>
    public static StripNdmSls CrackWidth(StripNdmSection s, double n, double m,
        IReadOnlyList<double> rowDiameters, double es, double phi1, double phi2,
        SigmaSCrcMethod sigmaSCrcMethod = SigmaSCrcMethod.ReleasedConcrete8137)
    {
        double h = s.H, half = h / 2.0;
        double ebt = s.Concrete.It.X[^1];

        double sign;
        if (m > 0) sign = 1.0;
        else if (m < 0) sign = -1.0;
        else
        {
            var z0 = Solve(s, n, 0.0, true);
            sign = !z0.Converged || z0.EpsAt(half) >= z0.EpsAt(-half) ? 1.0 : -1.0;
        }
        bool faceTop = sign > 0;

        // Семейство плоскостей с гранью на εbt,ult: ε₀ = εbt − q·h/2, κ = sign·q, q ≥ 0.
        StripNdmResponse At(double q) => s.Integrate(ebt - q * half, sign * q, true);

        double mcrc, hBtState;
        double planeTop, planeBot;
        bool crackedAtZero = false;
        string reason = "";
        var f0 = At(0.0);
        if (n >= f0.N)
        {
            crackedAtZero = true; mcrc = 0.0;
            planeTop = planeBot = ebt;
        }
        else
        {
            double lo = 0.0, hi = ebt / h;
            int grow = 0;
            while (At(hi).N > n && grow++ < 60) { lo = hi; hi *= 2.0; }
            if (At(hi).N > n)
            {
                reason = "трещина не образуется: сечение исчерпывает сжатие раньше";
                planeTop = planeBot = 0; mcrc = double.MaxValue;
            }
            else
            {
                for (int i = 0; i < 100 && hi - lo > 1e-14 * Math.Max(1.0, hi); i++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (At(mid).N > n) lo = mid; else hi = mid;
                }
                double q = 0.5 * (lo + hi);
                var fq = At(q);
                double mSigned = fq.M;
                double e0 = ebt - q * half, k = sign * q;
                planeTop = e0 + k * half; planeBot = e0 - k * half;
                if (sign * mSigned <= 0.0) { crackedAtZero = true; mcrc = 0.0; }
                else mcrc = sign * mSigned;
            }
        }
        hBtState = TensionZone(planeTop, planeBot, faceTop, h);

        bool cracked = crackedAtZero ? (n > 0 || sign * m > 0) : sign * m > mcrc;
        var rowsOut = new List<StripNdmSlsRow>(s.Rows.Count);
        if (!cracked)
        {
            foreach (var r in s.Rows) rowsOut.Add(new StripNdmSlsRow(r.Z, r.As, 0, 0, 0, 0, 0, 0, 0));
            return new StripNdmSls(mcrc == double.MaxValue ? 0.0 : mcrc, faceTop, false, hBtState, rowsOut, 0.0, null, true, reason);
        }

        var st = Solve(s, n, m, false);
        if (!st.Converged)
        {
            foreach (var r in s.Rows) rowsOut.Add(new StripNdmSlsRow(r.Z, r.As, 0, 0, 0, 0, 0, 0, 0));
            return new StripNdmSls(mcrc, faceTop, true, hBtState, rowsOut, 0.0, st, false,
                "НДС сечения с трещиной не найдено: " + st.Reason);
        }

        StripNdmState? stCrc = null;
        if (sigmaSCrcMethod == SigmaSCrcMethod.ReleasedConcrete8137)
        {
            stCrc = Solve(s, n, sign * mcrc, false);
            if (!stCrc.Converged) { stCrc = null; reason = "σs,crc — по (8.138): НДС при M = M_crc не найдено"; }
        }
        double ratio = Math.Abs(m) > 1e-9 ? Math.Clamp(mcrc / Math.Abs(m), 0.0, 1.0) : 0.0;
        double phi3 = n > 1e-3 ? 1.2 : 1.0;

        double worst = 0.0;
        for (int i = 0; i < s.Rows.Count; i++)
        {
            var r = s.Rows[i];
            double eps = st.EpsAt(r.Z);
            if (r.As <= 0.0 || eps <= 0.0)
            {
                rowsOut.Add(new StripNdmSlsRow(r.Z, r.As, 0, 0, 0, 0, 0, 0, 0));
                continue;
            }
            double sigma = s.Rebar.Sig(eps, out _, true, true);
            if (sigma < 1e-3)
            {
                rowsOut.Add(new StripNdmSlsRow(r.Z, r.As, 0, 0, 0, 0, 0, 0, 0));
                continue;
            }
            double sigmaCrc = stCrc != null
                ? Math.Min(Math.Max(0.0, s.Rebar.Sig(stCrc.EpsAt(r.Z), out _, true, true)), sigma)
                : sigma * ratio;
            double psi = Math.Clamp(1.0 - 0.8 * sigmaCrc / sigma, 0.1, 1.0);

            // П. 8.2.17: высота растянутой зоны — по состоянию трещинообразования, от грани этого ряда;
            // 2a ≤ h_bt ≤ 0,5·h0.
            bool rowTop = r.Z > 0;
            double a = half - Math.Abs(r.Z), h0 = half + Math.Abs(r.Z);
            double xt = TensionZone(planeTop, planeBot, rowTop, h);
            double hBt = Math.Min(Math.Max(xt, 2.0 * a), h0 / 2.0);
            double ds = i < rowDiameters.Count && rowDiameters[i] > 0 ? rowDiameters[i] : 0.012;
            double ls = Math.Clamp(0.5 * hBt / r.As * ds, Math.Max(10.0 * ds, 0.10), Math.Min(40.0 * ds, 0.40));
            double acrc = phi1 * phi2 * phi3 * psi * sigma / es * ls * 1000.0;
            worst = Math.Max(worst, acrc);
            rowsOut.Add(new StripNdmSlsRow(r.Z, r.As, sigma, sigmaCrc, psi, hBt, ls, phi3, acrc));
        }
        return new StripNdmSls(mcrc, faceTop, true, hBtState, rowsOut, worst, st, true, reason);
    }

    /// <summary>Высота растянутой зоны от грани (верх/низ) по деформациям граней.</summary>
    static double TensionZone(double top, double bot, bool fromTop, double h)
    {
        double face = fromTop ? top : bot, other = fromTop ? bot : top;
        if (face <= 0.0) return 0.0;
        if (other >= 0.0) return h;
        return h * face / (face - other);
    }
}
