using System.Linq;
using System.Text.RegularExpressions;
using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests.Services;

/// <summary>
/// Регрессионный тест на найденную и исправленную ошибку масштабирования запасного
/// (без захваченного вида экрана) построителя эпюры разреза: при несимметричном диапазоне
/// значений (типично для деформаций бетона — сжатие и растяжение разного порядка) масштаб
/// считался по полному размаху <c>vMax-vMin</c>, тогда как <see cref="SectionCutViewTransform.ToScreen"/>
/// всегда кладёт значение 0 в ЦЕНТР области построения — из-за этого кривая эпюры вылезала
/// за границу сетки на стороне с бóльшим по модулю значением.
/// </summary>
public sealed class SectionCutSvgExporterTests
{
    // Координаты именно кривой эпюры — атрибуты points="..." у polyline/polygon.
    // Текстовые подписи осей намеренно рисуются ЗА пределами прямоугольника построения
    // (это не баг), поэтому проверять нужно только сами точки кривой, а не любые числа в SVG.
    static readonly Regex PointsAttrPattern = new(
        "points=\"([^\"]+)\"", RegexOptions.Compiled);
    static readonly Regex CoordPattern = new(
        @"(-?\d+\.\d+),(-?\d+\.\d+)", RegexOptions.Compiled);

    [Fact]
    public void Build_FallbackView_AsymmetricValueRange_KeepsCurveWithinPlotBounds()
    {
        // Профиль деформаций, явно несимметричный: сжатие -0,0035, растяжение только +0,0005 —
        // такое соотношение типично для бетона у предела прочности с небольшим растянутым
        // краем сечения (как в примере IV.Б.6.1 пособия к СП63.13330.2012).
        var points = new[]
        {
            new CutSample(0.000, 0.0, -0.260, -0.0035, -7.65, 0),
            new CutSample(0.300, 0.0,  0.040,  0.0005,  0.10, 0),
        };
        var result = new SectionCutResult(
            Start: (0.0, -0.260), End: (0.0, 0.040),
            Segments: [new CutSegment(points, AreaIndex: 0)],
            Rebars: [], NearbyRebars: []);

        string svg = SectionCutSvgExporter.Build(new SectionCutExportArgs
        {
            Result = result,
            Mode = SectionPlotMode.Strain,
            Horizontal = false,
            AsOnScreen = true,
            FillMode = true,
            HatchMode = false,
            ShowRebarForce = false,
            EpsCu = null,
        });

        // Область построения (см. BuildFallbackView): PlotOx=58, PlotW=786 → x∈[58;844].
        const double plotXMin = 58.0, plotXMax = 58.0 + 786.0;
        var xs = PointsAttrPattern.Matches(svg)
            .SelectMany(m => CoordPattern.Matches(m.Groups[1].Value))
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.NotEmpty(xs);
        foreach (double x in xs)
        {
            Assert.True(x >= plotXMin - 0.5 && x <= plotXMax + 0.5,
                $"Точка эпюры x={x} вышла за границы области построения [{plotXMin};{plotXMax}] " +
                "— проверьте масштабирование по vAbsMax в BuildFallbackView.");
        }
    }
}
