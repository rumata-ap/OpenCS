using System;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Секущая жёсткость серий «момент — коэффициент жёсткости». При N≠0 и несимметричном
/// армировании кривая M(κ) не проходит через начало координат, поэтому секущая должна
/// отсчитываться от точки κ=0, а не от нуля.
/// </summary>
public sealed class MomentCurvatureStiffnessSecantTests
{
    /// <summary>
    /// Точка κ=0 несёт Mx₀ = 2, дальше сечение отвечает ровно с жёсткостью B0x = 100:
    /// Mx = 2 − 100·|Ky|. Коэффициент обязан быть 1.0 во всех точках — именно это ломалось,
    /// когда числителем брали полный Mx.
    /// </summary>
    const string LinearResponseJson = """
    {
      "has_mx": true,
      "has_my": true,
      "b0x": 100.0,
      "b0y": 50.0,
      "points": [
        {"mx":  2.0, "my": 0.0, "ky":  0.00, "kz": 0.00, "segment": 1, "converged": true},
        {"mx": -8.0, "my": 5.0, "ky": -0.10, "kz": 0.10, "segment": 1, "converged": true},
        {"mx":-18.0, "my":10.0, "ky": -0.20, "kz": 0.20, "segment": 1, "converged": true},
        {"mx":-48.0, "my":25.0, "ky": -0.50, "kz": 0.50, "segment": 3, "converged": true}
      ]
    }
    """;

    [Fact]
    public void MxStiffnessRatio_IsMeasuredFromTheZeroCurvaturePoint()
    {
        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(
            new CScore.CalcResult { Status = "ok", DataJson = LinearResponseJson });

        Assert.Equal(3, viewModel.MxStiffnessRatio.Length);
        Assert.All(viewModel.MxStiffnessRatio, r => Assert.Equal(1.0, r, 1e-9));
    }

    [Fact]
    public void MyStiffnessRatio_IsMeasuredFromTheZeroCurvaturePoint()
    {
        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(
            new CScore.CalcResult { Status = "ok", DataJson = LinearResponseJson });

        // My₀ = 0 (сечение симметрично по X) — поправка нулевая, но проверяется тот же путь.
        Assert.Equal(3, viewModel.MyStiffnessRatio.Length);
        Assert.All(viewModel.MyStiffnessRatio, r => Assert.Equal(1.0, r, 1e-9));
    }

    /// <summary>
    /// База секущей — ПЕРВАЯ точка кривой, а не точка κ=0. У преднапряжённого сечения начало
    /// кривой лежит в состоянии без внешнего момента при НЕнулевой кривизне (выгиб от обжатия),
    /// поэтому признак «κ≈0» как маркер базовой точки не работает. Приращение берётся по обеим
    /// осям: (−20−(−10))/(−0,25−(−0,10)) = 66,67 при B0x = 100.
    /// </summary>
    [Fact]
    public void StiffnessRatio_MeasuresIncrementFromTheFirstCurvePoint()
    {
        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(
            new CScore.CalcResult
            {
                Status = "ok",
                DataJson = """
                {
                  "has_mx": true,
                  "has_my": false,
                  "b0x": 100.0,
                  "points": [
                    {"mx": -10.0, "my": 0.0, "ky": -0.10, "kz": 0.0, "segment": 1, "converged": true},
                    {"mx": -20.0, "my": 0.0, "ky": -0.25, "kz": 0.0, "segment": 1, "converged": true}
                  ]
                }
                """
            });

        Assert.Single(viewModel.MxStiffnessRatio);
        Assert.Equal(2.0 / 3.0, viewModel.MxStiffnessRatio[0], 1e-9);
    }

    /// <summary>
    /// Первая точка не сошлась — базы нет, вычитать нечего: отношение считается от нуля, как
    /// и раньше. (−20/−0,25)/100 = 0,8.
    /// </summary>
    [Fact]
    public void StiffnessRatio_FallsBackToZeroBaseWhenFirstPointDidNotConverge()
    {
        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(
            new CScore.CalcResult
            {
                Status = "partial",
                DataJson = """
                {
                  "has_mx": true,
                  "has_my": false,
                  "b0x": 100.0,
                  "points": [
                    {"mx": -10.0, "my": 0.0, "ky": -0.10, "kz": 0.0, "segment": 1, "converged": false},
                    {"mx": -20.0, "my": 0.0, "ky": -0.25, "kz": 0.0, "segment": 1, "converged": true}
                  ]
                }
                """
            });

        Assert.Equal(new[] { 0.8 }, viewModel.MxStiffnessRatio);
    }

    [Fact]
    public void PointRows_ExposeTheSameStiffnessRatiosAsPlots()
    {
        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(
            new CScore.CalcResult
            {
                Status = "ok",
                DataJson = """
                {
                  "has_mx": true,
                  "has_my": true,
                  "ea0": 100.0,
                  "b0x": 200.0,
                  "b0y": 400.0,
                  "points": [
                    {"n": 0.0, "mx": 1.0, "my": 2.0, "e0": 0.0, "ky": 0.0, "kz": 0.0, "converged": true},
                    {"n": -10.0, "mx": -19.0, "my": -8.0, "e0": 0.1, "ky": 0.1, "kz": 0.05, "converged": true}
                  ]
                }
                """
            });

        var zero = viewModel.Rows[0];
        var point = viewModel.Rows[1];

        Assert.Null(zero.NStiffnessRatio);
        Assert.Null(zero.MxStiffnessRatio);
        Assert.Null(zero.MyStiffnessRatio);
        Assert.Equal(1.0, point.NStiffnessRatio!.Value, 1e-9);
        Assert.Equal(1.0, point.MxStiffnessRatio!.Value, 1e-9);
        Assert.Equal(0.5, point.MyStiffnessRatio!.Value, 1e-9);
        Assert.Equal(new[] { 1.0 }, viewModel.NStiffnessRatio);
        Assert.Equal(new[] { 1.0 }, viewModel.MxStiffnessRatio);
        Assert.Equal(new[] { 0.5 }, viewModel.MyStiffnessRatio);
    }

    [Fact]
    public void PointRows_LeaveRatiosEmptyWhenBaseOrPointIsUndefined()
    {
        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(
            new CScore.CalcResult
            {
                Status = "ok",
                DataJson = """
                {
                  "has_mx": true,
                  "has_my": true,
                  "ea0": 0.0,
                  "b0x": 0.0,
                  "b0y": 0.0,
                  "points": [
                    {"n": 0.0, "mx": 1.0, "my": 2.0, "e0": 0.0, "ky": 0.0, "kz": 0.0, "converged": true},
                    {"n": 10.0, "mx": 21.0, "my": 12.0, "e0": 0.1, "ky": 0.1, "kz": 0.05, "converged": true},
                    {"n": 20.0, "mx": 41.0, "my": 22.0, "e0": 0.2, "ky": 0.2, "kz": 0.1, "converged": false}
                  ]
                }
                """
            });

        Assert.All(viewModel.Rows, row =>
        {
            Assert.Null(row.NStiffnessRatio);
            Assert.Null(row.MxStiffnessRatio);
            Assert.Null(row.MyStiffnessRatio);
        });
        Assert.Empty(viewModel.NStiffnessRatio);
        Assert.Empty(viewModel.MxStiffnessRatio);
        Assert.Empty(viewModel.MyStiffnessRatio);
    }
}
