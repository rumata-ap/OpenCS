using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Отрезок в направлении упорядоченной цепочки.</summary>
public sealed record OrderedBeamSegment(
    BeamSegmentInput Source, bool IsReversed, double StartStation, double EndStation,
    double LengthM, double AngleToAxisDeg);

/// <summary>Кластерный узел упорядоченной цепочки.</summary>
public sealed record ChainNode(
    PlanarVector3 Point, double Station, bool IsEnd, IReadOnlyList<string> SourceKeys,
    double ClusterDiameterM);

/// <summary>Примыкание внешнего объекта на конце цепочки.</summary>
public sealed record EndAttachment(
    string SourceKey, EnvironmentElementKind Kind, bool AtStart, PlanarVector3 ContactPoint,
    double DistanceM);

/// <summary>Принятая цепочка, упорядоченная от начала к концу.</summary>
public sealed record StraightBeamChain(
    IReadOnlyList<OrderedBeamSegment> Segments, IReadOnlyList<ChainNode> Nodes,
    PlanarVector3 AxisOrigin, PlanarVector3 AxisDirection, double LengthM,
    IReadOnlyList<EndAttachment> StartAttachments, IReadOnlyList<EndAttachment> EndAttachments);
