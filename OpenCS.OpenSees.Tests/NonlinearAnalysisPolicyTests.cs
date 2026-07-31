using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class NonlinearAnalysisPolicyTests
{
    [Fact]
    public void Validate_AcceptsDefaults() => new NonlinearAnalysisPolicy().Validate();

    [Fact]
    public void Validate_RejectsNonPositiveTolerance()
    {
        var policy = new NonlinearAnalysisPolicy { Tolerance = 0 };
        var ex = Assert.Throws<InvalidOperationException>(policy.Validate);
        Assert.Contains("Допуск", ex.Message);
    }

    [Fact]
    public void Validate_RejectsUnknownConvergenceTest()
    {
        var policy = new NonlinearAnalysisPolicy { ConvergenceTest = "Bogus" };
        var ex = Assert.Throws<InvalidOperationException>(policy.Validate);
        Assert.Contains("критерий сходимости", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsUnknownAlgorithm()
    {
        var policy = new NonlinearAnalysisPolicy { Algorithm = "Bogus" };
        var ex = Assert.Throws<InvalidOperationException>(policy.Validate);
        Assert.Contains("алгоритм", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsNonPositiveRefinementDivisions()
    {
        var policy = new NonlinearAnalysisPolicy { RefinementDivisions = 0 };
        Assert.Throws<InvalidOperationException>(policy.Validate);
    }

    [Fact]
    public void Validate_RejectsNonPositiveMaxRefinementDepth()
    {
        var policy = new NonlinearAnalysisPolicy { MaxRefinementDepth = 0 };
        Assert.Throws<InvalidOperationException>(policy.Validate);
    }

    [Fact]
    public void Validate_RejectsNonPositiveMaxIterations()
    {
        var policy = new NonlinearAnalysisPolicy { MaxIterations = 0 };
        Assert.Throws<InvalidOperationException>(policy.Validate);
    }
}
