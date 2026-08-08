using System.Globalization;
using CScore;

/// <summary>Диагностический расчёт примера 47 по деформационной модели п. 8.2.32.</summary>
internal static class Example47Curvature
{
    internal const double Height = 0.300;
    internal const double Width = 1.150;
    internal const double RebarDepth = 0.042;
    internal const double RebarDiameter = 0.014;
    internal const double RebarArea = 923e-6;
    internal const double CrackingMoment = -38.0;
    internal const double LongMoment = -50.0;
    internal const double ShortMoment = -60.0;
    const double YieldStrain = 0.002;
    const double ConcreteCompressionLimit = -0.0035;
    const int CurvePointCount = 401;
    const double CrackLoopKappaEnd = 0.005;
    const int CrackLoopPointCount = 401;

    enum PsiMode
    {
        None,
        Clause8232,
        Clause8218Strain,
        Clause8232MinJump
    }

    public static void Run(bool includeConcreteTension = false)
    {
        var section = CreateSection();
        var epsCrcShort = CrackPlaneRebarStrains(section, CalcType.N, includeConcreteTension);
        var epsCrcLong = CrackPlaneRebarStrains(section, CalcType.NL, includeConcreteTension);

        var shortReference = Solve(section, CalcType.N, CrackingMoment, epsCrcShort,
            applyPsi: false, concreteTension: includeConcreteTension);
        var longReference = Solve(section, CalcType.NL, CrackingMoment, epsCrcLong,
            applyPsi: false, concreteTension: includeConcreteTension);

        var shortLong = Solve(section, CalcType.N, LongMoment, epsCrcShort, applyPsi: true,
            concreteTension: includeConcreteTension, initialGuess: shortReference.Curvature);
        var shortTotal = Solve(section, CalcType.N, ShortMoment, epsCrcShort, applyPsi: true,
            concreteTension: includeConcreteTension, initialGuess: shortLong.Curvature);
        var longTotal = Solve(section, CalcType.NL, LongMoment, epsCrcLong, applyPsi: true,
            concreteTension: includeConcreteTension, initialGuess: longReference.Curvature);

        var shortLongNoPsi = Solve(section, CalcType.N, LongMoment, epsCrcShort, applyPsi: false,
            concreteTension: includeConcreteTension, initialGuess: shortReference.Curvature);
        var shortTotalNoPsi = Solve(section, CalcType.N, ShortMoment, epsCrcShort, applyPsi: false,
            concreteTension: includeConcreteTension, initialGuess: shortLongNoPsi.Curvature);
        var longTotalNoPsi = Solve(section, CalcType.NL, LongMoment, epsCrcLong, applyPsi: false,
            concreteTension: includeConcreteTension, initialGuess: longReference.Curvature);

        double fullCurvature = shortTotal.Curvature.ky
            - shortLong.Curvature.ky
            + longTotal.Curvature.ky;
        double fullCurvatureNoPsi = shortTotalNoPsi.Curvature.ky
            - shortLongNoPsi.Curvature.ky
            + longTotalNoPsi.Curvature.ky;

        Console.WriteLine("Пример 47: кривизна по нелинейной деформационной модели СП 63.13330, п. 8.2.32");
        Console.WriteLine("Единицы: м, кН, кН·м, кПа; нижняя арматура => Mx < 0");
        Console.WriteLine($"Сечение: h={Height * 1000:F0} мм, b={Width * 1000:F0} мм, a={RebarDepth * 1000:F0} мм, As={RebarArea * 1e6:F0} мм² (6Ø14)");
        Console.WriteLine($"Бетон: B15, L3; длительная диаграмма: NL_2; Mcrc задан: 38 кН·м; растяжение бетона: {(includeConcreteTension ? "включено" : "выключено")}");
        Console.WriteLine();

        PrintReference("N, сразу после Mcrc", shortReference, epsCrcShort);
        PrintReference("NL, сразу после Mcrc", longReference, epsCrcLong);
        Console.WriteLine();
        Console.WriteLine($"{"Случай",-28} {"М, кН·м",10} {"ψs",10} {"iter",6} {"R",12} {"κy, 1/м",14} {"статус",12}");
        Console.WriteLine(new string('-', 100));
        PrintCase("N, M длит., 8.2.32", LongMoment, shortLong);
        PrintCase("N, M полн., 8.2.32", ShortMoment, shortTotal);
        PrintCase("NL, M длит., 8.2.32", LongMoment, longTotal);
        PrintCase("N, M длит., без ψs", LongMoment, shortLongNoPsi);
        PrintCase("N, M полн., без ψs", ShortMoment, shortTotalNoPsi);
        PrintCase("NL, M длит., без ψs", LongMoment, longTotalNoPsi);
        Console.WriteLine();
        Console.WriteLine($"Итог по 8.2.24 с ψs:    κfull = {fullCurvature:G8} 1/м, |κ| = {Math.Abs(fullCurvature):G8} 1/м");
        Console.WriteLine($"Контроль без ψs:         κfull = {fullCurvatureNoPsi:G8} 1/м, |κ| = {Math.Abs(fullCurvatureNoPsi):G8} 1/м");

        AssertConverged("N, M длит., 8.2.32", shortLong);
        AssertConverged("N, M полн., 8.2.32", shortTotal);
        AssertConverged("NL, M длит., 8.2.32", longTotal);
        AssertPsiEffect(shortLong, shortLongNoPsi, "N, M длит.");
        AssertPsiEffect(shortTotal, shortTotalNoPsi, "N, M полн.");
        AssertPsiEffect(longTotal, longTotalNoPsi, "NL, M длит.");
    }

