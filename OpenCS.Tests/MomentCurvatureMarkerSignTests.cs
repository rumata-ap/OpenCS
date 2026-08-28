using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Маркеры точек и контрольных точек обязаны лежать НА линии кривой. Линия строится знаковой
/// проекцией (один множитель на серию, взятый из предельной точки), поэтому и маркеры должны
/// строиться ею же. Пока они брались по <c>Math.Abs</c>, часть кривой, где кривизна меняет
/// знак (преднапряжённое сечение, участок до трещинообразования), отражалась в другой квадрант
/// и висела на графике отдельной «гроздью» точек рядом с кривой.
/// </summary>
public sealed class MomentCurvatureMarkerSignTests
{
    /// <summary>
    /// Модель преднапряжённого сечения: кривая стартует в состоянии без внешнего момента с
    /// ПОЛОЖИТЕЛЬНОЙ кривизной (выгиб от обжатия), проходит через ноль кривизны и уходит в
    /// отрицательную область к пределу. Знаки серии: Mx и Ky в предельной точке отрицательны,
    /// значит вся серия домножается на −1.
    /// </summary>
    const string SignChangeJson = """
    {
      "has_mx": true,
      "has_my": false,
      "points": [
        {"mx":    0.0, "my": 0.0, "ky":  0.0016, "kz": 0.0, "segment": 1, "converged": true},
        {"mx":  -50.0, "my": 0.0, "ky":  0.0005, "kz": 0.0, "segment": 1, "converged": true},
        {"mx":  -96.0, "my": 0.0, "ky": -0.0011, "kz": 0.0, "segment": 1, "converged": true},
        {"mx": -300.0, "my": 0.0, "ky": -0.0090, "kz": 0.0, "segment": 4, "converged": true}
      ],
      "cracking": {"mx": -96.0, "my": 0.0, "ky": -0.0011, "kz": 0.0, "converged": true}
    }
    """;

    [Fact]
    public void PlotPoint_MatchesTheCurveSeries_WhenCurvatureChangesSign()
    {
        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(
            new CScore.CalcResult { Status = "ok", DataJson = SignChangeJson });

        var series = Assert.Single(viewModel.CurvatureYSeriesParts);

        for (int i = 0; i < viewModel.Rows.Count; i++)
        {
            var (x, y) = viewModel.PlotPoint(viewModel.Rows[i], useMx: true);
            Assert.Equal(series.X[i], x, 12);
            Assert.Equal(series.Y[i], y, 12);
        }
    }

    /// <summary>
    /// Точка с положительной кривизной обязана оказаться в отрицательной части оси — именно её
    /// <c>Math.Abs</c> отражал на другую сторону, создавая вторую «ветвь» маркеров.
    /// </summary>
    [Fact]
    public void PlotPoint_KeepsTheStartOfTheCurveOnTheNegativeSide()
    {
        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(
            new CScore.CalcResult { Status = "ok", DataJson = SignChangeJson });

        var (x, y) = viewModel.PlotPoint(viewModel.Rows[0], useMx: true);

        Assert.Equal(-0.0016, x, 12);
        Assert.Equal(0.0, y, 12);
    }

    /// <summary>Контрольные маркеры (трещинообразование/текучесть/предел) — та же система координат.</summary>
    [Fact]
    public void PlotPoint_PutsControlMarkerOnTheCurve()
    {
        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(
            new CScore.CalcResult { Status = "ok", DataJson = SignChangeJson });

        Assert.NotNull(viewModel.Cracking);
        var (x, y) = viewModel.PlotPoint(viewModel.Cracking!, useMx: true);

        Assert.Equal(0.0011, x, 12);
        Assert.Equal(96.0, y, 12);
    }
}
