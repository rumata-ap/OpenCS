using CScore.Sp63.Normal;

namespace CScore.Sp63.CrackWidth;

/// <summary>Полные приведённые характеристики полосового профиля (п. 8.2.12).</summary>
/// <param name="Area">Ared — площадь приведённого сечения, м².</param>
/// <param name="Centroid">Расстояние от сжатой грани до центра тяжести приведённого сечения, м.</param>
/// <param name="Inertia">Ired — момент инерции приведённого сечения относительно его центра тяжести, м⁴.</param>
/// <param name="DistanceToTensionEdge">yt — расстояние от центра тяжести до растянутого волокна бетона, м.</param>
/// <param name="Wred">Wred = Ired / yt — упругий момент сопротивления по растянутой зоне, м³.</param>
/// <param name="Ex">ex = Wred / Ared = Ired / St,red — расстояние до ядровой точки, м.</param>
public sealed record Sp63SlsFullProperties(
    double Area,
    double Centroid,
    double Inertia,
    double DistanceToTensionEdge,
    double Wred,
    double Ex);

/// <summary>Приведённое сечение с трещиной: бетон сжатой зоны и арматура (п. 8.2.27).</summary>
/// <param name="NeutralAxis">Высота сжатой зоны xm от сжатой грани, м.</param>
/// <param name="Area">Площадь приведённого сечения с трещиной, м².</param>
/// <param name="Centroid">Расстояние от сжатой грани до центра тяжести сечения с трещиной, м.</param>
/// <param name="Inertia">Момент инерции сечения с трещиной относительно его центра тяжести, м⁴.</param>
public sealed record Sp63SlsCrackedProperties(
    double NeutralAxis,
    double Area,
    double Centroid,
    double Inertia);

/// <summary>
/// Одна составляющая ширины раскрытия трещин: Mcrc, xm, σs, σs,crc, ψs, ls и acrc
/// для ориентированного полосового профиля. Координаты и длины — в метрах, напряжения — в кПа,
/// <see cref="Acrc"/> — в миллиметрах.
/// </summary>
public sealed class Sp63SlsCrackTermResult
{
    /// <summary>Момент образования трещин Mcrc, кН·м (модуль при заданной ориентации).</summary>
    public double Mcrc { get; init; }

    /// <summary>Упругопластический момент сопротивления Wpl, м³ (п. 8.2.11).</summary>
    public double Wpl { get; init; }

    /// <summary>Коэффициент пластичности γ в Wpl = γ·Wred; NaN для общей ветки п. 8.2.10.</summary>
    public double Gamma { get; init; } = double.NaN;

    /// <summary>Образуются ли трещины (M &gt; Mcrc).</summary>
    public bool Cracked { get; init; }

    /// <summary>Средняя высота сжатой зоны xm с поправкой (8.154), м; ≤ 0 — сквозное растяжение.</summary>
    public double Xm { get; init; }

    /// <summary>Плечо zs = h0 − xm/3, м.</summary>
    public double Zs { get; init; }

    /// <summary>Напряжение в растянутой арматуре σs, кПа (п. 8.2.16, ф. 8.134/8.135).</summary>
    public double SigmaS { get; init; }

    /// <summary>Напряжение σs,crc сразу после образования трещин, кПа (п. 8.2.18).</summary>
    public double SigmaSCrc { get; init; }

    /// <summary>Коэффициент ψs по (8.137)/(8.138) с ограничением [0,1; 1].</summary>
    public double PsiS { get; init; }

    /// <summary>Базовое расстояние между трещинами ls, м (п. 8.2.17).</summary>
    public double Ls { get; init; }

    /// <summary>Ширина раскрытия acrc, мм (п. 8.2.15).</summary>
    public double Acrc { get; init; }

    /// <summary>Полные приведённые характеристики профиля, использованные для Mcrc.</summary>
    public Sp63SlsFullProperties Full { get; init; } = null!;

    /// <summary>Приведённое сечение с трещиной; <see langword="null"/> при сквозном растяжении.</summary>
    public Sp63SlsCrackedProperties? CrackedProperties { get; init; }
}