    /// <summary>Строит диаграмму кривизна–момент при N=0 до текучести арматуры.</summary>
    public static void RunMomentCurvature()
    {
        var tensionSection = CreateSection();
        var tensionReference = Solve(tensionSection, CalcType.N, CrackingMoment,
            new Dictionary<Fiber, double>(),
            applyPsi: false, concreteTension: true);
        var withoutPsi = BuildMomentCurve(
            tensionSection, CalcType.N, new Dictionary<Fiber, double>(), tensionReference.Curvature.ky,
            PsiMode.None, concreteTension: true);
        int peakIndex = FindFirstMomentPeak(withoutPsi);
        var crackAnchor = RefineFirstMomentPeak(
            tensionSection, CalcType.N, tensionReference.Curvature.ky, withoutPsi, peakIndex);
        withoutPsi[peakIndex] = crackAnchor;

        var withoutTensionSection = CreateSection();
        var withoutTensionReference = Solve(
            withoutTensionSection, CalcType.N, crackAnchor.Load.Mx,
            new Dictionary<Fiber, double>(), applyPsi: false, concreteTension: false,
            initialGuess: crackAnchor.Curvature);
        AssertConverged("Без растяжения бетона, cracked Mcrc", withoutTensionReference);
        var withoutTensionEpsCrc = RebarStrainsAt(
            withoutTensionSection, withoutTensionReference.Curvature);
        var tensionPsiSection = CreateSection();
        var tensionPsiEpsCrc = TransferCrackPlaneStrains(
            withoutTensionSection, withoutTensionEpsCrc, tensionPsiSection);
        var withoutConcreteTensionPsi = BuildPostCrackMomentCurve(
            withoutTensionSection, CalcType.N, withoutTensionEpsCrc,
            crackAnchor.Curvature.ky, crackAnchor,
            PsiMode.Clause8232, concreteTension: false);
        var withConcreteTensionPsi = BuildPostCrackMomentCurve(
            tensionPsiSection, CalcType.N, tensionPsiEpsCrc, crackAnchor.Curvature.ky,
            crackAnchor,
            PsiMode.Clause8232, concreteTension: true);
        AssertHasPostCrackMomentDrop(
            withoutConcreteTensionPsi, crackAnchor,
            "Без растяжения бетона + ψs по 8.2.32");
        var withoutTensionPsiMinSection = CreateSection();
        var withoutTensionPsiMinEpsCrc = TransferCrackPlaneStrains(
            withoutTensionSection, withoutTensionEpsCrc, withoutTensionPsiMinSection);
        var withoutConcreteTensionPsiMin = BuildMinPsiJumpPostCrackCurve(
            withoutTensionPsiMinSection, CalcType.N, withoutTensionPsiMinEpsCrc,
            crackAnchor.Curvature.ky, crackAnchor, withoutConcreteTensionPsi,
            concreteTension: false);
        var tensionPsiMinSection = CreateSection();
        var tensionPsiMinEpsCrc = TransferCrackPlaneStrains(
            withoutTensionSection, withoutTensionEpsCrc, tensionPsiMinSection);
        var withConcreteTensionPsiMin = BuildMinPsiJumpPostCrackCurve(
            tensionPsiMinSection, CalcType.N, tensionPsiMinEpsCrc,
            crackAnchor.Curvature.ky, crackAnchor, withConcreteTensionPsi,
            concreteTension: true);

        var crackLoopWithoutPsi = BuildCrackLoopCurve(
            CreateSection(), CalcType.N, new Dictionary<Fiber, double>(),
            crackAnchor, PsiMode.None, concreteTension: true);
        var crackLoopNoTensionSection = CreateSection();
        var crackLoopNoTensionEpsCrc = TransferCrackPlaneStrains(
            withoutTensionSection, withoutTensionEpsCrc, crackLoopNoTensionSection);
        var crackLoopNoTensionPsi = BuildCrackLoopCurve(
            crackLoopNoTensionSection, CalcType.N, crackLoopNoTensionEpsCrc,
            crackAnchor, PsiMode.Clause8232, concreteTension: false);
        var crackLoopTensionSection = CreateSection();
        var crackLoopTensionEpsCrc = TransferCrackPlaneStrains(
            withoutTensionSection, withoutTensionEpsCrc, crackLoopTensionSection);
        var crackLoopTensionPsi = BuildCrackLoopCurve(
            crackLoopTensionSection, CalcType.N, crackLoopTensionEpsCrc,
            crackAnchor, PsiMode.Clause8232, concreteTension: true);

        withConcreteTensionPsi = ComposePostCrackCurve(
            withoutPsi, peakIndex, withConcreteTensionPsi);
        withoutConcreteTensionPsi = ComposePostCrackCurve(
            withoutPsi, peakIndex, withoutConcreteTensionPsi);

        string fileName = "example47-moment-curvature-three.csv";
        string path = Path.GetFullPath(fileName);
        WriteMomentCurveCsv(path, crackAnchor.Curvature.ky, withoutPsi,
            withoutConcreteTensionPsi, withConcreteTensionPsi,
            withoutConcreteTensionPsiMin, withConcreteTensionPsiMin);
        string crackLoopPath = Path.GetFullPath("example47-moment-curvature-crack-loop.csv");
        WriteCrackLoopCsv(
            crackLoopPath, crackAnchor.Curvature.ky, crackLoopWithoutPsi,
            crackLoopNoTensionPsi, crackLoopTensionPsi);

        Console.WriteLine("Пример 47: диаграмма кривизна–момент по п. 8.2.32 СП 63.13330");
        Console.WriteLine("Условия: N=0, κz=0, Mx<0; до Mcrc работает растянутый бетон");
        Console.WriteLine($"Mcrc по первому пику = {Math.Abs(crackAnchor.Load.Mx):G8} кН·м, κcrc = {crackAnchor.Curvature.ky:G8} 1/м");
        Console.WriteLine($"Предел текучести A400: εy = {YieldStrain:G8}");
        PrintCurveSummary("Без ψs", withoutPsi);
        PrintCurveSummary("Без растяжения бетона + ψs по 8.2.32", withoutConcreteTensionPsi);
        PrintCurveSummary("Растяжение бетона + ψs по 8.2.32", withConcreteTensionPsi);
        PrintCrackLoopPoint("Петля, без ψs", crackLoopWithoutPsi[1]);
        PrintCrackLoopPoint("Петля, без растяжения бетона + ψs", crackLoopNoTensionPsi[1]);
        PrintCrackLoopPoint("Петля, растяжение бетона + ψs", crackLoopTensionPsi[1]);
        Console.WriteLine($"CSV: {path}");
        Console.WriteLine($"CSV петли: {crackLoopPath}");

        AssertCurve(withoutPsi, "Без ψs");
        AssertCurve(withoutConcreteTensionPsi, "Без растяжения бетона + ψs");
        AssertCurve(withConcreteTensionPsi, "Растяжение бетона + ψs");
    }

