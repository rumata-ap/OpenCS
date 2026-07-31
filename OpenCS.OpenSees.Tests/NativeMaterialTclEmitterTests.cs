using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Tests;

public sealed class NativeMaterialTclEmitterTests
{
    private static string F(double v) => TclNumber.Format(v);

    [Fact]
    public void ToTcl_Concrete01_EmitsUniaxialCommand()
    {
        var mat = new OpenSeesMaterialDefinition { Tag = 7, Native = new Concrete01Spec(-2e7, -0.002, -5e6, -0.005) };

        string tcl = NativeMaterialTclEmitter.ToTcl(mat, F);

        Assert.Equal($"uniaxialMaterial Concrete01 7 {F(-2e7)} {F(-0.002)} {F(-5e6)} {F(-0.005)}", tcl);
    }

    [Fact]
    public void ToTcl_Steel02_EmitsUniaxialCommand()
    {
        var mat = new OpenSeesMaterialDefinition { Tag = 3, Native = new Steel02Spec(4e8, 2e11, 0.01, 18, 0.925, 0.15) };

        string tcl = NativeMaterialTclEmitter.ToTcl(mat, F);

        Assert.Equal($"uniaxialMaterial Steel02 3 {F(4e8)} {F(2e11)} {F(0.01)} {F(18)} {F(0.925)} {F(0.15)}", tcl);
    }

    [Fact]
    public void ToTcl_NullNative_EmitsElasticMultiLinearFromEnvelopes()
    {
        var mat = new OpenSeesMaterialDefinition
        {
            Tag = 9,
            PositiveEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(0.01, 1e6)],
            NegativeEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(-0.01, -1e6)]
        };

        string tcl = NativeMaterialTclEmitter.ToTcl(mat, F);

        Assert.StartsWith("uniaxialMaterial ElasticMultiLinear 9 -strain", tcl);
        Assert.Contains(F(-0.01), tcl);
        Assert.Contains(F(0.01), tcl);
    }
}
