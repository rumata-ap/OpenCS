using Xunit;

namespace CScore.Tests;

/// <summary>
/// П. 6.1.24 СП 63: образование трещин по НДМ — трёхлинейная диаграмма бетона. Сечение с двухлинейной
/// диаграммой бетона (L2) даёт Mcrc как с трёхлинейной; раскрытие считается по диаграмме области.
/// </summary>
public sealed class CrackFormationSectionTests
{
    static CrossSection Example47(DiagrammType concreteDiagram)
    {
        var section = TestSections.Example47(nx: 6, ny: 100);
        foreach (var area in section.Areas.Where(a => a.Material?.Type == MatType.Concrete))
            area.DiagrammType = concreteDiagram;
        section.ResolveAndBuildDiagramms();
        return section;
    }

    static CrackWidthResult Solve(CrossSection section, CrossSection? crc = null) =>
        new CrackWidthSolver(section, calcCrc: CalcType.N, calcService: CalcType.N, calcServiceLong: CalcType.N,
                crackFormationSection: crc)
            .Compute(N: 0.0, mxLong: -50.0, mxTotal: -60.0);   // растянут низ с арматурой, как в примере 47

    [Fact]
    public void TwoLinearConcrete_McrcAsThreeLinear_SectionUnchanged()
    {
        var l2 = Example47(DiagrammType.L2);
        var l3 = Example47(DiagrammType.L3);

        // Mcrc целиком по двухлинейной (сжатый бетон с E_b,red) — заметно меньше: замена что-то меняет.
        var plainL2 = new CrackingSolver(l2, CalcType.N).CrackingMoment(0.0, -1.0, 0.0);
        var resL3 = Solve(l3);
        Assert.True(Math.Abs(plainL2.Mx) < 0.97 * resL3.Mcrc,
            $"Mcrc по L2 {plainL2.Mx:F2} не меньше Mcrc по L3 {resL3.Mcrc:F2} — проверка не различает диаграммы.");

        // Режим разностной диаграммы арматуры задан, как в обработчиках задач, — арматура перестроена
        // от трёхлинейного бетона, Mcrc совпадает точно.
        var resL2 = Solve(l2, CrackFormationSection.For(l2, rebarDifferentialDiagram: true));
        Assert.Equal(resL3.Mcrc, resL2.Mcrc, 3);
        // Без режима диаграммы арматуры остаются прежними (разностные от L2) — расхождение мало.
        var resDefault = Solve(l2);
        Assert.InRange(resDefault.Mcrc, 0.995 * resL3.Mcrc, 1.005 * resL3.Mcrc);
        // Исходное сечение не переведено на L3: раскрытие — по его диаграмме (п. 6.1.26).
        Assert.All(l2.Areas.Where(a => a.Material?.Type == MatType.Concrete),
            a => Assert.Equal(DiagrammType.L2, a.DiagrammType));
        Assert.True(resL2.Cracked && resL3.Cracked);
        Assert.NotEqual(resL3.AcrcLong, resL2.AcrcLong);
    }

    [Fact]
    public void ThreeLinearConcrete_ReturnsSameSection()
    {
        var l3 = Example47(DiagrammType.L3);
        Assert.False(CrackFormationSection.NeedsThreeLinear(l3));
        Assert.Same(l3, CrackFormationSection.For(l3));
    }
}
