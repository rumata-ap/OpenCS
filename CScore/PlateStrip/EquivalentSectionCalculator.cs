using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Строит линейное эквивалентное сечение по геометрии полосы и плитному источнику.</summary>
public static class EquivalentSectionCalculator
{
    const double DroppedTermTolerance = 1e-12;

    public static EquivalentSectionBuildResult Build(
        PlateStripBeamAnalogy? analogy,
        IPlateSectionResponse? source,
        ReductionPolicy policy,
        int widthIntegrationPoints = 2)
    {
        if (analogy == null)
            return Fail("equivalent_section_invalid_strip", "Геометрия полосы не задана.");
        if (source == null)
            return Fail("equivalent_section_missing_source", "Источник плитного отклика не задан.");
        if (!(analogy.ExplicitWidthM > 0.0) || !double.IsFinite(analogy.ExplicitWidthM))
            return Fail("equivalent_section_invalid_width", "Ширина полосы должна быть конечной и положительной.");
        if (!(analogy.Geometry.LengthM > 0.0) || !double.IsFinite(analogy.Geometry.LengthM))
            return Fail("equivalent_section_invalid_strip", "Длина полосы должна быть конечной и положительной.");
        if (widthIntegrationPoints < 2 || widthIntegrationPoints > 32)
            return Fail("equivalent_section_invalid_quadrature", "Число точек интегрирования должно быть от 2 до 32.");

        var tangent = source.Tangent(ShellStrainState.Zero);
        var h = PlateSectionResponseMath.BuildH(tangent.A, tangent.B, tangent.D);
        List<FemValidationDiagnostic> diagnostics;
        double[,]? kBeam;
        switch (policy)
        {
            case ReductionPolicy.DirectUniaxial:
                kBeam = Direct(h, analogy.ExplicitWidthM, out diagnostics);
                break;
            case ReductionPolicy.ConstitutiveIntegration:
                kBeam = Integrate(source, analogy.ExplicitWidthM, widthIntegrationPoints, out diagnostics);
                break;
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
            SourceKind = source.SourceKind,
            WidthIntegrationPoints = widthIntegrationPoints,
            BeamTangent = kBeam,
            EA = kBeam[0, 0],
            EIy = kBeam[1, 1],
            EIz = kBeam[2, 2],
            IsCalculable = true,
            Diagnostics = diagnostics,
            InputFingerprint = EquivalentSectionFingerprint.Compute(
                analogy, source, policy, widthIntegrationPoints),
            ResultFingerprint = EquivalentSectionFingerprint.ComputeResult(kBeam)
        };
        return new(true, section, diagnostics);
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

    static double[,] Integrate(IPlateSectionResponse source, double width, int pointCount,
                               out List<FemValidationDiagnostic> diagnostics)
    {
        diagnostics = [];
        var embedding = new StripKinematicEmbedding(width);
        var result = new double[3, 3];
        var (points, weights) = GaussLegendre(pointCount);
        for (int g = 0; g < points.Length; g++)
        {
            double v = points[g] * width / 2.0;
            double weight = weights[g] * width / 2.0;
            var b = embedding.Matrix(v);
            var shellState = embedding.Map(BeamStrainState.Zero, v);
            var tangent = source.Tangent(shellState);
            var h = PlateSectionResponseMath.BuildH(tangent.A, tangent.B, tangent.D);
            for (int a = 0; a < 3; a++)
            for (int c = 0; c < 3; c++)
            {
                double value = 0.0;
                for (int i = 0; i < 6; i++)
                for (int j = 0; j < 6; j++)
                    value += b[i, a] * h[i, j] * b[j, c];
                result[a, c] += weight * value;
            }
        }
        return result;
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