/// <summary>
/// Общий SLS solver приведённого полосового профиля: полные/треснувшие приведённые характеристики,
/// Mcrc (п. 8.2.11 — для прямоугольника и тавра с полкой в сжатой зоне через Wpl = γ·Wred;
/// п. 8.2.10 — для двутавра и тавра с полкой в растянутой зоне через стрессовую эпюру), σs, ψs,
/// ls и acrc. Единственная точка расчёта этих величин для checker'ов ширины трещин и
/// кривизны/прогиба. Координаты и длины — в метрах, усилия — в кН/кН·м, напряжения — в кПа.
/// </summary>
public static class Sp63SlsSectionSolver
{
    /// <summary>
    /// Предельная относительная деформация крайнего растянутого волокна бетона εb1,0 при
    /// кратковременном действии нагрузки (пп. 8.2.10, 8.1.30) для стрессовой эпюры Mcrc.
    /// </summary>
    public const double EpsB1T0 = 0.00015;

    /// <summary>
    /// Полные приведённые характеристики профиля с коэффициентом приведения арматуры
    /// <paramref name="alpha"/>: площадь, центр тяжести от сжатой грани, инерция относительно
    /// центра тяжести, Wred = Ired/yt и ex = Wred/Ared.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Коэффициент некорректен.</exception>
    public static Sp63SlsFullProperties ComputeFullProperties(
        Sp63SlsSectionGeometry geometry, double alpha)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!double.IsFinite(alpha) || alpha < 0.0)
            throw new ArgumentOutOfRangeException(nameof(alpha),
                "Коэффициент приведения должен быть конечным и неотрицательным.");

        double h = geometry.Height;
        double asT = geometry.TensionLayer.Area, ysT = geometry.TensionLayer.Coordinate;
        double asC = geometry.CompressionLayer.Area, ysC = geometry.CompressionLayer.Coordinate;

        double area = geometry.Area(0.0, h) + alpha * (asT + asC);
        double first = geometry.FirstMoment(0.0, h) + alpha * (asT * ysT + asC * ysC);
        double centroid = first / area;
        double secondAtZero = geometry.SecondMoment(0.0, h) +
            alpha * (asT * ysT * ysT + asC * ysC * ysC);
        double inertia = secondAtZero - area * centroid * centroid;
        double distanceToTensionEdge = h - centroid;
        double wred = inertia / distanceToTensionEdge;
        double ex = wred / area;
        return new Sp63SlsFullProperties(area, centroid, inertia,
            distanceToTensionEdge, wred, ex);
    }

    /// <summary>
    /// Приведённое сечение с трещиной: бетон полос на участке [0; xm] и оба уровня арматуры
    /// с коэффициентами <paramref name="alphaTension"/> (растянутый) и
    /// <paramref name="alphaCompression"/> (сжатый) — обобщение ф. (8.148).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">xm некорректен.</exception>
    public static Sp63SlsCrackedProperties ComputeCrackedProperties(
        Sp63SlsSectionGeometry geometry, double xm,
        double alphaTension, double alphaCompression)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!double.IsFinite(xm) || xm <= 0.0 || xm > geometry.Height + Sp63SlsSectionGeometry.Tolerance)
            throw new ArgumentOutOfRangeException(nameof(xm),
                "Высота сжатой зоны должна быть конечной и лежать в (0; Height].");
        if (!double.IsFinite(alphaTension) || alphaTension < 0.0 ||
            !double.IsFinite(alphaCompression) || alphaCompression < 0.0)
            throw new ArgumentOutOfRangeException(nameof(alphaTension),
                "Коэффициенты приведения должны быть конечными и неотрицательными.");

        double asT = geometry.TensionLayer.Area, ysT = geometry.TensionLayer.Coordinate;
        double asC = geometry.CompressionLayer.Area, ysC = geometry.CompressionLayer.Coordinate;

        double area = geometry.Area(0.0, xm) + alphaTension * asT + alphaCompression * asC;
        double first = geometry.FirstMoment(0.0, xm) +
            alphaTension * asT * ysT + alphaCompression * asC * ysC;
        double centroid = first / area;
        double secondAtZero = geometry.SecondMoment(0.0, xm) +
            alphaTension * asT * ysT * ysT + alphaCompression * asC * ysC * ysC;
        double inertia = secondAtZero - area * centroid * centroid;
        return new Sp63SlsCrackedProperties(xm, area, centroid, inertia);
    }

    /// <summary>
    /// Средняя высота сжатой зоны треснувшего сечения из уравнения статических моментов (8.149):
    /// Sb0(x) = αt·As·(h0 − x) + αc·A's·(a' − x) с Sb0(x) — статический момент сжатого бетона
    /// об уровне x. Для прямоугольника сводится к ф. (8.150)/(8.151). Решается монотонным
    /// бинарным поиском на отрезке [0; h0].
    /// </summary>
    /// <param name="alphaTension">Коэффициент приведения растянутой арматуры αs2.</param>
    /// <param name="alphaCompression">Коэффициент приведения сжатой арматуры αs1.</param>
    public static double ComputeCrackedNeutralAxis(Sp63SlsSectionGeometry geometry,
        double alphaTension, double alphaCompression)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        double h0 = geometry.TensionLayer.Coordinate;
        double aPrime = geometry.CompressionLayer.Coordinate;
        double asT = geometry.TensionLayer.Area;
        double asC = geometry.CompressionLayer.Area;

        double Balance(double x) =>
            x * geometry.Area(0.0, x) - geometry.FirstMoment(0.0, x)
            - alphaTension * asT * (h0 - x) - alphaCompression * asC * (aPrime - x);

        double lo = 0.0, hi = h0;
        for (int i = 0; i < 200; i++)
        {
            double mid = (lo + hi) / 2.0;
            if (Balance(mid) < 0.0) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2.0;
    }

    /// <summary>
    /// Единая точка расчёта одной составляющей ширины раскрытия трещин: Mcrc, xm, σs, σs,crc,
    /// ψs, ls и acrc по пп. 8.2.10–8.2.18 для ориентированного профиля (сжатая грань — y = 0).
    /// Момент передаётся модулем (ориентация задана при построении профиля), продольная сила —
    /// со знаком «+» при растяжении.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Усилия некорректны.</exception>
    public static Sp63SlsCrackTermResult ComputeCrackTerm(
        Sp63SlsSectionGeometry geometry,
        MaterialChars concrete,
        MaterialChars rebar,
        double moment,
        double axialForce,
        double phi1,
        double phi2,
        double acrcLimMm,
        SigmaSCrcMethod sigmaSCrcMethod,
        WplGammaMethod wplGamma,
        bool oppositeRow = false)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(concrete);
        ArgumentNullException.ThrowIfNull(rebar);
        if (!double.IsFinite(moment) || moment < 0.0)
            throw new ArgumentOutOfRangeException(nameof(moment),
                "Момент передаётся модулем: конечным и неотрицательным.");
        if (!double.IsFinite(axialForce))
            throw new ArgumentOutOfRangeException(nameof(axialForce));
        _ = acrcLimMm;

        double h = geometry.Height;
        double h0 = geometry.TensionLayer.Coordinate;
        double aPrime = geometry.CompressionLayer.Coordinate;
        double asT = geometry.TensionLayer.Area;
        double ds = geometry.TensionLayer.Diameter;

        double eb = concrete.E;
        double rbSer = Math.Abs(concrete.Fc);
        double rbt = concrete.Ft;
        double es = rebar.E;
        double rsSer = Math.Abs(rebar.Ft);
        double ebRed = rbSer / Sp63Curvature.EpsB1RedShort;
        double alphaFull = es / eb;
        double alpha = es / ebRed;

        // Полное приведённое сечение для Mcrc — с начальным модулем Eb (п. 8.2.12).
        var full = ComputeFullProperties(geometry, alphaFull);
        double gamma;
        double wpl;
        if (TryResolveGamma(geometry, wplGamma, asT, out gamma))
            wpl = gamma * full.Wred;
        else
            wpl = ComputeStressBlockWpl(geometry, concrete, rebar);
        double mcrc = rbt * wpl - axialForce * full.Ex;
        if (mcrc < 0.0) mcrc = 0.0;
        bool cracked = moment > mcrc;

        var fullS1 = ComputeFullProperties(geometry, alpha);
        double xM = ComputeCrackedNeutralAxis(geometry, alpha, alpha);

        double SigmaSAtMoment(double m, out double x, out Sp63SlsCrackedProperties? props)
        {
            if (m > 1e-9) x = xM - fullS1.Inertia * axialForce / (fullS1.Area * m);
            else x = axialForce > 1e-9 ? -1.0 : xM;
            if (x > h0) x = h0;
            props = null;
            if (x > 1e-9)
            {
                // Сжатая зона есть: при oppositeRow второй ряд сжат и трещины у его грани нет.
                if (oppositeRow) return 0.0;
                // Ф. (8.134): σs = α·[M·(h0 − yc)/Ired + N/Ared], момент относительно центра
                // тяжести сечения с трещиной — как в ShellSimplSolver.ComputeStripSls.
                var cracked1 = ComputeCrackedProperties(geometry, x, alpha, alpha);
                props = cracked1;
                double mRed = m + axialForce * (h / 2.0 - cracked1.Centroid);
                double sigma = alpha * (mRed * (h0 - cracked1.Centroid) / cracked1.Inertia +
                    axialForce / cracked1.Area);
                return Math.Clamp(sigma, 0.0, rsSer);
            }

            // Сквозное растяжение: бетон выключен, оба ряда арматуры делят N и M (§8.1.19а).
            // Сила ряда, растянутого моментом, F1 = (m + N·(h/2 − a'))/(h0 − a') для основной
            // геометрии и F1 = (m + N·(h0 − h/2))/(h0 − a') для переориентированной (oppositeRow,
            // привязка первого ряда — h − h0 этого вызова); противоположный ряд получает
            // остаток равновесия N − F1.
            double arm = h0 - aPrime;
            double force = arm > 1e-12
                ? (m + axialForce * (oppositeRow ? h0 - h / 2.0 : h / 2.0 - aPrime)) / arm
                : 0.0;
            if (oppositeRow) force = axialForce - force;
            double s = asT > 1e-14 ? force / asT : 0.0;
            return Math.Clamp(s, 0.0, rsSer);
        }

        double sigmaS = SigmaSAtMoment(moment, out double xm, out var crackedProps);
        double zs = h0 - Math.Max(0.0, xm) / 3.0;

        // П. 8.2.18: σs,crc — та же σs при M = Mcrc, либо ф. (8.138) для изгибаемых элементов.
        double sigmaSCrc = sigmaS;
        if (cracked)
        {
            if (sigmaSCrcMethod == SigmaSCrcMethod.CrackingMoment8138)
            {
                double ratio = moment > 1e-9 ? Math.Clamp(mcrc / moment, 0.0, 1.0) : 0.0;
                sigmaSCrc = sigmaS * ratio;
            }
            else
            {
                sigmaSCrc = Math.Min(SigmaSAtMoment(mcrc, out _, out _), sigmaS);
            }
        }

        double psiS = 1.0;
        if (cracked && sigmaS > 1e-3)
        {
            psiS = 1.0 - 0.8 * sigmaSCrc / sigmaS;
            if (psiS < 0.1) psiS = 0.1;
            if (psiS > 1.0) psiS = 1.0;
        }

        // П. 8.2.17: высота растянутой зоны для Abt — по нейтральной оси приведённого сечения
        // БЕЗ трещины (yt), ограничения 2a ≤ xt ≤ 0,5·h0; Abt — фактическая площадь полос.
        double aTens = h - h0;
        double xtCrc = Math.Max(0.0, full.DistanceToTensionEdge);
        double hBt = Math.Min(Math.Max(xtCrc, 2.0 * aTens), h0 / 2.0);
        double abt = geometry.Area(h - hBt, h);
        double lsRaw = 0.5 * abt / asT * ds;
        double lsMin = Math.Max(10.0 * ds, 0.10);
        double lsMax = Math.Min(40.0 * ds, 0.40);
        double ls = Math.Max(lsMin, Math.Min(lsRaw, lsMax));

        double phi3 = axialForce > 1e-3 ? 1.2 : 1.0;
        double acrcMm = 0.0;
        if (cracked && sigmaS > 1e-3)
            acrcMm = phi1 * phi2 * phi3 * psiS * sigmaS / es * ls * 1000.0;

        return new Sp63SlsCrackTermResult
        {
            Mcrc = mcrc,
            Wpl = wpl,
            Gamma = gamma,
            Cracked = cracked,
            Xm = xm,
            Zs = zs,
            SigmaS = sigmaS,
            SigmaSCrc = sigmaSCrc,
            PsiS = psiS,
            Ls = ls,
            Acrc = acrcMm,
            Full = full,
            CrackedProperties = crackedProps
        };
    }

    /// <summary>
    /// Ветка п. 8.2.11 «Wpl = γ·Wred»: для прямоугольника γ берётся из выбранного источника
    /// тем же resolver'ом, что и в <see cref="ShellSimplSolver.ComputeStripSls"/>, для тавра с
    /// одной полкой в сжатой зоне — всегда 1,3. Двутавр имеет обе полки, поэтому
    /// <see cref="Sp63SlsSectionGeometry.HasCompressionFlange"/> сам по себе не даёт права на
    /// упрощение: для двутавра и тавра с полкой в растянутой зоне — общая эпюра п. 8.2.10.
    /// </summary>
    static bool TryResolveGamma(Sp63SlsSectionGeometry geometry, WplGammaMethod method,
        double asT, out double gamma)
    {
        if (geometry.ShapeKind == Sp63NormalShapeKind.Rectangular)
        {
            gamma = ShellSimplSolver.ResolveWplGamma(method, asT,
                geometry.Bands[0].Width, geometry.Height);
            return true;
        }
        if (geometry.HasCompressionFlange && !geometry.HasTensionFlange)
        {
            gamma = 1.3;
            return true;
        }
        gamma = double.NaN;
        return false;
    }

    /// <summary>
    /// Упругопластический момент сопротивления Wpl по стрессовой эпюре п. 8.2.10 для двутавра и
    /// тавра с полкой в растянутой зоне: плоские сечения, деформация крайнего растянутого бетона
    /// <see cref="EpsB1T0"/>, треугольная эпюра сжатого бетона с модулем Rb,ser/0,0015 (6.9),
    /// трапециевидная эпюра растянутого бетона с ограничением Rbt,ser, упругая арматура.
    /// Положение нейтральной оси определяется равновесием чистого изгиба, Wpl = M/Rbt,ser.
    /// </summary>
    static double ComputeStressBlockWpl(Sp63SlsSectionGeometry geometry,
        MaterialChars concrete, MaterialChars rebar)
    {
        double h = geometry.Height;
        double rbSer = Math.Abs(concrete.Fc);
        double rbt = concrete.Ft;
        double ec = rbSer / Sp63Curvature.EpsB1RedShort;
        double es = rebar.E;
        double asT = geometry.TensionLayer.Area, ysT = geometry.TensionLayer.Coordinate;
        double asC = geometry.CompressionLayer.Area, ysC = geometry.CompressionLayer.Coordinate;

        // Вклад участка [y0; y1] ширины w при нейтральной оси x: напряжение растяжения
        // σ(y) = Ec·k·(y − x) до насыщения Rbt,ser, далее постоянное Rbt,ser; ниже нейтральной
        // та же формула даёт сжатие (отрицательную силу растяжения).
        (double Force, double Moment) Segment(double y0, double y1, double w, double x, double k)
        {
            double ySat = x + rbt / (ec * k);
            double force = 0.0, moment = 0.0;
            var cuts = new List<double> { y0 };
            if (x > y0 && x < y1) cuts.Add(x);
            if (ySat > y0 && ySat < y1) cuts.Add(ySat);
            cuts.Add(y1);
            cuts.Sort();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double a = cuts[i], c = cuts[i + 1], mid = (a + c) / 2.0;
                if (mid >= ySat)
                {
                    force += w * rbt * (c - a);
                    moment += w * rbt * (c * c - a * a) / 2.0;
                }
                else
                {
                    force += w * ec * k * ((c * c - a * a) / 2.0 - x * (c - a));
                    moment += w * ec * k *
                        ((c * c * c - a * a * a) / 3.0 - x * (c * c - a * a) / 2.0);
                }
            }
            return (force, moment);
        }

        (double Force, double Moment) Equilibrium(double x)
        {
            double k = EpsB1T0 / (h - x);
            double force = es * k * ((ysT - x) * asT + (ysC - x) * asC);
            double moment = es * k * ((ysT - x) * asT * ysT + (ysC - x) * asC * ysC);
            foreach (var band in geometry.Bands)
            {
                var (bandForce, bandMoment) = Segment(band.Start, band.End, band.Width, x, k);
                force += bandForce;
                moment += bandMoment;
            }
            return (force, moment);
        }

        double lo = 0.0, hi = h;
        for (int i = 0; i < 200; i++)
        {
            double mid = (lo + hi) / 2.0;
            if (Equilibrium(mid).Force > 0.0) lo = mid; else hi = mid;
        }
        return Equilibrium((lo + hi) / 2.0).Moment / rbt;
    }
}
