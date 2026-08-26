using CScore;
using CScore.Fire.Entities;

namespace CScore.Fire;

/// <summary>Входные данные расчёта температурной кривизны.</summary>
/// <param name="Fiber">Огневое фибровое сечение с назначенным снимком температуры.</param>
/// <param name="Def">Огневое сечение — источник граничных условий.</param>
/// <param name="MeshStepM">Шаг сетки, задающий число полос профиля.</param>
/// <param name="NormalizedLimitMin">Нормируемый предел огнестойкости R, мин — для φ₁.</param>
/// <param name="TensionRebarAtHeatedFace">Растянутая арматура расположена у нагреваемой грани.</param>
/// <param name="CompressionZoneMethod">auto, sp468_8_11 или fiber_equilibrium.</param>
public sealed record FireThermalCurvatureInput(
    FireFiberSection Fiber,
    FireSectionDef Def,
    double MeshStepM,
    double NormalizedLimitMin,
    bool TensionRebarAtHeatedFace,
    string CompressionZoneMethod);

/// <summary>Применённые параметры температурного состояния арматуры.</summary>
/// <param name="ClassGroup">Группа класса арматуры по таблице 5.6.</param>
/// <param name="ClassSource">Источник определения группы класса.</param>
/// <param name="TemperatureCelsius">Температура стержня, °C.</param>
/// <param name="AreaM2">Площадь стержней данной записи, м².</param>
/// <param name="GammaSt">Коэффициент γ_st.</param>
/// <param name="GammaStE">Коэффициент γ_st^e.</param>
public sealed record FireRebarTemperatureDetail(
    FireRebarClass ClassGroup,
    string ClassSource,
    double TemperatureCelsius,
    double AreaM2,
    double GammaSt,
    double GammaStE);

/// <summary>Результат расчёта температурной кривизны нагретого сечения.</summary>
public sealed record FireThermalCurvatureResult(
    double ChiT,
    double EpsT,
    double? D,
    double THotConcrete,
    double TColdConcrete,
    double TRebar,
    double HeightM,
    double H0M,
    double AsM2,
    double AlphaBt,
    double AlphaSt,
    double EstPa,
    double XiR,
    double XtM,
    string XtMethod,
    bool XtMethodFallback,
    bool XiCapped,
    double ZM,
    double ZSimplifiedM,
    double Phi1,
    double AxisX,
    double AxisY,
    bool AxisFromInertia,
    bool UniformHeating,
    bool RebarBothFaces,
    bool EpsB2OutOfRange,
    bool AggregateNotSilicate,
    double ProfileQuality,
    string? DUnsupportedReasonKey,
    IReadOnlyList<FireProfileBand> Profile,
    IReadOnlyList<FireRebarTemperatureDetail> RebarDetails);

/// <summary>
/// Температурная кривизна, удлинение оси и жёсткость нагретого сечения
/// по п. 8.44б СП 468, формулы (8.41а)–(8.41д).
/// </summary>
public static class FireThermalCurvature
{
    /// <summary>Перевод модуля и напряжения из кПа в МПа.</summary>
    public const double KpaPerMpa = 1_000.0;

    /// <summary>Перевод напряжения из кПа в Па.</summary>
    public const double PaPerKpa = FireCompressionZone.PaPerKpa;

    /// <summary>φ₁ по п. 8.44б: 0,5 при R ≤ 120, 0,4 при R180, 0,3 при R240.</summary>
    public static double Phi1(double normalizedLimitMin)
        => Sp468Tables.Interp(normalizedLimitMin, [120.0, 180.0, 240.0], [0.5, 0.4, 0.3]);

    /// <summary>Температурная кривизна по формулам (8.41а) и (8.41б).</summary>
    public static double Chi(
        double tRebar, double tColdConcrete, double h0M,
        string aggregateType, bool tensionRebarAtHeatedFace)
    {
        if (h0M <= 0.0) return 0.0;

        double aSt = Sp468Tables.AlphaSt(tRebar) * tRebar;
        double aBt = Sp468Tables.AlphaBt(aggregateType, tColdConcrete) * tColdConcrete;

        return tensionRebarAtHeatedFace ? (aSt - aBt) / h0M : (aBt - aSt) / h0M;
    }

    /// <summary>Температурное удлинение оси элемента по формуле (8.41д).</summary>
    public static double EpsAxial(double tRebar, double tColdConcrete, string aggregateType)
        => (Sp468Tables.AlphaBt(aggregateType, tColdConcrete) * tColdConcrete
          + Sp468Tables.AlphaSt(tRebar) * tRebar) / 2.0;

    /// <summary>Жёсткость по формуле (8.41в) с плечом по (8.41г).</summary>
    public static double Stiffness(double phi1, double estPa, double asM2, double h0M, double xtM)
        => phi1 * estPa * asM2 * (h0M - xtM / 3.0) * (h0M - xtM);

