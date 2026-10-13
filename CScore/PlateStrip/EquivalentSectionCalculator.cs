using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Строит линейное эквивалентное сечение по геометрии полосы и плитному источнику.</summary>
public static class EquivalentSectionCalculator
{
    const double DroppedTermTolerance = 1e-12;

    public static EquivalentSectionBuildResult Build(
        PlateStripBeamAnalogy? analogy,
        IPlateSectionResponse? centerlineSource,
        IReadOnlyList<IPlateSectionResponse>? widthSources,
        ReductionPolicy policy,
        int widthIntegrationPoints = 2,
        StripBeamFormulation formulation = StripBeamFormulation.EulerBernoulli)
    {
        if (analogy == null)
            return Fail("equivalent_section_invalid_strip", "Геометрия полосы не задана.");
        if (centerlineSource == null)
            return Fail("equivalent_section_missing_source", "Источник плитного отклика не задан.");
        if (!(analogy.ExplicitWidthM > 0.0) || !double.IsFinite(analogy.ExplicitWidthM))
            return Fail("equivalent_section_invalid_width", "Ширина полосы должна быть конечной и положительной.");
        if (!(analogy.Geometry.LengthM > 0.0) || !double.IsFinite(analogy.Geometry.LengthM))
            return Fail("equivalent_section_invalid_strip", "Длина полосы должна быть конечной и положительной.");
        if (widthIntegrationPoints < 2 || widthIntegrationPoints > 32)
            return Fail("equivalent_section_invalid_quadrature", "Число точек интегрирования должно быть от 2 до 32.");

        var tangent = centerlineSource.Tangent(ShellStrainState.Zero);
        var h = PlateSectionResponseMath.BuildH(tangent.A, tangent.B, tangent.D);
        List<FemValidationDiagnostic> diagnostics;
        double[,]? kBeam;
        double gj = 0.0, gaY = 0.0, gaZ = 0.0;
        bool timoshenko = formulation == StripBeamFormulation.Timoshenko;
        switch (policy)
        {
            case ReductionPolicy.DirectUniaxial:
                kBeam = Direct(h, analogy.ExplicitWidthM, out diagnostics);
                if (timoshenko)
                {
                    gj = 4.0 * analogy.ExplicitWidthM * h[5, 5];
                    gaY = gaZ = StripShear.RectangleCorrection * analogy.ExplicitWidthM * h[2, 2];
                }
                break;
            case ReductionPolicy.ConstitutiveIntegration:
                if (widthSources == null || widthSources.Count != widthIntegrationPoints)
                    return Fail("equivalent_section_source_count_mismatch",
                        "Число источников по ширине должно совпадать с числом точек интегрирования.");
                kBeam = Integrate(widthSources, analogy.ExplicitWidthM, widthIntegrationPoints, out diagnostics);
                if (timoshenko)
                {
                    var full = NonlinearStripTimoshenkoSectionLaw.Integrate(
                        analogy.ExplicitWidthM, widthSources, BeamStrainState.Zero, 0.0, null);
                    gj = full.K[3, 3];
                    gaY = full.GAy;
                    gaZ = full.GAz;
                    AddCouplingDiagnostic(full.K, diagnostics);
                }
                break;
            case ReductionPolicy.EnergyEquivalent:
            {
                if (widthSources == null || widthSources.Count != widthIntegrationPoints)
                    return Fail("equivalent_section_source_count_mismatch",
                        "Число источников по ширине должно совпадать с числом точек интегрирования.");
                diagnostics = [];
                // ε_x, κ_x удерживаются; при кручении — ещё κ_xy = 2θ′.
                int[] retained = timoshenko ? [0, 3, 5] : [0, 3];
                StripTimoshenkoSectionResponse energy;
                try
                {
                    energy = NonlinearStripTimoshenkoSectionLaw.Integrate(
                        analogy.ExplicitWidthM, widthSources, BeamStrainState.Zero, 0.0, retained);
                }
                catch (InvalidOperationException)
                {
                    return Fail("equivalent_section_energy_singular",
                        "Блок конденсируемых деформаций плитной жёсткости вырожден: EnergyEquivalent " +
                        "не вычисляется.");
                }
                kBeam = new double[3, 3];
                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    kBeam[i, j] = energy.K[i, j];
                if (timoshenko)
                {
                    gj = energy.K[3, 3];
                    gaY = energy.GAy;
                    gaZ = energy.GAz;
                    AddCouplingDiagnostic(energy.K, diagnostics);
                }
                break;
            }
            default:
                return Fail("equivalent_section_unsupported_policy", "Выбранная политика редукции не поддерживается.");
        }

        if (kBeam == null)
            return Fail("equivalent_section_unsupported_policy", "Выбранная политика редукции не поддерживается.");

        var section = new EquivalentSection
        {
            Tag = analogy.Id,
            SourceRegionId = analogy.SourceRegionId,
            Strip = analogy,
            ReductionPolicy = policy,
            SourceKind = centerlineSource.SourceKind,
            WidthIntegrationPoints = widthIntegrationPoints,
            BeamTangent = kBeam,
            EA = kBeam[0, 0],
            EIy = kBeam[1, 1],
            EIz = kBeam[2, 2],
            TorsionalStiffness = gj,
            ShearStiffnessGAy = gaY,
            ShearStiffnessGAz = gaZ,
            BeamFormulation = formulation,
            IsCalculable = true,
            Diagnostics = diagnostics,
            ResultFingerprint = timoshenko
                ? EquivalentSectionFingerprint.ComputeResult(kBeam, gj, gaY, gaZ)
                : EquivalentSectionFingerprint.ComputeResult(kBeam)
        };
        return new(true, section, diagnostics);
    }

