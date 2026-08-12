using OpenCS.OpenSees.Structural;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

public class FemAnalysisPathControlMapperTests
{
    [Fact]
    public void Resolve_NullDto_ReturnsLoadControlDefault()
    {
        var result = FemAnalysisPathControlMapper.Resolve(null, null, "Стадия 1");
        Assert.Equal(FemPathControlMode.LoadControl, result.Mode);
        Assert.Null(result.ContinueWithMode);
    }

    [Fact]
    public void Resolve_UnknownMode_Throws()
    {
        var dto = new FemAnalysisPathControl { Mode = "Nonsense" };
        var ex = Assert.Throws<NotSupportedException>(() => FemAnalysisPathControlMapper.Resolve(dto, null, "Стадия 1"));
        Assert.Contains("Стадия 1", ex.Message);
    }

    [Fact]
    public void Resolve_DisplacementControlMissingFields_Throws()
    {
        var dto = new FemAnalysisPathControl { Mode = "DisplacementControl", ControlNodeId = 2 };
        Assert.Throws<NotSupportedException>(() => FemAnalysisPathControlMapper.Resolve(dto, null, "Стадия 1"));
    }

    [Fact]
    public void Resolve_DisplacementControlComplete_ReturnsInput()
    {
        var dto = new FemAnalysisPathControl
        {
            Mode = "DisplacementControl", ControlNodeId = 2, ControlDof = 3,
            InitialIncrement = 0.001, MinIncrement = 0.0001, MaxIncrement = 0.01,
            TargetDisplacement = -0.05, MaxSteps = 200
        };
        var result = FemAnalysisPathControlMapper.Resolve(dto, null, "Стадия 1");
        Assert.Equal(FemPathControlMode.DisplacementControl, result.Mode);
        Assert.Equal(2, result.DisplacementControl!.ControlNodeId);
        Assert.Equal(-0.05, result.DisplacementControl.TargetDisplacement);
    }

    [Fact]
    public void Resolve_ArcLengthComplete_ReturnsInput()
    {
        var dto = new FemAnalysisPathControl
        {
            Mode = "ArcLength", ArcLengthS = 0.01, ArcLengthAlpha = 1.0, ArcLengthMinS = 0.001,
            MaxSteps = 100, MonitorNodeId = 2, MonitorDof = 3
        };
        var result = FemAnalysisPathControlMapper.Resolve(dto, null, "Стадия 1");
        Assert.Equal(FemPathControlMode.ArcLength, result.Mode);
        Assert.Equal(2, result.ArcLength!.MonitorNodeId);
    }

    [Fact]
    public void Resolve_ContinueWithOnNonLoadControlMode_Throws()
    {
        var dto = new FemAnalysisPathControl
        {
            Mode = "DisplacementControl", ControlNodeId = 2, ControlDof = 3,
            InitialIncrement = 0.001, MinIncrement = 0.0001, MaxIncrement = 0.01,
            TargetDisplacement = -0.05, MaxSteps = 200
        };
        var continueWith = new FemAnalysisPathControl { Mode = "ArcLength" };
        Assert.Throws<NotSupportedException>(() => FemAnalysisPathControlMapper.Resolve(dto, continueWith, "Стадия 1"));
    }

    [Fact]
    public void Resolve_ContinueWithOnLoadControl_ReturnsPopulated()
    {
        // dto == null означает legacy-JSON (сохранённый до появления фичи) — continuation в
        // таком стиле недопустим по контракту (Resolve_NullDtoWithContinueWith_Throws ниже);
        // LoadControl-стадия с continuation, как её реально сохраняет диалог постановки,
        // всегда приходит с явным dto.Mode == "LoadControl", не с dto == null.
        var loadControlDto = new FemAnalysisPathControl { Mode = "LoadControl" };
        var continueWith = new FemAnalysisPathControl
        {
            Mode = "DisplacementControl", ControlNodeId = 4, ControlDof = 3,
            InitialIncrement = 0.001, MinIncrement = 0.0001, MaxIncrement = 0.01,
            TargetDisplacement = -0.05, MaxSteps = 200
        };
        var result = FemAnalysisPathControlMapper.Resolve(loadControlDto, continueWith, "Стадия 1");
        Assert.Equal(FemPathControlMode.LoadControl, result.Mode);
        Assert.Equal(FemPathControlMode.DisplacementControl, result.ContinueWithMode);
        Assert.Equal(4, result.ContinueWithDisplacementControl!.ControlNodeId);
    }

    [Fact]
    public void Resolve_NullDtoWithContinueWith_Throws()
    {
        // dto == null — legacy-JSON путь; continuation без явного dto.Mode=="LoadControl" не
        // может возникнуть из реального UI (диалог всегда сохраняет явный DTO), поэтому
        // трактуется как рассинхронизация JSON.
        var continueWith = new FemAnalysisPathControl { Mode = "DisplacementControl" };
        Assert.Throws<NotSupportedException>(() => FemAnalysisPathControlMapper.Resolve(null, continueWith, "Стадия 1"));
    }
}
