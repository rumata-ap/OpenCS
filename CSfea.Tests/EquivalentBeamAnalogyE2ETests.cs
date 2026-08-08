using CScore;
using CScore.PlateStrip;
using CSfea.Core;
using CSfea.CScoreBridge;

namespace CSfea.Tests;

/// <summary>Аналитическая E2E-проверка стержневой аналогии полосы плиты: реальный прогон
/// FrameMesh2D с EquivalentBeamResponse против замкнутой формулы консоли (P·L³/3·EI, P·L/EA)
/// для однородной упругой прямоугольной плиты без трещин.</summary>
public static class EquivalentBeamAnalogyE2ETests
{
    // Материал: числовое значение E используется в том же смысле, в каком его трактует
    // PlateSection.Compute()/ComputeTangent() — Nx=E·eps·h БЕЗ дополнительного множителя
    // ×1000 (подтверждено диагностическим прогоном; расходится с распространённым в проекте
    // предположением "E в МПа, ×1000 для кН/м" — см. память platesection-kpa-not-mpa-units,
    // тот же известный разъезд единиц, что и в pre-existing FAIL PlateModelTests/
    // CScoreBridgeTests). Для аналитики этого теста это не физическая реалистичность
    // конкретного материала, а внутренняя согласованность E2E-цепочки.
    const double E = 30_000.0;
    const double B = 2.0;      // ширина полосы, м
    const double H = 0.3;      // толщина плиты, м (сознательно b != h — иначе перепутанная
                                // ось EIy/EIz в FrameMesh2D осталась бы незамеченной)
    const double L = 4.0;      // длина консоли, м

    public static void RunAll()
    {
        TestHarness.Section("Стержневая аналогия полосы плиты: консоль, поперечная сила vs аналитика (2D, EIz)");
        RunCantileverTransverse();

        TestHarness.Section("Стержневая аналогия полосы плиты: консоль, осевая сила vs аналитика (EA)");
        RunCantileverAxial();

        TestHarness.Section("Стержневая аналогия полосы плиты: однородная квадратура по ширине не искажает EIz");
        RunUniformQuadratureMatchesAnalytic();
    }

    static void RunCantileverTransverse()
    {
        var equivalent = BuildEquivalentSection();
        var response = new EquivalentBeamResponse(equivalent);
        var mesh = Cantilever(response, out int last);

        // FrameMesh2D/BeamElements.Beam2dKLocal используют EIz для DOF поперечного
        // перемещения v в плоскости 2D-рамы (не EIy — тот только для 3D вне-плоскостного изгиба).
        double eIzAnalyticKN = E * H * Math.Pow(B, 3) / 12.0; // "кН·м²" в единицах EquivalentSection
        double eIzAnalyticN = eIzAnalyticKN * 1000.0;         // Н·м² (граница CSfea, UnitScale.Moment)

        double p = 1000.0; // Н
        var f = new double[mesh.NDof];
        f[3 * last + 1] = p;
        var u = mesh.SolveLinear(f, new[] { 0, 1, 2 });
        double vTip = u[3 * last + 1];
        double vAnalytic = p * L * L * L / (3.0 * eIzAnalyticN);
        TestHarness.CheckRel("v_tip (P·L³/3·EIz, независимая аналитика E·h·b³/12)", vTip, vAnalytic, 0.02);
    }

    static void RunCantileverAxial()
    {
        var equivalent = BuildEquivalentSection();
        var response = new EquivalentBeamResponse(equivalent);
        var mesh = Cantilever(response, out int last);

        double eaAnalyticKN = E * H * B;            // "кН" в единицах EquivalentSection
        double eaAnalyticN = eaAnalyticKN * 1000.0; // Н (граница CSfea, UnitScale.Force)

        double p = 5.0e5; // Н
        var f = new double[mesh.NDof];
        f[3 * last + 0] = p;
        var u = mesh.SolveLinear(f, new[] { 0, 1, 2 });
        double uTip = u[3 * last + 0];
        double uAnalytic = p * L / eaAnalyticN;
        TestHarness.CheckRel("u_tip (P·L/EA, независимая аналитика E·h·b)", uTip, uAnalytic, 1e-6);
    }

    static void RunUniformQuadratureMatchesAnalytic()
    {
        var equivalent = BuildEquivalentSection();
        double eIzAnalyticKN = E * H * Math.Pow(B, 3) / 12.0;
        TestHarness.CheckRel(
            "EIz_equiv (ConstitutiveIntegration, однородно по ширине, без зон) vs E·h·b³/12",
            equivalent.EIz, eIzAnalyticKN, 1e-6);
    }

    static FrameMesh2D Cantilever(IBeamSectionResponse response, out int last)
    {
        const int nEl = 8;
        var nodes = new double[nEl + 1][];
        for (int i = 0; i <= nEl; i++) nodes[i] = new[] { i * L / nEl, 0.0 };
        var elems = new (int, int)[nEl];
        for (int i = 0; i < nEl; i++) elems[i] = (i, i + 1);
        var responses = new IBeamSectionResponse[nEl];
        for (int i = 0; i < nEl; i++) responses[i] = response;
        last = nEl;
        return new FrameMesh2D(nodes, elems, responses);
    }

    static EquivalentSection BuildEquivalentSection()
    {
        var analogy = new PlateStripBeamAnalogy
        {
            Id = "strip-e2e",
            SourceRegionId = 1,
            ExplicitWidthM = B,
            Fingerprint = "strip-e2e-fp",
            Geometry = new PlateStripGeometry { LengthM = L }
        };
        var source = LinearSnapshot();
        var widthSources = new IPlateSectionResponse[] { source, source };
        var build = EquivalentSectionCalculator.Build(
            analogy, source, widthSources, ReductionPolicy.ConstitutiveIntegration, 2);
        if (!build.IsCalculable || build.Section == null)
            throw new InvalidOperationException(
                "Не удалось построить эквивалентное сечение для E2E-теста: " +
                string.Join("; ", build.Diagnostics.Select(d => d.Message)));
        return build.Section;
    }

    static IPlateSectionResponse LinearSnapshot()
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = E, Ry = 600, Ru = 600, Ft = 600, Fc = -600,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = E, Type = MatType.ReSteelF, Tag = "lin" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        var diagram = m.GetDiagramms(DiagrammType.L2)![CalcType.C];

        var section = new PlateSection { H = H, NLayers = 20, TensionConcrete = true, PlateModel = "layered" };
        var result = PlateSectionTangentSnapshot.Create(section, diagram, diagram);
        if (!result.IsCalculable || result.Source == null)
            throw new InvalidOperationException("Не удалось построить линейный снимок PlateSection для E2E-теста.");
        return result.Source;
    }
}