    /// <summary>Физически масштабированные точки/веса квадратуры Гаусса–Лежандра по ширине
    /// полосы: v ∈ [-width/2, +width/2], сумма весов = width. Общая точка правды для
    /// интегрирования здесь и для резолва пространственного армирования вызывающей стороной.</summary>
    public static (double[] V, double[] Weights) WidthGaussPoints(double width, int n)
    {
        var (points, weights) = GaussLegendre(n);
        var v = new double[n];
        var w = new double[n];
        for (int i = 0; i < n; i++)
        {
            v[i] = points[i] * width / 2.0;
            w[i] = weights[i] * width / 2.0;
        }
        return (v, w);
    }

    /// <summary>Связи кручения с N/My/Mz в 4×4 касательной в линейном срезе отбрасываются:
    /// GJ берётся по диагонали. Значимая связь (трещины, несимметричное армирование) — повод
    /// для предупреждения.</summary>
    static void AddCouplingDiagnostic(double[,] k4, List<FemValidationDiagnostic> diagnostics)
    {
        double scale = Math.Sqrt(Math.Abs(k4[3, 3]));
        for (int j = 0; j < 3; j++)
        {
            double reference = scale * Math.Sqrt(Math.Abs(k4[j, j]));
            if (reference > 0.0 && Math.Abs(k4[3, j]) > 1e-9 * reference)
            {
                diagnostics.Add(new(
                    "equivalent_section_torsion_coupling_dropped",
                    "Связь кручения с осевой/изгибными компонентами в линейном сечении отброшена.",
                    false));
                return;
            }
        }
    }

    static double[,] Direct(double[,] h, double width, out List<FemValidationDiagnostic> diagnostics)
    {
        diagnostics = [];
        var aXX = h[0, 0];
        var bXX = h[0, 3];
        var dXX = h[3, 3];
        var result = new double[3, 3];
        result[0, 0] = aXX * width;
        result[1, 1] = dXX * width;
        result[2, 2] = aXX * width * width * width / 12.0;

        bool dropped = Math.Abs(bXX) > DroppedTermTolerance;
        for (int i = 0; i < 6 && !dropped; i++)
        for (int j = 0; j < 6 && !dropped; j++)
        {
            bool retained = (i == 0 && j == 0) || (i == 0 && j == 3) ||
                            (i == 3 && j == 0) || (i == 3 && j == 3);
            if (!retained && Math.Abs(h[i, j]) > DroppedTermTolerance) dropped = true;
        }
        if (dropped)
            diagnostics.Add(new(
                "equivalent_section_direct_dropped_terms",
                "DirectUniaxial отбросил ненулевые связи плитного отклика.",
                false));
        return result;
    }

    /// <summary>Линейная сборка: та же интеграция BᵀHB по ширине, что и в нелинейном режиме,
    /// но в нулевом состоянии. Тело вынесено в NonlinearStripSection (Срез 7), где состояние
    /// стало параметром; здесь оно фиксировано нулём, поэтому результат Срезов 2–6 не изменился.</summary>
    static double[,] Integrate(IReadOnlyList<IPlateSectionResponse> sources, double width, int pointCount,
                               out List<FemValidationDiagnostic> diagnostics)
    {
        diagnostics = [];
        return NonlinearStripSection.IntegrateTangent(sources, width, pointCount, BeamStrainState.Zero);
    }

    static (double[] Points, double[] Weights) GaussLegendre(int n)
    {
        var points = new double[n];
        var weights = new double[n];
        int half = (n + 1) / 2;
        const double tolerance = 1e-15;
        for (int i = 0; i < half; i++)
        {
            double z = Math.Cos(Math.PI * (i + 0.75) / (n + 0.5));
            double zPrevious;
            double derivative;
            do
            {
                double p0 = 1.0;
                double p1 = z;
                for (int k = 2; k <= n; k++)
                {
                    double p2 = ((2.0 * k - 1.0) * z * p1 - (k - 1.0) * p0) / k;
                    p0 = p1;
                    p1 = p2;
                }
                derivative = n * (z * p1 - p0) / (z * z - 1.0);
                zPrevious = z;
                z -= p1 / derivative;
            }
            while (Math.Abs(z - zPrevious) > tolerance);

            points[i] = -z;
            points[n - 1 - i] = z;
            double weight = 2.0 / ((1.0 - z * z) * derivative * derivative);
            weights[i] = weight;
            weights[n - 1 - i] = weight;
        }
        return (points, weights);
    }

    static EquivalentSectionBuildResult Fail(string code, string message) =>
        new(false, null, [new FemValidationDiagnostic(code, message)]);
}