    static int FindFirstMomentPeak(IReadOnlyList<MomentCurvaturePoint> points)
    {
        for (int i = 1; i < points.Count - 1; i++)
        {
            double previous = Math.Abs(points[i - 1].Load.Mx);
            double current = Math.Abs(points[i].Load.Mx);
            double next = Math.Abs(points[i + 1].Load.Mx);
            if (current >= previous && current > next)
                return i;
        }

        throw new InvalidOperationException("Не найден первый пик момента на кривой с растянутым бетоном");
    }

    static MomentCurvaturePoint RefineFirstMomentPeak(
        CrossSection section,
        CalcType calc,
        double crackCurvature,
        IReadOnlyList<MomentCurvaturePoint> points,
        int peakIndex)
    {
        double sign = Math.Sign(crackCurvature);
        double left = Math.Abs(points[peakIndex - 1].Curvature.ky);
        double right = Math.Abs(points[peakIndex + 1].Curvature.ky);
        var emptyEpsCrc = new Dictionary<Fiber, double>();
        for (int i = 0; i < 45; i++)
        {
            double first = left + (right - left) / 3.0;
            double second = right - (right - left) / 3.0;
            var firstPoint = SolveAtCurvature(
                section, calc, sign * first, emptyEpsCrc, crackCurvature,
                PsiMode.None, concreteTension: true);
            var secondPoint = SolveAtCurvature(
                section, calc, sign * second, emptyEpsCrc, crackCurvature,
                PsiMode.None, concreteTension: true);
            if (Math.Abs(firstPoint.Load.Mx) < Math.Abs(secondPoint.Load.Mx))
                left = first;
            else
                right = second;
        }

        double refined = 0.5 * (left + right);
        return SolveAtCurvature(
            section, calc, sign * refined, emptyEpsCrc, crackCurvature,
            PsiMode.None, concreteTension: true);
    }

    static List<MomentCurvaturePoint> ComposePostCrackCurve(
        IReadOnlyList<MomentCurvaturePoint> preCrack,
        int peakIndex,
        IReadOnlyList<MomentCurvaturePoint> postCrack)
    {
        var result = preCrack.Take(peakIndex + 1).ToList();
        result.AddRange(postCrack.Skip(1));
        return result;
    }

    static List<MomentCurvaturePoint> BuildPostCrackMomentCurve(
        CrossSection source,
        CalcType calc,
        IReadOnlyDictionary<Fiber, double> epsCrc,
        double crackCurvature,
        MomentCurvaturePoint anchor,
        PsiMode psiMode,
        bool concreteTension)
    {
        double sign = Math.Sign(crackCurvature);
        if (sign == 0.0)
            throw new InvalidOperationException("Не удалось определить направление кривизны после Mcrc");

        double kappaMax = FindKappaForConcreteCompression(
            source, calc, epsCrc, crackCurvature, psiMode, concreteTension);
        var psiStart = FindFirstPostCrackPoint(
            source, calc, epsCrc, crackCurvature, psiMode, concreteTension, kappaMax);
        var points = new List<MomentCurvaturePoint>(CurvePointCount) { anchor, psiStart };

        for (int i = 1; i < CurvePointCount - 1; i++)
        {
            double magnitude = Math.Abs(psiStart.Curvature.ky)
                + (kappaMax - Math.Abs(psiStart.Curvature.ky))
                * i / (CurvePointCount - 2);
            points.Add(SolveAtCurvature(
                source, calc, sign * magnitude, epsCrc, crackCurvature, psiMode, concreteTension));
        }

        return points;
    }

    static List<MomentCurvaturePoint> BuildMinPsiJumpPostCrackCurve(
        CrossSection source,
        CalcType calc,
        IReadOnlyDictionary<Fiber, double> epsCrc,
        double crackCurvature,
        MomentCurvaturePoint anchor,
        IReadOnlyList<MomentCurvaturePoint> standardPostCrack,
        bool concreteTension)
    {
        if (standardPostCrack.Count < 2)
            throw new InvalidOperationException("Недостаточно точек для явного скачка ψmin");

        var firstStandardPoint = standardPostCrack[1];
        var minPsiPoint = SolveAtCurvature(
            source, calc, firstStandardPoint.Curvature.ky, epsCrc,
            crackCurvature, PsiMode.Clause8232MinJump, concreteTension);
        var result = new List<MomentCurvaturePoint>(standardPostCrack.Count)
        {
            anchor,
            minPsiPoint
        };
        result.AddRange(standardPostCrack.Skip(2));
        return result;
    }

