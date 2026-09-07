using System.Reflection;
using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Квалификация области применимости наклонных сечений.</summary>
public sealed class Sp63ShearApplicabilityTests
{
    [Fact]
    public void Evaluate_StandardAutoWithRectangleAndOneShear_HasNormativeVerdict()
    {
        var result = Evaluate("standard_auto", rectangle: true, manualB: null,
            equivalentConfirmed: false, orthogonalShear: 0.0, torsion: 0.0, manualPhiN: false);

        Assert.True(ReadBool(result, "HasNormativeVerdict"));
        Assert.Equal("ok", ReadString(result, "Status"));
    }

    [Fact]
    public void Evaluate_StandardAutoWithOrthogonalShear_IsNotApplicable()
    {
        var result = Evaluate("standard_auto", rectangle: true, manualB: null,
            equivalentConfirmed: false, orthogonalShear: 0.001, torsion: 0.0, manualPhiN: false);

        Assert.False(ReadBool(result, "HasNormativeVerdict"));
        Assert.Equal("not_applicable", ReadString(result, "Status"));
    }

    [Fact]
    public void Evaluate_EquivalentWithoutConfirmation_IsNotApplicable()
    {
        var result = Evaluate("standard_equivalent", rectangle: false, manualB: 0.3,
            equivalentConfirmed: false, orthogonalShear: 0.0, torsion: 0.0, manualPhiN: false);

        Assert.False(ReadBool(result, "HasNormativeVerdict"));
        Assert.Equal("not_applicable", ReadString(result, "Status"));
    }

    [Fact]
    public void Evaluate_StandardAutoWithManualWidth_IsNotApplicable()
    {
        var result = Evaluate("standard_auto", rectangle: true, manualB: 0.3,
            equivalentConfirmed: false, orthogonalShear: 0.0, torsion: 0.0, manualPhiN: false);

        Assert.False(ReadBool(result, "HasNormativeVerdict"));
        Assert.Equal("not_applicable", ReadString(result, "Status"));
    }

    static object Evaluate(string mode, bool rectangle, double? manualB,
        bool equivalentConfirmed, double orthogonalShear, double torsion, bool manualPhiN)
    {
        var assembly = typeof(ShearPlane).Assembly;
        var inputType = assembly.GetType("CScore.Sp63Shear.Sp63ShearApplicabilityInput");
        var serviceType = assembly.GetType("CScore.Sp63Shear.Sp63ShearApplicability");
        Assert.NotNull(inputType);
        Assert.NotNull(serviceType);
        var input = Activator.CreateInstance(inputType!,
            mode, rectangle, manualB, equivalentConfirmed, orthogonalShear, torsion, manualPhiN);
        var method = serviceType!.GetMethod("Evaluate", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return method!.Invoke(null, [input])!;
    }

    static bool ReadBool(object result, string property) =>
        (bool)result.GetType().GetProperty(property)!.GetValue(result)!;

    static string ReadString(object result, string property) =>
        (string)result.GetType().GetProperty(property)!.GetValue(result)!;
}
