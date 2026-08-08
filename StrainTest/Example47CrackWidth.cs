using CScore;

/// <summary>Тестовый расчёт ширины трещины примера 47 через εs,avg по п. 8.2.32.</summary>
internal static class Example47CrackWidth
{
    public static void Run(bool includeConcreteTension = false)
    {
        // Базовый режим отключает растянутый бетон; отдельный режим позволяет
        // включить растягивающую ветвь L3 для сравнительного расчёта.
        bool concreteTension = includeConcreteTension;
        var section = Example47Curvature.CreateSection();

        var epsCrcN = Example47Curvature.CrackPlaneRebarStrains(section, CalcType.N, concreteTension);

        var referenceN = Example47Curvature.Solve(
            section, CalcType.N, Example47Curvature.CrackingMoment, epsCrcN,
            applyPsi: false, concreteTension: concreteTension);

        var n50 = Example47Curvature.Solve(
            section, CalcType.N, Example47Curvature.LongMoment, epsCrcN,
            applyPsi: true, concreteTension: concreteTension,
            initialGuess: referenceN.Curvature);
        var n60 = Example47Curvature.Solve(
            section, CalcType.N, Example47Curvature.ShortMoment, epsCrcN,
            applyPsi: true, concreteTension: concreteTension,
            initialGuess: n50.Curvature);

        var n50NoPsi = Example47Curvature.Solve(
            section, CalcType.N, Example47Curvature.LongMoment, epsCrcN,
            applyPsi: false, concreteTension: concreteTension,
            initialGuess: referenceN.Curvature);
        var n60NoPsi = Example47Curvature.Solve(
            section, CalcType.N, Example47Curvature.ShortMoment, epsCrcN,
            applyPsi: false, concreteTension: concreteTension,
            initialGuess: n50NoPsi.Curvature);

        var (abt, ls) = CrackSpacing(referenceN.Curvature);
        double epsN50 = AverageTensileStrain(section, n50.Curvature);
        double epsN60 = AverageTensileStrain(section, n60.Curvature);
        double epsN50NoPsi = AverageTensileStrain(section, n50NoPsi.Curvature);
        double epsN60NoPsi = AverageTensileStrain(section, n60NoPsi.Curvature);

        double sigmaN50Raw = RebarStress(section, CalcType.N, epsN50, 1.0);
        double sigmaN60Raw = RebarStress(section, CalcType.N, epsN60, 1.0);
        double sigmaN50Effective = RebarStress(section, CalcType.N, epsN50, n50.Psi);
        double sigmaN60Effective = RebarStress(section, CalcType.N, epsN60, n60.Psi);
        double sigmaN50NoPsi = RebarStress(section, CalcType.N, epsN50NoPsi, 1.0);
        double sigmaN60NoPsi = RebarStress(section, CalcType.N, epsN60NoPsi, 1.0);
        double xN50 = CompressionZoneHeight(n50.Curvature);
        double xN60 = CompressionZoneHeight(n60.Curvature);
        double xN50NoPsi = CompressionZoneHeight(n50NoPsi.Curvature);
        double xN60NoPsi = CompressionZoneHeight(n60NoPsi.Curvature);

        var kSecN50 = Example47Curvature.SecantStiffnessAt(
            section, CalcType.N, n50.Curvature, epsCrcN, applyPsi: true, concreteTension: concreteTension);
        var kSecN60 = Example47Curvature.SecantStiffnessAt(
            section, CalcType.N, n60.Curvature, epsCrcN, applyPsi: true, concreteTension: concreteTension);
        var kSecN50NoPsi = Example47Curvature.SecantStiffnessAt(
            section, CalcType.N, n50NoPsi.Curvature, epsCrcN, applyPsi: false, concreteTension: concreteTension);
        var kSecN60NoPsi = Example47Curvature.SecantStiffnessAt(
            section, CalcType.N, n60NoPsi.Curvature, epsCrcN, applyPsi: false, concreteTension: concreteTension);

        // П. 6.1.26: для расчёта раскрытия трещин сжатый бетон оценивается
        // по диаграмме непродолжительного действия, поэтому a_crc1, a_crc2 и
        // a_crc3 используют CalcType.N. П. 8.2.7: a_crc,short = a_crc1 + a_crc2 - a_crc3.
        // В самой формуле 8.2.15 ψs отсутствует: εs,avg уже получена из решения 8.2.32,
        // где ψs применялся при определении равновесной плоскости деформаций.
        double acrc1 = CrackWidth8232.FromAverageStrain(epsN50, ls, phi1: 1.4, phi2: 0.5, phi3: 1.0);
        double acrc2 = CrackWidth8232.FromAverageStrain(epsN60, ls, phi1: 1.0, phi2: 0.5, phi3: 1.0);
        double acrc3 = CrackWidth8232.FromAverageStrain(epsN50, ls, phi1: 1.0, phi2: 0.5, phi3: 1.0);
        double acrcLongMm = acrc1 * 1000.0;
        double acrcShortMm = (acrc1 + acrc2 - acrc3) * 1000.0;
        double fullCurvature = n60.Curvature.ky;
        double fullCurvatureNoPsi = n60NoPsi.Curvature.ky;

        Console.WriteLine("Пример 47: ширина раскрытия трещин через εs,avg по СП 63.13330");
        Console.WriteLine("Единицы: м, кН, кН·м, кПа; нижняя арматура => Mx < 0");
        Console.WriteLine($"Модель: B15 L3, A400, Mcrc=38 кН·м; бетон — N по п. 6.1.26; " +
            $"растяжение бетона: {(concreteTension ? "включено" : "выключено")}; ψs только в равновесии 8.2.32");
        Console.WriteLine();
        Console.WriteLine($"A_bt = {abt * 1e6:G6} мм², ls = {ls * 1000:G6} мм");
        Console.WriteLine("Состояние       κy с ψs       κy без ψs     εs с ψs       εs без ψs    σraw с ψs  σeq=σraw/ψ  σ без ψs   x с ψs  x без ψs");
        PrintState("N, M=50", n50, n50NoPsi, epsN50, epsN50NoPsi, sigmaN50Raw, sigmaN50Effective, sigmaN50NoPsi, xN50, xN50NoPsi);
        PrintState("N, M=60", n60, n60NoPsi, epsN60, epsN60NoPsi, sigmaN60Raw, sigmaN60Effective, sigmaN60NoPsi, xN60, xN60NoPsi);
        Console.WriteLine();
        Console.WriteLine("Секущие матрицы Ksec: строки [N, Mx, My], столбцы [ε0, κy, κz]");
        Console.WriteLine("Маска обозначений:");
        Console.WriteLine("  [ EA    ESx   ESy  ]");
        Console.WriteLine("  [ ESx   EIx   EIxy ]");
        Console.WriteLine("  [ ESy   EIxy  EIy  ]");
        Console.WriteLine("Диагональные единицы: K00 — кН; K11, K22 — кН·м²; смешанные — кН·м");
        PrintMatrix("N, M=50, с ψs", kSecN50);
        PrintMatrix("N, M=50, без ψs", kSecN50NoPsi);
        PrintMatrix("N, M=60, с ψs", kSecN60);
        PrintMatrix("N, M=60, без ψs", kSecN60NoPsi);
        Console.WriteLine();
        Console.WriteLine($"κ(N, M=60) с ψs   = {fullCurvature:G8} 1/м");
        Console.WriteLine($"κ(N, M=60) без ψs = {fullCurvatureNoPsi:G8} 1/м");
        Console.WriteLine();
        Console.WriteLine($"a_crc1 (длительная)       = {acrcLongMm:G8} мм");
        Console.WriteLine($"a_crc2 (полная, кратк.)    = {acrc2 * 1000.0:G8} мм");
        Console.WriteLine($"a_crc3 (длительная, кратк.) = {acrc3 * 1000.0:G8} мм");
        Console.WriteLine($"a_crc,long                 = {acrcLongMm:G8} мм");
        Console.WriteLine($"a_crc,short                = {acrcShortMm:G8} мм");

        AssertConverged("N, 50", n50);
        AssertConverged("N, 60", n60);
        AssertConverged("N, 50 без ψs", n50NoPsi);
        AssertConverged("N, 60 без ψs", n60NoPsi);
        if (ls <= 0.0 || acrcLongMm <= 0.0 || acrcShortMm <= 0.0)
            throw new InvalidOperationException("Расчёт ширины трещины дал неположительный результат");
    }

