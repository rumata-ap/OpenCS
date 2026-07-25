using CScore;
using CSmath;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Tests;

public sealed class MaterialDiagramMapperTests
{
    [Theory]
    [InlineData(DiagrammType.L2)]
    [InlineData(DiagrammType.L3)]
    [InlineData(DiagrammType.SP63)]
    public void Standard_diagram_contains_zero_critical_strains_and_deterministic_samples(DiagrammType type)
    {
        Diagramm diagram = CreateDiagram(type);

        OpenSeesMaterialDefinition result = MaterialDiagramMapper.Map(
            diagram,
            tag: 7,
            sourceId: "concrete-1",
            sourceType: MatType.Concrete);

        double[] critical = diagram.GetCriticalStrains();
        double[] strains = result.PositiveEnvelope
            .Concat(result.NegativeEnvelope)
            .Select(point => point.Strain)
            .ToArray();

        Assert.Contains(0, strains);
        foreach (double strain in critical)
            Assert.Contains(strain, strains);

        Assert.Contains(result.PositiveEnvelope, point =>
            Math.Abs(point.Strain - 0.00005) < 1e-12);
        Assert.Equal(result.PositiveEnvelope.Select(point => point.Strain).OrderBy(x => x),
            result.PositiveEnvelope.Select(point => point.Strain));
        Assert.Contains(result.Warnings, warning => warning.Contains("монотон", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Flat_compression_tail_receives_small_nonzero_residual_slope_while_concrete_tension_stays_exactly_zero()
    {
        // Хвост сжатия строго горизонтален (плато раздавливания бетона) — ElasticMultiLinear в
        // OpenSees экстраполирует за пределами диаграммы наклоном последнего сегмента, ровно
        // нулевой наклон делает матрицу гибкости сечения вырожденной в forceBeamColumn (см.
        // отладку кинематических нагрузок) — поэтому сжатие всё ещё получает малый нудж-наклон.
        // Растяжение бетона, напротив, ВСЕГДА идёт через AppendConcreteTensionRupture — строго
        // горизонтальный хвост при нулевом напряжении, осознанно, без нуджа (см. комментарии
        // ResidualTailSlopeFraction/AppendConcreteTensionRupture про приоритет корректного
        // распределения напряжений над устойчивостью солвера).
        Diagramm diagram = new(
            new LSpline([-0.003, -0.002, 0], [-2_000, -2_000, 0]),
            new LSpline([0, 0.0001, 0.0002], [0, 500, 500]),
            DiagrammType.Custom,
            MatType.Concrete,
            DiagrammType.Custom.ToString());

        OpenSeesMaterialDefinition result = MaterialDiagramMapper.Map(
            diagram, tag: 5, sourceId: "concrete-flat", sourceType: MatType.Concrete);

        var positive = result.PositiveEnvelope.OrderBy(p => p.Strain).ToList();
        double tensionTailSlope = (positive[^1].StressPa - positive[^2].StressPa) /
            (positive[^1].Strain - positive[^2].Strain);
        Assert.Equal(0, tensionTailSlope, 6);

        var negative = result.NegativeEnvelope.OrderBy(p => p.Strain).ToList();
        double compressionTailSlope = (negative[1].StressPa - negative[0].StressPa) /
            (negative[1].Strain - negative[0].Strain);
        Assert.NotEqual(0, compressionTailSlope);

        Assert.Contains(result.Warnings, w => w.Contains("горизонтальным", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Concrete_tension_branch_ruptures_to_zero_beyond_ultimate_strain_and_stays_exactly_zero()
    {
        // Воспроизводит Diagramm.Sig(): при ε > It.X[^1] бетон на растяжении обрывается в ноль
        // (полное растрескивание) — НАВСЕГДА, без остаточного наклона. Осознанный выбор: любой,
        // сколь угодно малый ненулевой наклон вносит нефизичный вклад в интеграл по сечению при
        // больших деформациях волокон (растянутая зона перестаёт быть по-настоящему "мёртвой") —
        // это важнее устойчивости обращения матрицы гибкости в OpenSees.
        Diagramm diagram = new(
            new LSpline([-0.0035, -0.002, 0], [-14_500, -14_500, 0]),
            new LSpline([0, 0.0001, 0.00015], [0, 1_050, 1_050]),
            DiagrammType.Custom,
            MatType.Concrete,
            DiagrammType.Custom.ToString());

        OpenSeesMaterialDefinition result = MaterialDiagramMapper.Map(
            diagram, tag: 9, sourceId: "concrete-crack", sourceType: MatType.Concrete);

        var positive = result.PositiveEnvelope.OrderBy(p => p.Strain).ToList();

        EnvelopePoint peak = positive.Single(p => Math.Abs(p.Strain - 0.00015) < 1e-12);
        Assert.Equal(1_050_000, peak.StressPa, 3);

        // Всё после пика — строго ноль напряжения, включая крайнюю (экстраполируемую) точку.
        foreach (EnvelopePoint p in positive.Where(p => p.Strain > 0.00015))
            Assert.Equal(0, p.StressPa, 6);

        EnvelopePoint last = positive[^1];
        Assert.True(last.Strain > peak.Strain);

        Assert.Contains(result.Warnings, w => w.Contains("обрыв", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConsiderConcreteTension_false_collapses_tension_branch_to_exact_zero()
    {
        // considerConcreteTension=false — альтернатива обрыву-в-ноль: бетон вообще не считается
        // работающим на растяжение (Diagramm.Sig(tenB:false)). Напряжение растяжения строго ноль
        // на всём диапазоне деформаций — та же осознанная позиция, что и в тесте выше: никакого
        // остаточного наклона, даже малого, ради корректного интеграла по сечению.
        Diagramm diagram = new(
            new LSpline([-0.0035, -0.002, 0], [-14_500, -14_500, 0]),
            new LSpline([0, 0.0001, 0.00015], [0, 1_050, 1_050]),
            DiagrammType.Custom,
            MatType.Concrete,
            DiagrammType.Custom.ToString());

        OpenSeesMaterialDefinition result = MaterialDiagramMapper.Map(
            diagram, tag: 11, sourceId: "concrete-no-tension", sourceType: MatType.Concrete,
            considerConcreteTension: false);

        var positive = result.PositiveEnvelope.OrderBy(p => p.Strain).ToList();

        // Ровно 2 точки — вся ветвь растяжения одна горизонтальная прямая при нулевом напряжении.
        Assert.Equal(2, positive.Count);
        Assert.Equal(0, positive[0].Strain);
        Assert.Equal(0, positive[0].StressPa);
        Assert.True(positive[1].Strain > 0);
        Assert.Equal(0, positive[1].StressPa, 6);
    }

    [Fact]
    public void ConsiderConcreteTension_false_stays_exactly_zero_at_realistic_large_strain()
    {
        // Регрессия: ElasticMultiLinear в OpenSees экстраполирует НАКЛОН последнего сегмента
        // огибающей за пределы таблицы БЕСКОНЕЧНО. Раньше огибающая получала малый ненулевой
        // остаточный наклон "для устойчивости солвера", который на реальных деформациях волокон,
        // уходящих далеко за пределы таблицы (кинематические нагрузки, до ~9% по факту), давал
        // заметное паразитное напряжение растяжения. Теперь наклон строго нулевой — при
        // экстраполяции на любую деформацию напряжение остаётся точно нулём.
        Diagramm diagram = new(
            new LSpline([-0.0035, -0.002, 0], [-14_500, -14_500, 0]),
            new LSpline([0, 0.0001, 0.00015], [0, 1_050, 1_050]),
            DiagrammType.Custom,
            MatType.Concrete,
            DiagrammType.Custom.ToString());

        OpenSeesMaterialDefinition result = MaterialDiagramMapper.Map(
            diagram, tag: 12, sourceId: "concrete-no-tension-large-strain", sourceType: MatType.Concrete,
            considerConcreteTension: false);

        var positive = result.PositiveEnvelope.OrderBy(p => p.Strain).ToList();
        double slope = (positive[1].StressPa - positive[0].StressPa) / (positive[1].Strain - positive[0].Strain);

        Assert.Equal(0, slope, 6);
        Assert.Equal(0, slope * 0.089, 6);
    }

    [Fact]
    public void Custom_diagram_retains_source_compression_and_tension_points_in_SI()
    {
        Diagramm diagram = CreateDiagram(DiagrammType.Custom);

        OpenSeesMaterialDefinition result = MaterialDiagramMapper.Map(
            diagram,
            tag: 3,
            sourceId: "custom-1",
            sourceType: MatType.Custom);

        Assert.Contains(result.NegativeEnvelope, point =>
            point.Strain == -0.002 && point.StressPa == -2_000_000);
        Assert.Contains(result.PositiveEnvelope, point =>
            point.Strain == 0.001 && point.StressPa == 1_500_000);
    }

    private static Diagramm CreateDiagram(DiagrammType type)
    {
        if (type == DiagrammType.Custom)
        {
            return new Diagramm(
                new LSpline([-0.002, 0], [-2_000, 0]),
                new LSpline([0, 0.001], [0, 1_500]),
                type,
                MatType.ReSteelF,
                type.ToString());
        }

        double[] compressionStrains = [-0.003, -0.0015, 0];
        double[] compressionStress = [-3_000, -2_000, 0];
        double[] tensionStrains = [0, 0.0001, 0.0002];
        double[] tensionStress = [0, 500, 1_000];

        return new Diagramm(
            new LSpline(compressionStrains, compressionStress),
            new LSpline(tensionStrains, tensionStress),
            type,
            MatType.Concrete,
            type.ToString())
        { };
    }
}
