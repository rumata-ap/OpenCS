namespace CScore.Submodel;

/// <summary>Разрешённые допуски анализа и характерный размер, из которого они получены.</summary>
public sealed record ResolvedTolerances(
    double NodeCoincidenceM,
    double LineDistanceM,
    double AngularDeg,
    double GapM,
    double OverlapM,
    double CharacteristicSizeM);

/// <summary>Абсолютные и относительные допуски геометрического анализа цепочки.</summary>
public sealed record ChainTolerances(
    double NodeCoincidenceAbsM, double NodeCoincidenceRel,
    double LineDistanceAbsM, double LineDistanceRel,
    double AngularDeg,
    double GapAbsM, double GapRel,
    double OverlapAbsM, double OverlapRel)
{
    public static ChainTolerances Default { get; } = new(
        NodeCoincidenceAbsM: 0.001, NodeCoincidenceRel: 1e-4,
        LineDistanceAbsM: 0.002, LineDistanceRel: 2e-4,
        AngularDeg: 0.5,
        GapAbsM: 0.010, GapRel: 1e-3,
        OverlapAbsM: 0.001, OverlapRel: 1e-4);

    public ResolvedTolerances Resolve(double characteristicSizeM)
    {
        if (!double.IsFinite(characteristicSizeM) || characteristicSizeM < 0.0)
            throw new ArgumentOutOfRangeException(nameof(characteristicSizeM),
                "Характерный размер должен быть конечным неотрицательным числом.");

        return new ResolvedTolerances(
            Combine(NodeCoincidenceAbsM, NodeCoincidenceRel, characteristicSizeM),
            Combine(LineDistanceAbsM, LineDistanceRel, characteristicSizeM),
            NonNegative(AngularDeg, nameof(AngularDeg)),
            Combine(GapAbsM, GapRel, characteristicSizeM),
            Combine(OverlapAbsM, OverlapRel, characteristicSizeM),
            characteristicSizeM);
    }

    static double Combine(double absolutePart, double relativePart, double size) =>
        Math.Max(NonNegative(absolutePart, nameof(absolutePart)),
            NonNegative(relativePart, nameof(relativePart)) * size);

    static double NonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(name, "Допуск должен быть конечным неотрицательным числом.");
        return value;
    }
}
