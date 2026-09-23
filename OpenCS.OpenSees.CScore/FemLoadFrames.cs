using System.Globalization;
using System.Text.Json;
using CScore.Fem;
using CScore.Planar;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Кадры FemLocalAxis для кусков нагрузок, полученных от <see cref="FemMemberLoadSegmenter"/>.</summary>
internal sealed class FemLoadFrames
{
    readonly Dictionary<string, FemMember> _memberByTag = new(StringComparer.Ordinal);
    readonly Dictionary<string, FemNode> _sourceNodeByTag;
    readonly Dictionary<string, FemMeshNode> _meshNodeByTag = new(StringComparer.Ordinal);
    readonly Dictionary<string, FemElement> _elementByTag = new(StringComparer.Ordinal);

    public FemLoadFrames(IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemNode> sourceNodes, IReadOnlyList<FemMember> sourceMembers)
    {
        foreach (var member in sourceMembers)
            _memberByTag.TryAdd(member.ElemTag, member);
        _sourceNodeByTag = sourceNodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeTag))
            .ToDictionary(node => node.NodeTag, StringComparer.Ordinal);
        foreach (var node in meshNodes)
            if (FemMeshTopology.CanonicalNodeTag(node.NodeTag) is string tag)
                _meshNodeByTag[tag] = node;
        foreach (var element in meshElements)
            _elementByTag.TryAdd(element.ElemTag, element);
    }

    /// <summary>Кадр исходного конструктивного стержня (сегментатор уже проверил его узлы и длину).</summary>
    public Frame MemberFrame(string memberTag)
    {
        var member = _memberByTag[memberTag];
        int[] ends = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson)!;
        var i = _sourceNodeByTag[ends[0].ToString(CultureInfo.InvariantCulture)];
        var j = _sourceNodeByTag[ends[1].ToString(CultureInfo.InvariantCulture)];
        return Build(i.X, i.Y, i.Z, j.X, j.Y, j.Z, member.RotationDeg);
    }

    /// <summary>Кадр mesh-элемента с углом исходного стержня.</summary>
    public Frame ElementFrame(string elementTag, string memberTag)
    {
        var tags = FemMeshTopology.ReadNodeTags(_elementByTag[elementTag], 2)!;
        var i = _meshNodeByTag[tags[0]];
        var j = _meshNodeByTag[tags[1]];
        return Build(i.X, i.Y, i.Z, j.X, j.Y, j.Z, _memberByTag[memberTag].RotationDeg);
    }

    public static PlanarVector3 ToGlobal(PlanarVector3 value, string coordinateSystem, Frame sourceFrame) =>
        coordinateSystem.Equals("local", StringComparison.OrdinalIgnoreCase)
            ? sourceFrame.X * value.X + sourceFrame.Y * value.Y + sourceFrame.Z * value.Z
            : value;

    public static PlanarVector3 ToElementLocal(PlanarVector3 value, string coordinateSystem,
        Frame sourceFrame, Frame elementFrame)
    {
        var global = ToGlobal(value, coordinateSystem, sourceFrame);
        return new PlanarVector3(global.Dot(elementFrame.X), global.Dot(elementFrame.Y), global.Dot(elementFrame.Z));
    }

    public static int ParseElementTag(string tag, string memberTag)
    {
        if (int.TryParse(tag, out int value)) return value;
        throw new InvalidOperationException($"Элемент сетки '{tag}' стержня '{memberTag}' имеет нечисловой тег.");
    }

    static Frame Build(double xi, double yi, double zi, double xj, double yj, double zj, double rotationDeg)
    {
        var (x, y, z) = FemLocalAxis.LocalFrame(
            new FemLinearNode(0, xi, yi, zi, new bool[6]), new FemLinearNode(0, xj, yj, zj, new bool[6]), rotationDeg);
        return new Frame(new PlanarVector3(x.X, x.Y, x.Z), new PlanarVector3(y.X, y.Y, y.Z),
            new PlanarVector3(z.X, z.Y, z.Z));
    }

    /// <summary>Ортонормированный кадр X/Y/Z в глобальной системе.</summary>
    public readonly record struct Frame(PlanarVector3 X, PlanarVector3 Y, PlanarVector3 Z);
}