    /// <summary>Полный расчёт по снимку температурного поля.</summary>
    public static FireThermalCurvatureResult Run(FireThermalCurvatureInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var fiber = input.Fiber ?? throw new ArgumentNullException(nameof(input.Fiber));

        FireTemperatureProfileResult profile =
            FireTemperatureProfile.Build(fiber, input.Def, input.MeshStepM);

        string aggregate = fiber.AggregateType;
        bool aggregateNotSilicate = !string.Equals(aggregate, "silicate", StringComparison.OrdinalIgnoreCase);

        var (tensionRebars, bothFaces) = SelectTensionRebars(
            fiber, profile, input.TensionRebarAtHeatedFace);
        if (tensionRebars.Count == 0)
            throw new InvalidOperationException("Не найдена арматура растянутой зоны.");

        double asTotal = tensionRebars.Sum(r => Math.Max(0.0, r.Area));
        if (asTotal <= 0.0)
            throw new InvalidOperationException("Площадь арматуры растянутой зоны не определена.");

        double tRebar = tensionRebars.Sum(r => r.Temperature * Math.Max(0.0, r.Area)) / asTotal;
        double h0 = ComputeH0(fiber, profile, tensionRebars);

        double chi = profile.UniformHeating
            ? 0.0
            : Chi(tRebar, profile.TCold, h0, aggregate, input.TensionRebarAtHeatedFace);
        double eps = EpsAxial(tRebar, profile.TCold, aggregate);

        var equivalent = BuildRebarEquivalent(tensionRebars, asTotal);
        double epsB2 = Sp468Tables.EpsB2Silicate(profile.TCold, out bool epsB2OutOfRange);
        double xiR = FireCompressionZone.XiRFromStrain(equivalent.EpsSel, epsB2);

        FireCompressionZoneResult zone = ResolveCompressionZone(
            input, fiber, profile, equivalent.RsntPa, asTotal, h0, xiR);

        double phi1 = Phi1(input.NormalizedLimitMin);
        double? d = zone.Supported
            ? Stiffness(phi1, equivalent.EstPa, asTotal, h0, zone.XtM)
            : null;

        return new FireThermalCurvatureResult(
            ChiT: chi,
            EpsT: eps,
            D: d,
            THotConcrete: profile.THot,
            TColdConcrete: profile.TCold,
            TRebar: tRebar,
            HeightM: profile.Height,
            H0M: h0,
            AsM2: asTotal,
            AlphaBt: Sp468Tables.AlphaBt(aggregate, profile.TCold),
            AlphaSt: Sp468Tables.AlphaSt(tRebar),
            EstPa: equivalent.EstPa,
            XiR: xiR,
            XtM: zone.XtM,
            XtMethod: zone.Method,
            XtMethodFallback: zone.Fallback,
            XiCapped: zone.XiCapped,
            ZM: h0 - zone.XtM / 3.0,
            ZSimplifiedM: 0.85 * h0,
            Phi1: phi1,
            AxisX: profile.AxisX,
            AxisY: profile.AxisY,
            AxisFromInertia: profile.AxisFromInertia,
            UniformHeating: profile.UniformHeating,
            RebarBothFaces: bothFaces,
            EpsB2OutOfRange: epsB2OutOfRange,
            AggregateNotSilicate: aggregateNotSilicate,
            ProfileQuality: profile.Quality,
            DUnsupportedReasonKey: zone.Supported ? null : zone.UnsupportedReasonKey,
            Profile: profile.Bands,
            RebarDetails: equivalent.Details);
    }

    static FireCompressionZoneResult ResolveCompressionZone(
        FireThermalCurvatureInput input,
        FireFiberSection fiber,
        FireTemperatureProfileResult profile,
        double rsntPa,
        double asM2,
        double h0,
        double xiR)
    {
        string method = (input.CompressionZoneMethod ?? "auto").Trim().ToLowerInvariant();
        double tensionForceN = rsntPa * asM2;

        if (method == "fiber_equilibrium")
            return FireCompressionZone.ByFiberEquilibrium(
                fiber, profile.AxisX, profile.AxisY, tensionForceN, h0, xiR);

        var (widthM, rbntPa) = EstimateWebWidthAndStrength(fiber, profile);
        FireCompressionZoneResult byFormula = FireCompressionZone.ByFormula811(
            rsntPa, asM2, rbntPa, widthM, h0, xiR);

        if (byFormula.Supported || method == "sp468_8_11")
            return byFormula;

        return FireCompressionZone.ByFiberEquilibrium(
            fiber, profile.AxisX, profile.AxisY, tensionForceN, h0, xiR);
    }

