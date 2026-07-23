namespace OpenCS.OpenSees.Structural;

/// <summary>Распределённая нагрузка одного OpenSees-элемента в его локальных осях.</summary>
public sealed record FemLinearDistributedLoad(
    int ElementTag,
    double WyStart, double WzStart, double WxStart,
    double WyEnd, double WzEnd, double WxEnd,
    double AOverL, double BOverL);