    static MomentCurvaturePoint FindFirstPostCrackPoint(
        CrossSection source,
        CalcType calc,
        IReadOnlyDictionary<Fiber, double> epsCrc,
        double crackCurvature,
        PsiMode psiMode,
        bool concreteTension,
        double kappaMax)
    {
        double sign = Math.Sign(crackCurvature);
        int scanCount = CurvePointCount * 4;
        for (int i = 1; i <= scanCount; i++)
        {
            double magnitude = Math.Abs(crackCurvature)
                + (kappaMax - Math.Abs(crackCurvature)) * i / scanCount;
            var point = SolveAtCurvature(
                source, calc, sign * magnitude, epsCrc, crackCurvature, psiMode, concreteTension);
            if (point.Converged && Math.Abs(point.Curvature.ky) > Math.Abs(crackCurvature))
                return point;
        }

        throw new InvalidOperationException("Не найдена первая равновесная точка ψs после Mcrc");
    }

    static Dictionary<Fiber, double> TransferCrackPlaneStrains(
        CrossSection source,
        IReadOnlyDictionary<Fiber, double> sourceStrains,
        CrossSection target)
    {
        var sourceBars = source.Areas
            .Where(a => a.Material?.Type == MatType.ReSteelF)
            .SelectMany(a => a.Fibers)
            .Where(f => f.TypeFiber == FiberType.point)
            .ToList();
        var targetBars = target.Areas
            .Where(a => a.Material?.Type == MatType.ReSteelF)
            .SelectMany(a => a.Fibers)
            .Where(f => f.TypeFiber == FiberType.point)
            .ToList();
        if (sourceBars.Count != targetBars.Count)
            throw new InvalidOperationException("Не совпало число стержней при переносе εs,crc");

        var result = new Dictionary<Fiber, double>(targetBars.Count);
        for (int i = 0; i < targetBars.Count; i++)
            result[targetBars[i]] = sourceStrains[sourceBars[i]];
        return result;
    }

    static List<MomentCurvaturePoint> BuildMomentCurve(
        CrossSection source,
        CalcType calc,
        IReadOnlyDictionary<Fiber, double> epsCrc,
        double crackCurvature,
        PsiMode psiMode,
        bool concreteTension)
    {
        double sign = Math.Sign(crackCurvature);
        if (sign == 0.0)
            throw new InvalidOperationException("Не удалось определить направление кривизны после Mcrc");

        double kappaMax = FindKappaForConcreteCompression(
            source, calc, epsCrc, crackCurvature, psiMode, concreteTension);
        var points = new List<MomentCurvaturePoint>(CurvePointCount);
        for (int i = 0; i < CurvePointCount; i++)
        {
            double magnitude = kappaMax * i / (CurvePointCount - 1);
            points.Add(SolveAtCurvature(
                source, calc, sign * magnitude, epsCrc, crackCurvature, psiMode, concreteTension));
        }

        return points;
    }

    static List<MomentCurvaturePoint> BuildCrackLoopCurve(
        CrossSection source,
        CalcType calc,
        IReadOnlyDictionary<Fiber, double> epsCrc,
        MomentCurvaturePoint anchor,
        PsiMode psiMode,
        bool concreteTension)
    {
        double sign = Math.Sign(anchor.Curvature.ky);
        if (sign == 0.0)
            throw new InvalidOperationException("Не удалось определить направление кривизны петли после Mcrc");

        var points = new List<MomentCurvaturePoint>(CrackLoopPointCount)
        {
            anchor
        };
        for (int i = 1; i < CrackLoopPointCount; i++)
        {
            double magnitude = Math.Abs(anchor.Curvature.ky)
                + (CrackLoopKappaEnd - Math.Abs(anchor.Curvature.ky))
                * i / (CrackLoopPointCount - 1);
            points.Add(SolveAtCurvature(
                source, calc, sign * magnitude, epsCrc, anchor.Curvature.ky,
                psiMode, concreteTension));
        }

        return points;
    }

    static double FindKappaForConcreteCompression(
        CrossSection source,
        CalcType calc,
        IReadOnlyDictionary<Fiber, double> epsCrc,
        double crackCurvature,
        PsiMode psiMode,
        bool concreteTension)
    {
        double sign = Math.Sign(crackCurvature);
        double lo = 0.0;
        double hi = Math.Max(Math.Abs(crackCurvature) * 1.5, 1e-4);
        var highPoint = SolveAtCurvature(source, calc, sign * hi, epsCrc, crackCurvature, psiMode, concreteTension);
        for (int i = 0; i < 24 && highPoint.MinContourConcreteStrain > ConcreteCompressionLimit; i++)
        {
            hi *= 1.5;
            highPoint = SolveAtCurvature(source, calc, sign * hi, epsCrc, crackCurvature, psiMode, concreteTension);
        }

        if (!highPoint.Converged || highPoint.MinContourConcreteStrain > ConcreteCompressionLimit)
            throw new InvalidOperationException("Не удалось дойти до предельной деформации бетона");

        for (int i = 0; i < 50; i++)
        {
            double mid = 0.5 * (lo + hi);
            var point = SolveAtCurvature(source, calc, sign * mid, epsCrc, crackCurvature, psiMode, concreteTension);
            if (point.MinContourConcreteStrain <= ConcreteCompressionLimit)
                hi = mid;
            else
                lo = mid;
        }

        return hi;
    }

