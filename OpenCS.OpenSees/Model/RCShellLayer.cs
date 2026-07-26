namespace OpenCS.OpenSees.Model;

/// <summary>Назначение слоя в слоистом shell-сечении.</summary>
public enum ShellLayerKind
{
    /// <summary>Слой бетона.</summary>
    Concrete,

    /// <summary>Арматура, направленная вдоль локальной оси x.</summary>
    RebarX,

    /// <summary>Арматура, направленная вдоль локальной оси y.</summary>
    RebarY
}

/// <summary>Результат mapping одного физического слоя shell-сечения.</summary>
public sealed record RCShellLayer(
    int Index,
    ShellLayerKind Kind,
    double CenterZ,
    double Thickness,
    int MaterialTag,
    double DirectionDegrees,
    string SourceId);
