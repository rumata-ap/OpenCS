using System.Text.Json.Serialization;
using CScore.Planar;

namespace CScore.Submodel;

/// <summary>
/// Шесть DOF узла: X, Y, Z — поступательные (силы Н или перемещения м), Rx, Ry, Rz — вращательные
/// (моменты Н·м или повороты рад). Индекс 0..5 совпадает с битами <c>FemNode.DofMask</c> и с
/// <c>FemKinematicLoad.Dof − 1</c>; поэтому это отдельный тип, а не пара <see cref="PlanarVector3"/>.
/// </summary>
public sealed record Dof6(double X, double Y, double Z, double Rx, double Ry, double Rz)
{
    public static readonly Dof6 Zero = new(0, 0, 0, 0, 0, 0);

    /// <summary>Сборка из силовой (поступательной) и моментной (вращательной) частей.</summary>
    public static Dof6 FromParts(PlanarVector3 force, PlanarVector3 moment) =>
        new(force.X, force.Y, force.Z, moment.X, moment.Y, moment.Z);

    [JsonIgnore] public PlanarVector3 Force => new(X, Y, Z);
    [JsonIgnore] public PlanarVector3 Moment => new(Rx, Ry, Rz);

    [JsonIgnore]
    public double this[int dof] => dof switch
    {
        0 => X, 1 => Y, 2 => Z, 3 => Rx, 4 => Ry, 5 => Rz,
        _ => throw new ArgumentOutOfRangeException(nameof(dof), dof, "DOF должен быть 0..5.")
    };

    [JsonIgnore] public double MaxForceAbs => Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));
    [JsonIgnore] public double MaxMomentAbs => Math.Max(Math.Abs(Rx), Math.Max(Math.Abs(Ry), Math.Abs(Rz)));

    public Dof6 Scale(double factor) => new(X * factor, Y * factor, Z * factor, Rx * factor, Ry * factor, Rz * factor);

    public static Dof6 operator +(Dof6 a, Dof6 b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.Rx + b.Rx, a.Ry + b.Ry, a.Rz + b.Rz);
    public static Dof6 operator -(Dof6 a, Dof6 b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.Rx - b.Rx, a.Ry - b.Ry, a.Rz - b.Rz);
    public static Dof6 operator -(Dof6 a) => new(-a.X, -a.Y, -a.Z, -a.Rx, -a.Ry, -a.Rz);
}

/// <summary>
/// Концевые усилия стержня в его локальных осях: <c>N, Qy, Qz, Mx, My, Mz</c> как X..Rz
/// в <see cref="Dof6"/> для концов i и j. Конвенция OpenSees <c>eleResponse localForce</c>:
/// вектор сопротивления элемента — силы, с которыми узлы действуют на элемент; в сборке
/// <c>Σ_e p_e = P + R</c> в каждом узле.
/// </summary>
public sealed record BeamEndForces(Dof6 I, Dof6 J);

/// <summary>Какие данные есть в родительском результате. Отсутствие — не ноль.</summary>
public sealed record ParentResultCapabilities(
    bool IsLinear, bool HasDisplacements, bool HasReactions, bool HasEndForces);

/// <summary>
/// Source-independent линейный результат родительской схемы. Теги — канонические строковые
/// <c>FemMeshNode.NodeTag</c>/<c>FemElement.ElemTag</c>; все величины в глобальной системе,
/// кроме концевых усилий (локальные оси элемента).
/// </summary>
public interface IParentLinearResult
{
    ParentResultCapabilities Capabilities { get; }

    /// <summary>Перемещения и повороты узла (м, рад).</summary>
    bool TryGetDisplacement(string meshNodeTag, out Dof6 value);

    /// <summary>Реакция закреплённого узла (Н, Н·м).</summary>
    bool TryGetReaction(string meshNodeTag, out Dof6 value);

    /// <summary>Концевые усилия стержня в конвенции <see cref="BeamEndForces"/>.</summary>
    bool TryGetEndForces(string meshElementTag, out BeamEndForces value);
}

/// <summary>Словарная реализация <see cref="IParentLinearResult"/> (адаптеры, тесты).</summary>
public sealed class DictionaryParentLinearResult : IParentLinearResult
{
    readonly IReadOnlyDictionary<string, Dof6> _displacements;
    readonly IReadOnlyDictionary<string, Dof6> _reactions;
    readonly IReadOnlyDictionary<string, BeamEndForces> _endForces;

    public DictionaryParentLinearResult(bool isLinear,
        IReadOnlyDictionary<string, Dof6> displacements,
        IReadOnlyDictionary<string, Dof6> reactions,
        IReadOnlyDictionary<string, BeamEndForces> endForces)
    {
        _displacements = displacements;
        _reactions = reactions;
        _endForces = endForces;
        Capabilities = new ParentResultCapabilities(isLinear,
            displacements.Count > 0, reactions.Count > 0, endForces.Count > 0);
    }

    public ParentResultCapabilities Capabilities { get; }

    public bool TryGetDisplacement(string meshNodeTag, out Dof6 value) =>
        TryGet(_displacements, meshNodeTag, out value);

    public bool TryGetReaction(string meshNodeTag, out Dof6 value) =>
        TryGet(_reactions, meshNodeTag, out value);

    public bool TryGetEndForces(string meshElementTag, out BeamEndForces value)
    {
        if (_endForces.TryGetValue(meshElementTag, out var found)) { value = found; return true; }
        value = null!;
        return false;
    }

    static bool TryGet(IReadOnlyDictionary<string, Dof6> source, string tag, out Dof6 value)
    {
        if (source.TryGetValue(tag, out var found)) { value = found; return true; }
        value = null!;
        return false;
    }
}
