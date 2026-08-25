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

    [Fact]
    public void StiffnessRatio_UsesNoOffsetWhenFirstPointIsNotAtZeroCurvature()
    {
        // Результат без точки κ=0 в начале (например, ветвь «предел раньше трещины»):
        // вычитать нечего, поведение прежнее — отношение полного момента к кривизне.
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

        Assert.Equal(new[] { 0.8 }, viewModel.MxStiffnessRatio);
    }
}