    static (double WidthM, double RbntPa) EstimateWebWidthAndStrength(
        FireFiberSection fiber, FireTemperatureProfileResult profile)
    {
        double perpX = -profile.AxisY, perpY = profile.AxisX;

        double sMin = double.MaxValue;
        foreach (var c in fiber.ConcreteElements)
            sMin = Math.Min(sMin, c.Cx * profile.AxisX + c.Cy * profile.AxisY);

        double level = sMin + 0.15 * profile.Height;
        double band = Math.Max(profile.Height / FireTemperatureProfile.MinBands, 1e-4);

        double pMin = double.MaxValue, pMax = double.MinValue;
        double area = 0.0, gammaArea = 0.0, fcArea = 0.0;
        foreach (var c in fiber.ConcreteElements)
        {
            double s = c.Cx * profile.AxisX + c.Cy * profile.AxisY;
            if (Math.Abs(s - level) > band) continue;

            double p = c.Cx * perpX + c.Cy * perpY;
            pMin = Math.Min(pMin, p);
            pMax = Math.Max(pMax, p);

            area += c.Area;
            gammaArea += c.GammaBt * c.Area;
            var chars = ResolveChars(c.Material);
            fcArea += (chars is null ? 0.0 : Math.Abs(chars.Fc) * PaPerKpa) * c.Area;
        }

        if (area <= 0.0 || pMax <= pMin) return (0.0, 0.0);

        double width = pMax - pMin;
        double rbnt = gammaArea / area * (fcArea / area);
        return (width, rbnt);
    }

    static (double RsntPa, double EstPa, double EpsSel,
        IReadOnlyList<FireRebarTemperatureDetail> Details) BuildRebarEquivalent(
            IReadOnlyList<FireRebarElement> rebars, double areaTotal)
    {
        double rsArea = 0.0;
        double eArea = 0.0;
        var details = new List<FireRebarTemperatureDetail>(rebars.Count);

        foreach (var rebar in rebars)
        {
            double area = Math.Max(0.0, rebar.Area);
            if (area <= 0.0) continue;

            var chars = ResolveChars(rebar.Material);
            double rsPa = chars is null ? 0.0 : Math.Abs(chars.Ft) * PaPerKpa;
            double ePa = rebar.Material.E * PaPerKpa;
            double gammaSt = Sp468Tables.GammaSt(rebar.ClassGroup, rebar.Temperature);
            double gammaStE = Sp468Tables.GammaStE(rebar.ClassGroup, rebar.Temperature);

            rsArea += gammaSt * rsPa * area;
            eArea += gammaStE * ePa * area;
            details.Add(new FireRebarTemperatureDetail(
                rebar.ClassGroup, rebar.ClassSource, rebar.Temperature,
                area, gammaSt, gammaStE));
        }

        double epsSel = eArea > 0.0 ? rsArea / eArea : 0.0;
        return (rsArea / areaTotal, eArea / areaTotal, epsSel, details);
    }

    static (List<FireRebarElement> Rebars, bool BothFaces) SelectTensionRebars(
        FireFiberSection fiber, FireTemperatureProfileResult profile, bool atHeatedFace)
    {
        double sMin = double.MaxValue;
        foreach (var c in fiber.ConcreteElements)
            sMin = Math.Min(sMin, c.Cx * profile.AxisX + c.Cy * profile.AxisY);

        double half = sMin + profile.Height / 2.0;
        var near = new List<FireRebarElement>();
        var far = new List<FireRebarElement>();
        foreach (var r in fiber.RebarElements)
        {
            double s = r.X * profile.AxisX + r.Y * profile.AxisY;
            (s < half ? near : far).Add(r);
        }

        bool bothFaces = near.Count > 0 && far.Count > 0;
        return (atHeatedFace ? near : far, bothFaces);
    }

    static double ComputeH0(
        FireFiberSection fiber, FireTemperatureProfileResult profile,
        List<FireRebarElement> tension)
    {
        double sMin = double.MaxValue, sMax = double.MinValue;
        foreach (var c in fiber.ConcreteElements)
        {
            double s = c.Cx * profile.AxisX + c.Cy * profile.AxisY;
            sMin = Math.Min(sMin, s);
            sMax = Math.Max(sMax, s);
        }

        double areaSum = tension.Sum(r => Math.Max(0.0, r.Area));
        double sRebar = tension.Sum(r =>
            (r.X * profile.AxisX + r.Y * profile.AxisY) * Math.Max(0.0, r.Area)) / areaSum;

        double toMin = sRebar - sMin;
        double toMax = sMax - sRebar;
        return Math.Max(toMin, toMax);
    }

    static MaterialChars? ResolveChars(Material material)
        => material.GetChars(CalcType.C) ?? material.MaterialChars.FirstOrDefault();
}
