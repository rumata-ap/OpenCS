using CScore;
using OpenCS.OpenSees.Structural;

namespace OpenCS.ViewModels;

/// <summary>Канонические компоненты концевого усилия в единицах OpenSees.</summary>
public readonly record struct FemForceEndpointValues(
    double N, double Qy, double Qz, double Mx, double My, double Mz);

/// <summary>Пара канонических усилий в начале и конце mesh-элемента.</summary>
public readonly record struct FemForceEndpointPair(
    FemForceEndpointValues Start,
    FemForceEndpointValues End);

/// <summary>Политика знаков для преобразования концевых усилий.</summary>
public sealed record FemForceEndpointSignPolicy(
    double NI, double NJ,
    double QyI, double QyJ,
    double QzI, double QzJ,
    double MxI, double MxJ,
    double MyI, double MyJ,
    double MzI, double MzJ)
{
    /// <summary>Знаки OpenSees для внутреннего усилия в узлах i/j.</summary>
    public static FemForceEndpointSignPolicy OpenSeesDefault { get; } = new(
        -1, 1, -1, 1, -1, 1, -1, 1, 1, -1, 1, -1);
}

/// <summary>Единая точка преобразования силовых результатов OpenSees.</summary>
public static class FemForceEndpointConverter
{
    /// <summary>Преобразует сырые усилия OpenSees в канонические значения на концах.</summary>
    public static FemForceEndpointPair Convert(
        FemElementEndForces source,
        FemForceEndpointSignPolicy policy)
    {
        return new(
            new FemForceEndpointValues(
                source.Ni * policy.NI,
                source.Qyi * policy.QyI,
                source.Qzi * policy.QzI,
                source.Mxi * policy.MxI,
                source.Myi * policy.MyI,
                source.Mzi * policy.MzI),
            new FemForceEndpointValues(
                source.Nj * policy.NJ,
                source.Qyj * policy.QyJ,
                source.Qzj * policy.QzJ,
                source.Mxj * policy.MxJ,
                source.Myj * policy.MyJ,
                source.Mzj * policy.MzJ));
    }

    /// <summary>Возвращает компоненту из уже преобразованного конца элемента.</summary>
    public static double ReadComponent(
        FemForceEndpointValues values,
        FemForceComponent component) => component switch
    {
        FemForceComponent.N => values.N,
        FemForceComponent.Qy => values.Qy,
        FemForceComponent.Qz => values.Qz,
        FemForceComponent.Mx => values.Mx,
        FemForceComponent.My => values.My,
        FemForceComponent.Mz => values.Mz,
        _ => 0.0
    };

    /// <summary>Создаёт строку набора усилий в кН и кН·м.</summary>
    public static LoadItem ToLoadItem(
        FemForceEndpointValues values,
        int num,
        string label) => new()
    {
        Num = num,
        Label = label,
        N = values.N / 1000.0,
        Vx = values.Qz / 1000.0,
        Vy = values.Qy / 1000.0,
        T = values.Mx / 1000.0,
        Mx = values.Mz / 1000.0,
        My = values.My / 1000.0
    };
}
