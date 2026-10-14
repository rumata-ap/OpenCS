using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;

namespace OpenCS.Tasks;

/// <summary>Строго конвертирует FemAnalysisPathControl (UI/JSON DTO, NodeId) в
/// FemPathControlInput (resolve-слой). Не допускает молчаливых фолбэков — бросает
/// NotSupportedException на неизвестный Mode, отсутствующие обязательные поля для
/// выбранного режима, ContinueWith без эффективного LoadControl. Диалог постановки по
/// построению не создаёт неполный DTO — эта проверка защищает от рассинхронизации
/// хранимого JSON (ручное редактирование БД, будущие миграции), не является основным
/// UX-гейтом.</summary>
public static class FemAnalysisPathControlMapper
{
    public static FemPathControlInput Resolve(FemAnalysisPathControl? dto, FemAnalysisPathControl? continueWith, string stageTag)
    {
        if (dto == null)
        {
            if (continueWith != null)
                throw new NotSupportedException($"Стадия «{stageTag}»: продолжение (continuation) задано без явного LoadControl.");
            return new FemPathControlInput();
        }

        var mode = ParseMode(dto.Mode, stageTag);
        FemDisplacementControlInput? dc = null;
        FemArcLengthInput? al = null;
        switch (mode)
        {
            case FemPathControlMode.DisplacementControl:
                dc = ResolveDisplacementControl(dto, stageTag);
                break;
            case FemPathControlMode.ArcLength:
                al = ResolveArcLength(dto, stageTag);
                break;
        }

        FemPathControlMode? continueMode = null;
        FemDisplacementControlInput? cdc = null;
        FemArcLengthInput? cal = null;
        if (continueWith != null)
        {
            if (mode != FemPathControlMode.LoadControl)
                throw new NotSupportedException($"Стадия «{stageTag}»: продолжение задано не для LoadControl-стадии.");
            continueMode = ParseMode(continueWith.Mode, stageTag);
            switch (continueMode)
            {
                case FemPathControlMode.DisplacementControl:
                    cdc = ResolveDisplacementControl(continueWith, stageTag);
                    break;
                case FemPathControlMode.ArcLength:
                    cal = ResolveArcLength(continueWith, stageTag);
                    break;
                default:
                    throw new NotSupportedException($"Стадия «{stageTag}»: продолжение LoadControl→LoadControl не имеет смысла.");
            }
        }

        return new FemPathControlInput(mode, dc, al, continueMode, cdc, cal);
    }

    static FemPathControlMode ParseMode(string mode, string stageTag) => mode switch
    {
        "LoadControl" => FemPathControlMode.LoadControl,
        "DisplacementControl" => FemPathControlMode.DisplacementControl,
        "ArcLength" => FemPathControlMode.ArcLength,
        _ => throw new NotSupportedException($"Стадия «{stageTag}»: неизвестный режим управления траекторией «{mode}».")
    };

    static FemDisplacementControlInput ResolveDisplacementControl(FemAnalysisPathControl dto, string stageTag)
    {
        if (dto.ControlNodeId is not { } nodeId || dto.ControlDof is not { } dof ||
            dto.InitialIncrement is not { } init || dto.MinIncrement is not { } min ||
            dto.MaxIncrement is not { } max || dto.TargetDisplacement is not { } target ||
            dto.MaxSteps is not { } maxSteps)
            throw new NotSupportedException($"Стадия «{stageTag}»: DisplacementControl требует ControlNodeId, ControlDof, InitialIncrement, MinIncrement, MaxIncrement, TargetDisplacement и MaxSteps.");
        return new FemDisplacementControlInput(nodeId, dof, init, min, max, target, maxSteps);
    }

    static FemArcLengthInput ResolveArcLength(FemAnalysisPathControl dto, string stageTag)
    {
        if (dto.ArcLengthS is not { } s || dto.ArcLengthAlpha is not { } alpha ||
            dto.ArcLengthMinS is not { } minS || dto.MaxSteps is not { } maxSteps ||
            dto.MonitorNodeId is not { } nodeId || dto.MonitorDof is not { } dof)
            throw new NotSupportedException($"Стадия «{stageTag}»: ArcLength требует ArcLengthS, ArcLengthAlpha, ArcLengthMinS, MaxSteps, MonitorNodeId и MonitorDof.");
        return new FemArcLengthInput(s, alpha, minS, maxSteps, nodeId, dof);
    }
}
