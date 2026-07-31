using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class DrillingPolicyTests
{
    [Fact]
    public void Validate_AcceptsDefaultNone() => new DrillingPolicy().Validate();

    [Fact]
    public void Validate_AcceptsStabilizationWithPositiveValue() =>
        new DrillingPolicy { Mode = ShellDrillingMode.Stabilization, StabilizationValue = 0.001 }.Validate();

    [Fact]
    public void Validate_AcceptsNonlinearDrillingWithoutValue() =>
        new DrillingPolicy { Mode = ShellDrillingMode.NonlinearDrilling }.Validate();

    [Fact]
    public void Validate_RejectsStabilizationWithoutValue()
    {
        var policy = new DrillingPolicy { Mode = ShellDrillingMode.Stabilization };
        var ex = Assert.Throws<InvalidOperationException>(policy.Validate);
        Assert.Contains("Stabilization", ex.Message);
    }

    [Fact]
    public void Validate_RejectsStabilizationWithNonPositiveValue()
    {
        var policy = new DrillingPolicy { Mode = ShellDrillingMode.Stabilization, StabilizationValue = 0 };
        Assert.Throws<InvalidOperationException>(policy.Validate);
    }

    [Fact]
    public void Validate_RejectsStabilizationValueSetWithoutStabilizationMode()
    {
        var policy = new DrillingPolicy { Mode = ShellDrillingMode.None, StabilizationValue = 0.001 };
        var ex = Assert.Throws<InvalidOperationException>(policy.Validate);
        Assert.Contains("StabilizationValue", ex.Message);
    }
}
