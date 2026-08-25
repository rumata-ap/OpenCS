using System;
using System.Linq;
using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Путь нагружения диаграммы кривизна-момент при двухплоскостном изгибе. Кривая строится
/// для ЗАДАННОГО соотношения Mx:My, поэтому все её точки обязаны лежать на луче
/// Mx/My = Mx0/My0 — в обоих режимах расстановки вспомогательных точек.
/// </summary>
public class BiaxialCurvatureProportionalPathTests
{
    const double Mx0 = -60.0;
    const double My0 = -20.0;
    const double TargetRatio = Mx0 / My0;

    static BiaxialCurvatureCurveResult Compute(CurveStepMode stepMode, bool usePsi)
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section,
            calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: stepMode);
        return solver.Compute(0.0, Mx0, My0, CurvatureNMode.Constant, usePsi);
    }

    [Theory]
    [InlineData(CurveStepMode.ByCurvature, true)]
    [InlineData(CurveStepMode.ByCurvature, false)]
    [InlineData(CurveStepMode.ByMoment, true)]
    [InlineData(CurveStepMode.ByMoment, false)]
    public void Points_StayOnTheProportionalLoadingRay(CurveStepMode stepMode, bool usePsi)
    {
        var result = Compute(stepMode, usePsi);

        // Участок 2 исключён: это не путь нагружения, а вспомогательная петля перехода
        // «без трещины» → «с трещиной» при неизменных усилиях концов. Её промежуточные точки —
        // интерполяция состояния сечения, их усилия по построению уходят с луча и на график
        // основной кривой не попадают (при ψs она вообще не строится).
        var offRay = result.Points
            .Where(p => p.Converged && p.Segment != 2 && Math.Abs(p.My) > 1e-6)
            .Where(p => Math.Abs(p.Mx / p.My - TargetRatio) > 0.02 * Math.Abs(TargetRatio))
            .ToList();

        Assert.True(offRay.Count == 0,
            "Точки вне луча: " + string.Join(", ",
                offRay.Select(p => $"уч.{p.Segment} Mx={p.Mx:F2} My={p.My:F2} Mx/My={p.Mx / p.My:F3}")));
    }

    [Theory]
    [InlineData(CurveStepMode.ByCurvature)]
    [InlineData(CurveStepMode.ByMoment)]
    public void ControlPoints_StayOnTheProportionalLoadingRay(CurveStepMode stepMode)
    {
        var result = Compute(stepMode, usePsi: true);

        foreach (var (tag, point) in new[]
                 {
                     ("трещинообразование", result.Cracking),
                     ("текучесть", result.Yield),
                     ("предел", result.Ultimate),
                 })
        {
            Assert.NotNull(point);
            Assert.True(Math.Abs(point!.Mx / point.My - TargetRatio) <= 0.02 * Math.Abs(TargetRatio),
                $"Контрольная точка «{tag}» вне луча: Mx/My = {point.Mx / point.My:F3}, ожидалось {TargetRatio:F3}");
        }
    }

    [Theory]
    [InlineData(CurveStepMode.ByCurvature)]
    [InlineData(CurveStepMode.ByMoment)]
    public void PostCrackingSegment_ResolvesTheStiffnessDropZone(CurveStepMode stepMode)
    {
        var result = Compute(stepMode, usePsi: true);
        var cracking = result.Cracking;
        var yield = result.Yield;
        Assert.NotNull(cracking);
        Assert.NotNull(yield);

        // Сразу за Mcrc жёсткость обваливается; зона обвала — первые ~15% участка "3".
        // Без сгущения туда не попадала ни одна точка, и весь обвал рисовался одним отрезком.
        double dropZoneEnd = Math.Abs(cracking!.Mx)
            + 0.15 * (Math.Abs(yield!.Mx) - Math.Abs(cracking.Mx));
        int inDropZone = result.Points.Count(p =>
            p.Converged && p.Segment == 3 &&
            Math.Abs(p.Mx) > Math.Abs(cracking.Mx) && Math.Abs(p.Mx) < dropZoneEnd);

        Assert.True(inDropZone >= 3,
            $"В зоне обвала жёсткости {Math.Abs(cracking.Mx):F1}…{dropZoneEnd:F1} кН·м всего {inDropZone} точек");
    }
}
