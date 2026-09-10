using CScore.Fem;
using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Результат анализа окружения кандидата.</summary>
public sealed record EnvironmentScanResult(
    IReadOnlyList<EndAttachment> StartAttachments, IReadOnlyList<EndAttachment> EndAttachments,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Метрический анализ родительской топологии возле узлов кандидата.</summary>
public static class ChainEnvironmentScan
{
    public static EnvironmentScanResult Scan(ChainCandidate candidate,
        IReadOnlyList<EnvironmentElement> environment, ResolvedTolerances tolerances)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(environment);
        var startAttachments = new List<EndAttachment>();
        var endAttachments = new List<EndAttachment>();
        var diagnostics = new List<FemValidationDiagnostic>();
        var nodes = candidate.Chain.Nodes;
        if (nodes.Count == 0) return new(startAttachments, endAttachments, diagnostics);
        var index = new SpatialPointIndex(tolerances.NodeCoincidenceM);
        for (var i = 0; i < nodes.Count; i++) index.Add(i, nodes[i].Point);
        var toleranceSq = tolerances.NodeCoincidenceM * tolerances.NodeCoincidenceM;

        foreach (var element in environment)
        {
            if (element.Kind == EnvironmentElementKind.Shell)
            {
                ScanShell(element, nodes, tolerances, startAttachments, endAttachments, diagnostics);
                continue;
            }
            if (element.Kind != EnvironmentElementKind.Beam) continue;
            var matches = new List<(int NodeIndex, PlanarVector3 Point, double Distance)>();
            foreach (var point in element.NodePoints)
            {
                if (!point.IsFinite) continue;
                foreach (var nodeIndex in index.Neighbors(point))
                {
                    var distanceSq = (nodes[nodeIndex].Point - point).LengthSquared;
                    if (distanceSq > toleranceSq || matches.Any(item => item.NodeIndex == nodeIndex)) continue;
                    matches.Add((nodeIndex, point, Math.Sqrt(distanceSq)));
                }
            }
            if (matches.Count > 0)
            {
                if (matches.Any(match => !nodes[match.NodeIndex].IsEnd))
                {
                    diagnostics.Add(new("chain_internal_attachment",
                        $"Элемент {element.SourceKey} примыкает к внутреннему узлу цепочки — такая цепочка не извлекается как двухконцевая.",
                        true, [element.SourceKey]));
                    continue;
                }
                foreach (var match in matches.OrderBy(match => match.NodeIndex))
                {
                    var atStart = match.NodeIndex == 0;
                    (atStart ? startAttachments : endAttachments).Add(new EndAttachment(element.SourceKey,
                        element.Kind, atStart, match.Point, match.Distance));
                }
                continue;
            }
            if (LiesOnChainBody(candidate, element, tolerances, out var contactKey))
                diagnostics.Add(new("chain_dangling_contact",
                    $"Узел элемента {element.SourceKey} лежит на элементе {contactKey} цепочки, но общего узла с ним нет — усилие через этот контакт не передаётся.",
                    false, [element.SourceKey, contactKey]));
        }
        return new(startAttachments, endAttachments, diagnostics);
    }

    static bool LiesOnChainBody(ChainCandidate candidate, EnvironmentElement element,
        ResolvedTolerances tolerances, out string contactKey)
    {
        contactKey = "";
        foreach (var point in element.NodePoints)
        foreach (var segment in candidate.Chain.Segments)
        {
            if (!point.IsFinite || DistancePointToSegment(point, segment.Source.Start, segment.Source.End) > tolerances.LineDistanceM) continue;
            contactKey = segment.Source.SourceKey;
            return true;
        }
        return false;
    }

    /// <summary>Расстояние от точки до отрезка в пространстве.</summary>
    public static double DistancePointToSegment(PlanarVector3 point, PlanarVector3 a, PlanarVector3 b)
    {
        var ab = b - a;
        var lengthSq = ab.LengthSquared;
        if (lengthSq <= 0.0) return (point - a).Length;
        var t = Math.Clamp((point - a).Dot(ab) / lengthSq, 0.0, 1.0);
        return (point - (a + ab * t)).Length;
    }

    static void ScanShell(EnvironmentElement element, IReadOnlyList<ChainNode> nodes, ResolvedTolerances tolerances,
        List<EndAttachment> starts, List<EndAttachment> ends, List<FemValidationDiagnostic> diagnostics)
    {
        var warped = false; var degenerate = false;
        for (var i = 0; i < nodes.Count; i++)
        {
            switch (ShellContactGeometry.Classify(element.NodePoints, nodes[i].Point, tolerances))
            {
                case ShellContactKind.None: continue;
                case ShellContactKind.Degenerate:
                    if (!degenerate) diagnostics.Add(new("chain_shell_degenerate", $"Оболочечный элемент {element.SourceKey} вырожден — контакт с ним не анализируется.", false, [element.SourceKey]));
                    degenerate = true; return;
                case ShellContactKind.Warped:
                    if (!warped) diagnostics.Add(new("chain_shell_warped", $"Оболочечный элемент {element.SourceKey} неплоский — проверены только вершины и рёбра.", false, [element.SourceKey]));
                    warped = true; continue;
                case ShellContactKind.Vertex when nodes[i].IsEnd:
                    var start = i == 0; (start ? starts : ends).Add(new(element.SourceKey, EnvironmentElementKind.Shell, start, nodes[i].Point, 0)); continue;
                case ShellContactKind.Vertex:
                    diagnostics.Add(new("chain_internal_attachment", $"Оболочечный элемент {element.SourceKey} примыкает к внутреннему узлу цепочки.", true, [element.SourceKey])); continue;
                default:
                    diagnostics.Add(new("chain_shell_edge_contact", $"Конец цепочки попадает на ребро или внутрь грани элемента {element.SourceKey} мимо его узлов — такая постановка не поддерживается.", true, [element.SourceKey])); continue;
            }
        }
    }
}