    static MomentCurvaturePoint SolveAtCurvature(
        CrossSection source,
        CalcType calc,
        double ky,
        IReadOnlyDictionary<Fiber, double> epsCrc,
        double crackCurvature,
        PsiMode psiMode,
        bool concreteTension)
    {
        bool psiActive = psiMode != PsiMode.None && Math.Abs(ky) > Math.Abs(crackCurvature) + 1e-12;
        var section = new PsiSection(source, epsCrc, psiActive ? psiMode : PsiMode.None);
        CurvatureEquilibriumResult equilibrium;
        try
        {
            equilibrium = CurvatureEquilibrium8232.Solve(
                e0 => section.Integral(new Kurvature { e0 = e0, ky = ky, kz = 0.0 },
                    calc, ten: concreteTension, ca: true),
                targetN: 0.0,
                tolerance: 0.05,
                initialBracket: 0.02,
                maxIterations: 120);
        }
        catch (InvalidOperationException)
        {
            return FailedMomentCurvaturePoint(ky, psiActive, double.NaN);
        }

        if (!equilibrium.Converged)
            return FailedMomentCurvaturePoint(ky, psiActive, equilibrium.Residual);

        var curvature = new Kurvature { e0 = equilibrium.E0, ky = ky, kz = 0.0 };
        return EvaluateAtKnownCurvature(
            source, calc, curvature, epsCrc, crackCurvature, psiMode,
            concreteTension, equilibrium.Residual);
    }

    static MomentCurvaturePoint EvaluateAtKnownCurvature(
        CrossSection source,
        CalcType calc,
        Kurvature curvature,
        IReadOnlyDictionary<Fiber, double> epsCrc,
        double crackCurvature,
        PsiMode psiMode,
        bool concreteTension,
        double residual)
    {
        bool psiActive = psiMode != PsiMode.None
            && Math.Abs(curvature.ky) > Math.Abs(crackCurvature) + 1e-12;
        var section = new PsiSection(source, epsCrc, psiActive ? psiMode : PsiMode.None);
        var load = section.Integral(curvature, calc, ten: concreteTension, ca: true);
        var rebars = section.Areas
            .Where(a => a.Material?.Type is MatType.ReSteelF or MatType.ReSteelU)
            .SelectMany(a => a.Fibers)
            .Where(f => f.TypeFiber == FiberType.point)
            .ToList();

        var tensile = rebars
            .Select(f => (Fiber: f, Strain: f.Eps + f.Eps_p))
            .Where(item => item.Strain > 0.0)
            .ToList();
        double maxStrain = tensile.Count == 0 ? 0.0 : tensile.Max(item => item.Strain);
        double maxStress = tensile.Count == 0
            ? 0.0
            : tensile.Max(item => item.Fiber.Sig) / 1000.0;
        double tensileForce = tensile.Sum(item => item.Fiber.Sig * item.Fiber.Area);
        double minContourConcreteStrain = MinContourConcreteStrain(curvature);
        double psi = tensile.Count == 0 || !psiActive
            ? 1.0
            : tensile.Average(item => PsiValue(psiMode, epsCrc[item.Fiber], item.Strain));

        return new MomentCurvaturePoint(
            curvature, load, maxStrain, maxStress, tensileForce, minContourConcreteStrain,
            psi, psiActive, residual, true);
    }

    static MomentCurvaturePoint FailedMomentCurvaturePoint(
        double ky,
        bool psiActive,
        double residual) => new(
            new Kurvature { ky = ky },
            new Load { N = double.NaN, Mx = double.NaN, My = double.NaN },
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            psiActive,
            residual,
            false);

    static void WriteMomentCurveCsv(
        string path,
        double crackCurvature,
        IReadOnlyList<MomentCurvaturePoint> withoutPsi,
        IReadOnlyList<MomentCurvaturePoint> withPsi8232,
        IReadOnlyList<MomentCurvaturePoint> withConcreteTension,
        IReadOnlyList<MomentCurvaturePoint> withPsi8232MinNoConcreteTension,
        IReadOnlyList<MomentCurvaturePoint> withPsi8232MinConcreteTension)
    {
        var rows = new List<string>
        {
            "curve;point;state;psi_active;kappa_1_per_m;kappa_abs_1_per_m;Mx_kNm;Mx_abs_kNm;N_kN;e0;eps_s_max;sig_s_max_MPa;Fs_kN;eps_b_min_contour;psi_s_avg"
        };
        AddRows(rows, "without_psi", crackCurvature, withoutPsi);
        AddRows(rows, "psi_8232_no_concrete_tension", crackCurvature, withPsi8232);
        AddRows(rows, "psi_8232_concrete_tension", crackCurvature, withConcreteTension);
        AddRows(rows, "psi_8232_min_no_concrete_tension", crackCurvature, withPsi8232MinNoConcreteTension);
        AddRows(rows, "psi_8232_min_concrete_tension", crackCurvature, withPsi8232MinConcreteTension);
        File.WriteAllLines(path, rows);
    }

    static void WriteCrackLoopCsv(
        string path,
        double crackCurvature,
        IReadOnlyList<MomentCurvaturePoint> withoutPsi,
        IReadOnlyList<MomentCurvaturePoint> noConcreteTensionPsi,
        IReadOnlyList<MomentCurvaturePoint> concreteTensionPsi)
    {
        var rows = new List<string>
        {
            "curve;point;state;psi_active;kappa_1_per_m;kappa_abs_1_per_m;Mx_kNm;Mx_abs_kNm;N_kN;e0;eps_s_max;sig_s_max_MPa;Fs_kN;eps_b_min_contour;psi_s_avg"
        };
        AddRows(rows, "without_psi", crackCurvature, withoutPsi);
        AddRows(rows, "psi_8232_no_concrete_tension", crackCurvature, noConcreteTensionPsi);
        AddRows(rows, "psi_8232_concrete_tension", crackCurvature, concreteTensionPsi);
        File.WriteAllLines(path, rows);
    }