    static double AverageTensileStrain(CrossSection section, Kurvature plane)
    {
        var strains = section.Areas
            .Where(a => a.Material?.Type is MatType.ReSteelF or MatType.ReSteelU)
            .SelectMany(a => a.Fibers)
            .Where(f => f.TypeFiber == FiberType.point)
            .Select(f => plane.e0 + plane.ky * f.Y + plane.kz * f.X)
            .Where(eps => eps > 0.0)
            .ToList();

        if (strains.Count == 0)
            throw new InvalidOperationException("В сечении нет растянутых стержней");

        return strains.Average();
    }

    static double RebarStress(CrossSection section, CalcType calc, double averageStrain, double psi)
    {
        var rebar = section.Areas.FirstOrDefault(
            a => a.Material?.Type is MatType.ReSteelF or MatType.ReSteelU);
        if (rebar == null || !rebar.Diagramms.TryGetValue(calc, out var diagram))
            throw new InvalidOperationException($"Нет диаграммы арматуры для расчёта {calc}");

        double stress = diagram.Sig(averageStrain, out _);
        return stress / psi / 1000.0; // кПа -> МПа
    }

    static double CompressionZoneHeight(Kurvature plane)
    {
        if (Math.Abs(plane.ky) < 1e-12)
            throw new InvalidOperationException("Не удалось определить нейтральную ось");

        double yNeutral = -plane.e0 / plane.ky;
        double yBottom = -Example47Curvature.Height / 2.0;
        double yTop = Example47Curvature.Height / 2.0;
        double height = plane.ky < 0.0
            ? yTop - yNeutral
            : yNeutral - yBottom;

        return Math.Clamp(height, 0.0, Example47Curvature.Height) * 1000.0;
    }

