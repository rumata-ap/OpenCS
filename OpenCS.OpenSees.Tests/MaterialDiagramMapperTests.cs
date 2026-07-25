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
    public void Flat_tail_receives_small_nonzero_residual_slope()
    {
        // Хвосты обеих ветвей строго горизонтальны (плато трещинообразования/раздавливания
        // бетона) — ElasticMultiLinear в OpenSees экстраполирует за пределами диаграммы наклоном
        // последнего сегмента, ровно нулевой наклон делает матрицу гибкости сечения вырожденной
        // в forceBeamColumn (см. отладку кинематических нагрузок).
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
        Assert.NotEqual(0, tensionTailSlope);

        var negative = result.NegativeEnvelope.OrderBy(p => p.Strain).ToList();
        double compressionTailSlope = (negative[1].StressPa - negative[0].StressPa) /
            (negative[1].Strain - negative[0].Strain);
        Assert.NotEqual(0, compressionTailSlope);

        Assert.Contains(result.Warnings, w => w.Contains("горизонтальным", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Concrete_tension_branch_ruptures_to_zero_beyond_ultimate_strain_with_small_residual_tail()
    {
        // Воспроизводит Diagramm.Sig(): при ε > It.X[^1] бетон на растяжении обрывается в ноль
        // (полное растрескивание). До фикса MaterialDiagramMapper никогда не сэмплировал точку
        // за пределом растяжимости, и нудж-наклон применялся к пиковому плато, придавая ему
        // ПОЛОЖИТЕЛЬНЫЙ наклон — бетон никогда не терял способность воспринимать растяжение.
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

        // Сразу за пиком — обрыв в ноль (трещина), а не рост напряжения.
        EnvelopePoint afterPeak = positive.First(p => p.Strain > 0.00015);
        Assert.True(afterPeak.Strain > peak.Strain);
        Assert.Equal(0, afterPeak.StressPa, 3);

        // Хвост за обрывом — малый ПОЛОЖИТЕЛЬНЫЙ остаточный наклон (для устойчивости обращения
        // матрицы гибкости), заметно меньший пиковой прочности, а не пропорциональный начальному
        // модулю упругости ветви.
        EnvelopePoint last = positive[^1];
        Assert.True(last.Strain > afterPeak.Strain);
        double residualSlope = (last.StressPa - afterPeak.StressPa) / (last.Strain - afterPeak.Strain);
        Assert.True(residualSlope > 0);
        Assert.True(last.StressPa < peak.StressPa);

        Assert.Contains(result.Warnings, w => w.Contains("обрыв", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConsiderConcreteTension_false_collapses_tension_branch_to_small_uniform_residual_slope()
    {
        // considerConcreteTension=false — альтернатива обрыву-в-ноль: бетон вообще не считается
        // работающим на растяжение (Diagramm.Sig(tenB:false)), без софтенинг-участка и без
        // связанных с ним проблем сходимости (см. заметку о хрупкости обрыва на реальном сценарии).
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

        // Ровно 2 точки — вся ветвь растяжения одна прямая, без внутренних плоских участков
        // (иначе волокно, деформация которого попадёт на плоский участок, снова даст вырожденную
        // касательную жёсткость — см. Баг №1 в истории отладки).
        Assert.Equal(2, positive.Count);
        Assert.Equal(0, positive[0].Strain);
        Assert.Equal(0, positive[0].StressPa);

        double slope = (positive[1].StressPa - positive[0].StressPa) / (positive[1].Strain - positive[0].Strain);
        Assert.True(slope > 0);
        // Остаточный наклон — малая доля от максимального наклона ветви СЖАТИЯ (единственный
        // ненулевой источник масштаба, т.к. сама ветвь растяжения тождественно нулевая при
        // считывании диаграммы): сегмент (-0.002, -14_500_000) -> (0, 0), наклон 7.25e9 Па/ед.деф.
        const double CompressionReferenceSlope = 7.25e9;
        Assert.True(slope < CompressionReferenceSlope * 0.01);

        // Абсолютное остаточное напряжение — на порядки меньше реальной прочности бетона на
        // растяжение (в этой диаграмме пик — 1.05 МПа): сечение фактически работает как уже
        // полностью растрескавшееся, а не набирает прочность.
        Assert.True(positive[1].StressPa < 14_500_000 * 0.01);
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