    static void AddRows(
        ICollection<string> rows,
        string curve,
        double crackCurvature,
        IReadOnlyList<MomentCurvaturePoint> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (!point.Converged)
            {
                rows.Add(string.Join(";", new[]
                {
                    curve,
                    i.ToString(CultureInfo.InvariantCulture),
                    "no_equilibrium",
                    point.PsiActive ? "1" : "0",
                    Format(point.Curvature.ky),
                    Format(Math.Abs(point.Curvature.ky)),
                    "NaN",
                    "NaN",
                    "NaN",
                    "NaN",
                    "NaN",
                    "NaN",
                    "NaN",
                    "NaN",
                    "NaN"
                }));
                continue;
            }
            string state = Math.Abs(point.Curvature.ky) <= Math.Abs(crackCurvature) + 1e-12
                ? "I"
                : point.MaxTensileStrain < YieldStrain ? "II" : "II_yield";
            rows.Add(string.Join(";", new[]
            {
                curve,
                i.ToString(CultureInfo.InvariantCulture),
                state,
                point.PsiActive ? "1" : "0",
                Format(point.Curvature.ky),
                Format(Math.Abs(point.Curvature.ky)),
                Format(point.Load.Mx),
                Format(Math.Abs(point.Load.Mx)),
                Format(point.Load.N),
                Format(point.Curvature.e0),
                Format(point.MaxTensileStrain),
                Format(point.MaxTensileStress),
                Format(point.TensileForce),
                Format(point.MinContourConcreteStrain),
                Format(point.Psi)
            }));
        }
    }

    static void PrintCurveSummary(string title, IReadOnlyList<MomentCurvaturePoint> points)
    {
        var valid = points.Where(point => point.Converged).ToList();
        var yield = valid.FirstOrDefault(point => point.MaxTensileStrain >= YieldStrain);
        var last = valid[^1];
        string yieldSummary = yield.Converged
            ? $"Myield≈{Math.Abs(yield.Load.Mx):G8} кН·м, κyield≈{yield.Curvature.ky:G8} 1/м"
            : "Myield не найден";
        Console.WriteLine(
            $"{title}: {yieldSummary}, " +
            $"M(εb,min={ConcreteCompressionLimit:G5})={Math.Abs(last.Load.Mx):G8} кН·м, " +
            $"κmax={last.Curvature.ky:G8} 1/м, εs,max={last.MaxTensileStrain:G8}, " +
            $"ψavg={last.Psi:G6}, " +
            $"valid={valid.Count}/{points.Count}");
    }

    static void PrintCrackLoopPoint(string title, MomentCurvaturePoint point)
    {
        Console.WriteLine(
            $"{title}: κ={point.Curvature.ky:G8} 1/м, "
            + $"M={Math.Abs(point.Load.Mx):G8} кН·м, "
            + $"εs={point.MaxTensileStrain:G8}, ψs={point.Psi:G8}");
    }

    static void AssertCurve(
        IReadOnlyList<MomentCurvaturePoint> points,
        string title,
        bool allowGaps = false,
        bool allowInitialBackstep = false)
    {
        var valid = points.Where(point => point.Converged).ToList();
        if (points.Count < 2 || valid.Count == 0 ||
            (!allowGaps && valid.Count != points.Count) || valid.Any(point => point.Residual > 0.05))
            throw new InvalidOperationException($"{title}: нарушено равновесие N=0 на кривой");
        if (valid[^1].MinContourConcreteStrain > ConcreteCompressionLimit + 1e-9)
            throw new InvalidOperationException($"{title}: кривая не дошла до εb,min={ConcreteCompressionLimit:G8}");
        bool backstepUsed = false;
        for (int i = 1; i < valid.Count; i++)
        {
            bool decreases = Math.Abs(valid[i].Curvature.ky) < Math.Abs(valid[i - 1].Curvature.ky);
            if (decreases && allowInitialBackstep && !backstepUsed)
            {
                backstepUsed = true;
                continue;
            }
            if (Math.Abs(valid[i].Curvature.ky) <= Math.Abs(valid[i - 1].Curvature.ky))
                throw new InvalidOperationException($"{title}: кривизна не возрастает по модулю");
        }
    }

    static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    static double MinContourConcreteStrain(Kurvature curvature)
    {
        double min = double.PositiveInfinity;
        foreach (double x in new[] { -Width / 2.0, Width / 2.0 })
            foreach (double y in new[] { -Height / 2.0, Height / 2.0 })
                min = Math.Min(min, curvature.e0 + curvature.ky * y + curvature.kz * x);
        return min;
    }

    static double PsiValue(PsiMode mode, double epsCrc, double eps) => mode switch
    {
        PsiMode.Clause8232 => Curvature8232.PsiS(epsCrc, eps),
        PsiMode.Clause8218Strain => Curvature8232.PsiSFromStrainRatio(epsCrc, eps),
        PsiMode.Clause8232MinJump => 1.0 / 1.8,
        _ => 1.0
    };

    internal static CrossSection CreateSection()
    {
        var concreteMaterial = new Material
        {
            Id = 1,
            Tag = "B15",
            Type = MatType.Concrete,
            E = 24_000_000.0,
            MaterialChars =
            [
                ConcreteChars(CalcType.C, false),
                ConcreteChars(CalcType.CL, true),
                ConcreteChars(CalcType.N, false),
                ConcreteChars(CalcType.NL, true),
            ]
        };

        var x = new[] { -Width / 2, Width / 2, Width / 2, -Width / 2, -Width / 2 };
        var y = new[] { -Height / 2, -Height / 2, Height / 2, Height / 2, -Height / 2 };
        var concrete = new MaterialArea
        {
            Id = 1,
            Tag = "Бетон B15",
            Category = AreaCategory.Region,
            Material = concreteMaterial,
            MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L3,
            Hull = new Contour(x, y, "hull")
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 48, ny: 24);

        var steelMaterial = new Material
        {
            Id = 2,
            Tag = "A400",
            Type = MatType.ReSteelF,
            E = 200_000_000.0,
            MaterialChars =
            [
                RebarChars(CalcType.C),
                RebarChars(CalcType.CL),
                RebarChars(CalcType.N),
                RebarChars(CalcType.NL),
            ]
        };

        var rebar = new MaterialArea
        {
            Id = 2,
            Tag = "6Ø14 снизу",
            Category = AreaCategory.RebarGroup,
            Material = steelMaterial,
            MaterialId = steelMaterial.Id,
            DiagrammType = DiagrammType.L2,
            HostArea = concrete,
            HostAreaId = concrete.Id
        };

        double yBar = -Height / 2 + RebarDepth;
        double barArea = RebarArea / 6.0;
        for (int i = 0; i < 6; i++)
        {
            double xBar = -0.45 + i * 0.18;
            var bar = Fiber.CreatePoint(RebarDiameter, xBar, yBar);
            bar.Area = barArea;
            rebar.Fibers.Add(bar);
        }

        var section = new CrossSection
        {
            Id = 1,
            Tag = "Пример 47, плита фундамента",
            Areas = [concrete, rebar]
        };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    static MaterialChars ConcreteChars(CalcType calc, bool longTerm)
    {
        return longTerm
            ? new MaterialChars
            {
                Type = MatType.Concrete,
                TypeCalc = calc,
                Fc = -11_000,
                Ft = 1_100,
                E = 9_090_909.091,
                Ec0 = -0.0034,
                Ec1 = -0.00121,
                Ec2 = -0.0048,
                Ec1Red = -0.0028,
                Et1Red = 0.00022,
                Et0 = 0.00024,
                Et1 = 0.000121,
                Et2 = 0.00031
            }
            : new MaterialChars
            {
                Type = MatType.Concrete,
                TypeCalc = calc,
                Fc = -11_000,
                Ft = 1_100,
                E = 24_000_000,
                Ec0 = -0.002,
                Ec1 = -0.000275,
                Ec2 = -0.0035,
                Ec1Red = -0.0015,
                Et1Red = 0.00008,
                Et0 = 0.0001,
                Et1 = 0.0000275,
                Et2 = 0.00015
            };
    }

    static MaterialChars RebarChars(CalcType calc) => new()
    {
        Type = MatType.ReSteelF,
        TypeCalc = calc,
        Fc = -400_000,
        Ft = 400_000,
        E = 200_000_000,
        Ec2 = -0.0035,
        Et2 = 0.025
    };

    internal static Dictionary<Fiber, double> CrackPlaneRebarStrains(CrossSection section, CalcType calc, bool concreteTension)
    {
        var reference = Solve(section, calc, CrackingMoment, new Dictionary<Fiber, double>(),
            applyPsi: false, concreteTension: concreteTension);
        AssertConverged($"{calc}, Mcrc", reference);
        return RebarStrainsAt(section, reference.Curvature);
    }

    static Dictionary<Fiber, double> RebarStrainsAt(CrossSection section, Kurvature curvature)
    {
        var strains = new Dictionary<Fiber, double>();
        foreach (var area in section.Areas.Where(a => a.Material?.Type == MatType.ReSteelF))
        {
            foreach (var bar in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                strains[bar] = curvature.e0 + curvature.ky * bar.Y + curvature.kz * bar.X;
        }
        return strains;
    }

    internal static SolveResult Solve(
        CrossSection source,
        CalcType calc,
        double targetMx,
        IReadOnlyDictionary<Fiber, double> epsCrc,
        bool applyPsi,
        bool concreteTension,
        Kurvature? initialGuess = null)
    {
        var section = new PsiSection(source, epsCrc, applyPsi);
        var solver = new StrainSolver(section, calc, ten: concreteTension, ca: true,
            tol: 0.05, maxIter: 80, h: 1e-7, centralJacobian: true);
        var curvature = solver.Solve(0.0, targetMx, 0.0, initialGuess);
        var load = section.Integral(curvature, calc, ten: concreteTension, ca: true);
        double psi = AveragePsi(section, curvature, epsCrc);
        return new SolveResult(curvature, load, solver.Converged, solver.Iterations, solver.Residual, psi);
    }

    /// <summary>Секущая матрица жёсткости в найденной плоскости деформаций.</summary>
    internal static double[,] SecantStiffnessAt(
        CrossSection source,
        CalcType calc,
        Kurvature curvature,
        IReadOnlyDictionary<Fiber, double> epsCrc,
        bool applyPsi,
        bool concreteTension)
    {
        var section = new PsiSection(source, epsCrc, applyPsi);
        section.Integral(curvature, calc, ten: concreteTension, ca: true);

        var stiffness = new double[3, 3];
        foreach (var area in section.Areas)
        {
            foreach (var fiber in area.Fibers)
            {
                if (!double.IsFinite(fiber.E) || fiber.E == 0.0 || fiber.Area <= 0.0)
                    continue;

                double[] b = [1.0, fiber.Y, fiber.X];
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 3; col++)
                        stiffness[row, col] += fiber.E * fiber.Area * b[row] * b[col];
            }
        }

        return stiffness;
    }

    static double AveragePsi(CrossSection section, Kurvature curvature, IReadOnlyDictionary<Fiber, double> epsCrc)
    {
        var values = new List<double>();
        foreach (var area in section.Areas.Where(a => a.Material?.Type == MatType.ReSteelF))
        {
            foreach (var bar in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
            {
                double eps = curvature.e0 + curvature.ky * bar.Y + curvature.kz * bar.X;
                if (eps > 0 && epsCrc.TryGetValue(bar, out double crc))
                    values.Add(Curvature8232.PsiS(crc, eps));
            }
        }
        return values.Count == 0 ? 1.0 : values.Average();
    }

    static void PrintReference(string title, SolveResult result, IReadOnlyDictionary<Fiber, double> epsCrc)
    {
        double min = epsCrc.Count == 0 ? 0 : epsCrc.Values.Min();
        double max = epsCrc.Count == 0 ? 0 : epsCrc.Values.Max();
        Console.WriteLine($"{title}: сходится={result.Converged}, iter={result.Iterations}, R={result.Residual:G6}, κy={result.Curvature.ky:G8} 1/м, εs,crc={min:G6}...{max:G6}");
    }

    static void PrintCase(string title, double moment, SolveResult result)
    {
        string status = result.Converged ? "OK" : "FAIL";
        Console.WriteLine($"{title,-28} {moment,10:F1} {result.Psi,10:F5} {result.Iterations,6} {result.Residual,12:G5} {result.Curvature.ky,14:G7} {status,12}");
    }

    static void AssertConverged(string title, SolveResult result)
    {
        if (!result.Converged || result.Residual > 0.05)
            throw new InvalidOperationException($"{title}: StrainSolver не сошёлся, R={result.Residual:G8}");
    }

    static void AssertPsiEffect(SolveResult withPsi, SolveResult withoutPsi, string title)
    {
        if (withPsi.Psi <= 0.0 || withPsi.Psi >= 1.0)
            throw new InvalidOperationException($"{title}: ψs не попал внутрь (0; 1), получено {withPsi.Psi:G8}");
        if (Math.Abs(withPsi.Curvature.ky) >= Math.Abs(withoutPsi.Curvature.ky))
            throw new InvalidOperationException($"{title}: применение ψs не уменьшило модуль кривизны");
    }

    static void AssertHasPostCrackMomentDrop(
        IReadOnlyList<MomentCurvaturePoint> points,
        MomentCurvaturePoint anchor,
        string title)
    {
        if (!points.Skip(1).Any(point => point.Converged
            && Math.Abs(point.Load.Mx) < Math.Abs(anchor.Load.Mx)))
        {
            var first = points.Skip(1).FirstOrDefault();
            throw new InvalidOperationException(
                $"{title}: не найден посттрещинный спад момента ниже Mcrc; "
                + $"Mcrc={Math.Abs(anchor.Load.Mx):G8}, "
                + $"Mfirst={Math.Abs(first.Load.Mx):G8}, "
                + $"κfirst={Math.Abs(first.Curvature.ky):G8}");
        }
    }

    internal readonly record struct SolveResult(
        Kurvature Curvature,
        Load Load,
        bool Converged,
        int Iterations,
        double Residual,
        double Psi);

    internal readonly record struct MomentCurvaturePoint(
        Kurvature Curvature,
        Load Load,
        double MaxTensileStrain,
        double MaxTensileStress,
        double TensileForce,
        double MinContourConcreteStrain,
        double Psi,
        bool PsiActive,
        double Residual,
        bool Converged);

    sealed class PsiSection : CrossSection
    {
        readonly IReadOnlyDictionary<Fiber, double> _epsCrc;
        readonly PsiMode _psiMode;

        public PsiSection(CrossSection source, IReadOnlyDictionary<Fiber, double> epsCrc, bool applyPsi)
            : this(source, epsCrc, applyPsi ? PsiMode.Clause8232 : PsiMode.None)
        {
        }

        public PsiSection(CrossSection source, IReadOnlyDictionary<Fiber, double> epsCrc, PsiMode psiMode)
        {
            Id = source.Id;
            Tag = source.Tag;
            Areas = source.Areas;
            _epsCrc = epsCrc;
            _psiMode = psiMode;
        }

        public override Load Integral(Kurvature k, CalcType calc = CalcType.C,
            bool ten = true, bool ca = true)
        {
            double n = 0.0;
            double mx = 0.0;
            double my = 0.0;

            foreach (var area in Areas)
            {
                if (area.Material?.Type == MatType.Concrete)
                {
                    area.SetEps(k, calc, ten, ca);
                    foreach (var fiber in area.Fibers)
                        Add(fiber, ref n, ref mx, ref my);
                    continue;
                }

                if (area.Material?.Type != MatType.ReSteelF)
                    continue;
                if (!area.Diagramms.TryGetValue(calc, out var diagram))
                    throw new InvalidOperationException($"Нет диаграммы {calc} для области {area.Tag}");

                foreach (var fiber in area.Fibers)
                {
                    double eps = k.e0 + k.ky * fiber.Y + k.kz * fiber.X;
                    fiber.Eps = eps;
                    double totalEps = eps + fiber.Eps_p;
                    double stress = diagram.Sig(totalEps, out double tangent, ten, ca);
                    double psi = 1.0;
                    if (_psiMode != PsiMode.None && _epsCrc.TryGetValue(fiber, out double epsCrc))
                    {
                        psi = PsiValue(_psiMode, epsCrc, totalEps);
                        stress /= psi;
                        tangent /= psi;
                    }

                    fiber.Sig = stress;
                    fiber.E2 = tangent;
                    fiber.E = Math.Abs(totalEps) > 1e-20 ? stress / totalEps : 0.0;
                    fiber.N = stress * fiber.Area;
                    fiber.Mx = fiber.N * fiber.Y;
                    fiber.My = fiber.N * fiber.X;
                    Add(fiber, ref n, ref mx, ref my);
                }
            }

            return new Load { Calc = calc, N = n, Mx = mx, My = my };
        }

        static void Add(Fiber fiber, ref double n, ref double mx, ref double my)
        {
            n += fiber.N;
            mx += fiber.Mx;
            my += fiber.My;
        }
    }
}
