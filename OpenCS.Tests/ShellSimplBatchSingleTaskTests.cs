using CScore;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Одиночная задача по строке пакета упрощённой проверки пластин: параметры пакета и усилия строки.</summary>
public sealed class ShellSimplBatchSingleTaskTests
{
    [Fact]
    public void SingleParams_KeepsBatchSettings_AndRowForces()
    {
        var batch = new ShellSimplParams
        {
            StepDeg = 5.0, AcrcLimMm = 0.2, Phi1 = 1.4, Phi2 = 0.3, StripModel = "ndm",
            CapriCheckMode = "constraints", SigmaSCrcMethod = "moment", AutoStressToForce = true,
        };
        var row = new ShellLoadItem { Num = 3, Nx = 1, Ny = 2, Nxy = 3, Mx = 10, My = 20, Mxy = 5, SigmaX = 1000.0 };

        var p = BatchRowTaskFactory.ShellSimplSingleParams(System.Text.Json.JsonSerializer.Serialize(batch), row, 0.2);

        Assert.Equal(5.0, p.StepDeg);
        Assert.Equal(0.2, p.AcrcLimMm);
        Assert.Equal(1.4, p.Phi1);
        Assert.Equal(0.3, p.Phi2);
        Assert.Equal(StripModel.Ndm, p.Model);
        Assert.Equal(CapriCheckMode.Constraints, p.CapriMode);
        Assert.Equal(SigmaSCrcMethod.CrackingMoment8138, p.SigmaSCrc);
        // Напряжения → усилия как в пакете (σx·h), дальше усилия уже в кН/м.
        Assert.Equal(200.0, p.Nx, 9);
        Assert.Equal(0.0, p.Ny, 9);
        Assert.Equal((10.0, 20.0, 5.0), (p.Mx, p.My, p.Mxy));
        Assert.False(p.AutoStressToForce);
    }
}
