using Xunit;

namespace CScore.Tests;

// Деформационная модель (CrackWidthSolver/CrackingSolver, задача crack_width) на примере 47
// («Пособие СП63.13330.2018», раздел 4): плита фундамента h=300, b=1150, a=42 мм, B15,
// As=923 мм² (6⌀14 A400), длительный момент Ml=50 кН·м (определяющий случай по условию
// (4.32) книги), полный момент M=60 кН·м. Трёхлинейная диаграмма бетона (DiagrammType.L3) на
// растяжение и сжатие — TestSections.Example47(). Диаграмма бетона берётся ТОЛЬКО с учётом
// непродолжительного действия нагрузки (п. 6.1.26 СП63.13330) — длительная (NL/CL) диаграмма
// в расчёте раскрытия трещин не участвует вовсе, независимо от того, длительный или полный
// момент рассматривается; длительность нагрузки входит в формулу только через коэффициент φ1
// (1.4 против 1.0), см. Solve(). В отличие от упрощённой проверки Sp63CrackWidthChecker (см.
// CScore.Tests/Sp63CrackWidth/Sp63CrackWidthCheckerTests.cs →
// Check_Example47_PlateFoundation_...), здесь Mcrc ищется бисекцией по реальной нелинейной
// диаграмме (не формульным Wpl=1,3Wred), а ширина раскрытия — по официальной трёхчленной
// сумме acrc=acrc1+acrc2-acrc3 п. 8.2.7 (не через один коэффициент φ1).
public class CrackWidthSolverExample47Tests
{
    // Сходимость по числу слоёв фибр через высоту сечения: ny=12 (значение по умолчанию у
    // TestSections.Example47) уже в пределах ~1,3% от ny=400, ny=200 — в пределах ~0,005%.
    // "Достаточное деление по высоте" в остальных тестах этого файла — ny=200.
    [Theory]
    [InlineData(12, 0.014)]   // ~1,3% от предела сходимости
    [InlineData(50, 0.002)]
    [InlineData(200, 0.0002)]
    public void Compute_ConvergesWithHeightSubdivision(int ny, double relTolToFineMesh)
    {
        var fine = Solve(400);
        var coarse = Solve(ny);

        Assert.True(coarse.Cracked);
        double relDiff = System.Math.Abs(coarse.AcrcLong - fine.AcrcLong) / fine.AcrcLong;
        Assert.True(relDiff <= relTolToFineMesh,
            $"ny={ny}: acrc={coarse.AcrcLong:F6} мм отличается от мелкой сетки (ny=400, " +
            $"acrc={fine.AcrcLong:F6} мм) на {relDiff:P2} — больше допуска {relTolToFineMesh:P2}.");
    }

    [Fact]
    public void Compute_Example47_LongTermGoverns_MatchesBookWithinMethodTolerance()
    {
        var res = Solve(ny: 200);

        Assert.True(res.Cracked);
        Assert.True(res.CrcConverged);

        // Книга: Mcrc=33,22 кН·м (табличный коэфф. Wpl/Wred=1,75). Здесь Mcrc ищется бисекцией
        // по реальной трёхлинейной диаграмме до деформации разрыва Et2 — точнее, чем табличный
        // коэффициент, но для другого набора параметров диаграммы, поэтому расхождение в
        // пределах ~10% ожидаемо и не является ошибкой.
        Assert.InRange(res.Mcrc, 33.22 * 0.85, 33.22 * 1.15);

        // Книга: σs=235,9 МПа — для одиночного армирования обе методики близки.
        Assert.InRange(res.SigmaS / 1000.0, 235.9 * 0.9, 235.9 * 1.1);

        // Оба метода упираются в один и тот же абсолютный предел ls ≤ 400 мм.
        Assert.Equal(0.4, res.Ls, 3);

        // Книга: acrc=0,155 мм (продолжительное). НДМ даёт заметно МЕНЬШЕ (~15%, из-за более
        // высокого Mcrc и, как следствие, σs) — решение не в запас, поэтому верхняя граница
        // допуска не отпускается выше книги.
        Assert.InRange(res.AcrcLong, 0.155 * 0.70, 0.155 * 1.0);
        Assert.True(res.PassedLong);

        // Официальная трёхчленная сумма п. 8.2.7 (недоступна упрощённой проверке).
        Assert.Equal(res.AcrcShort, res.Acrc1 + res.Acrc2 - res.Acrc3, 6);
        Assert.True(res.AcrcShort > res.AcrcLong);
        Assert.True(res.PassedShort);
    }

    static CrackWidthResult Solve(int ny)
    {
        var section = TestSections.Example47(nx: 6, ny: ny);
        // П. 6.1.26 СП63.13330: диаграмму состояния сжатого бетона при расчёте раскрытия
        // трещин по НДМ берут ТОЛЬКО с учётом непродолжительного действия нагрузки — длительная
        // диаграмма (NL/CL) в самом расчёте раскрытия трещин не участвует. Длительная часть
        // отличается от кратковременной только моментом (mxLong вместо mxTotal) и коэффициентом
        // φ1 (1.4 вместо 1.0), а не выбором диаграммы, поэтому calcServiceLong = calcService = N.
        var solver = new CrackWidthSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            calcServiceLong: CalcType.N, phi2: 0.5, acrcUltLong: 0.3, acrcUltShort: 0.4);
        return solver.Compute(N: 0.0, mxLong: -50.0, mxTotal: -60.0);
    }
}