    static void PrintState(
        string title,
        Example47Curvature.SolveResult withPsi,
        Example47Curvature.SolveResult withoutPsi,
        double epsWithPsi,
        double epsWithoutPsi,
        double sigmaWithPsiRaw,
        double sigmaWithPsiEffective,
        double sigmaWithoutPsi,
        double xWithPsiMm,
        double xWithoutPsiMm)
    {
        Console.WriteLine($"{title,-13} {withPsi.Curvature.ky,12:G6} {withoutPsi.Curvature.ky,14:G6} " +
            $"{epsWithPsi,12:G6} {epsWithoutPsi,13:G6} {sigmaWithPsiRaw,11:G6} " +
            $"{sigmaWithPsiEffective,13:G6} {sigmaWithoutPsi,11:G6} {xWithPsiMm,10:G6} {xWithoutPsiMm,11:G6}");
    }

    static void PrintMatrix(string title, double[,] matrix)
    {
        Console.WriteLine(title);
        for (int row = 0; row < 3; row++)
            Console.WriteLine($"  [{matrix[row, 0],14:G8} {matrix[row, 1],14:G8} {matrix[row, 2],14:G8}]");
    }

    static (double Abt, double Ls) CrackSpacing(Kurvature crackPlane)
    {
        if (Math.Abs(crackPlane.ky) < 1e-12)
            throw new InvalidOperationException("Не удалось определить высоту растянутой зоны");

        double yNeutral = -crackPlane.e0 / crackPlane.ky;
        double yBottom = -Example47Curvature.Height / 2.0;
        double yTop = Example47Curvature.Height / 2.0;
        double tensionHeight = crackPlane.ky < 0.0
            ? yNeutral - yBottom
            : yTop - yNeutral;

        double h0 = Example47Curvature.Height - Example47Curvature.RebarDepth;
        tensionHeight = Math.Clamp(
            Math.Max(tensionHeight, 0.0),
            2.0 * Example47Curvature.RebarDepth,
            0.5 * h0);

        double abt = Example47Curvature.Width * tensionHeight;
        double ls = 0.5 * abt / Example47Curvature.RebarArea * Example47Curvature.RebarDiameter;
        double lsMin = Math.Max(10.0 * Example47Curvature.RebarDiameter, 0.10);
        double lsMax = Math.Min(40.0 * Example47Curvature.RebarDiameter, 0.40);
        ls = Math.Clamp(ls, lsMin, lsMax);
        return (abt, ls);
    }

    static void AssertConverged(string title, Example47Curvature.SolveResult result)
    {
        if (!result.Converged || result.Residual > 0.05)
            throw new InvalidOperationException($"{title}: StrainSolver не сошёлся, R={result.Residual:G8}");
    }
}
