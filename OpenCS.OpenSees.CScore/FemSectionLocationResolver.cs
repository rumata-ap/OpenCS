using System.Text.Json;
using CScore.Fem;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Строка выбора точки интегрирования с положением вдоль исходного стержня.</summary>
public sealed record FemSectionLocationRow(
    string SourceMemberTag,
    int MeshElementTag,
    int SectionTag,
    int IntegrationPoint,
    double PositionFromMemberStartM,
    double MemberLengthM,
    double RelativePosition,
    bool IsStateAvailable);

/// <summary>Строит каталог fiber-сечений по mesh-элементам и фактическим положениям OpenSees.</summary>
public sealed class FemSectionLocationResolver
{
    sealed record Segment(int ElementTag, int A, int B, double Length);

    /// <summary>Возвращает точки интегрирования, ориентированные от узла I исходного стержня.</summary>
    public IReadOnlyList<FemSectionLocationRow> Resolve(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemMember> sourceMembers,
        IReadOnlyList<FemNonlinearSectionLocation> recordedLocations,
        IReadOnlySet<(int ElementTag, int IntegrationPoint)> availableStates)
    {
        var nodeByTag = meshNodes
            .Where(n => int.TryParse(n.NodeTag, out _))
            .ToDictionary(n => int.Parse(n.NodeTag));
        var elementsByMember = meshElements
            .Where(e => e.SourceMemberTag is not null && e.ElemType == "beam")
            .Select(e => (Element: e, Ends: ReadEnds(e.NodeIdsJson)))
            .Where(x => int.TryParse(x.Element.ElemTag, out _) && x.Ends is { Length: 2 })
            .Where(x => nodeByTag.ContainsKey(x.Ends![0]) && nodeByTag.ContainsKey(x.Ends[1]))
            .GroupBy(x => x.Element.SourceMemberTag!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var locationsByElement = recordedLocations.GroupBy(l => l.ElementTag)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.IntegrationPoint).ToList());
        var memberByTag = sourceMembers.ToDictionary(m => m.ElemTag, StringComparer.Ordinal);
        var rows = new List<FemSectionLocationRow>();

        foreach (var (memberTag, candidates) in elementsByMember)
        {
            if (!memberByTag.TryGetValue(memberTag, out var member)) continue;
            var sourceEnds = ReadEnds(member.NodeIdsJson);
            if (sourceEnds is not { Length: 2 }) continue;

            var remaining = candidates.ToDictionary(
                x => int.Parse(x.Element.ElemTag),
                x => new Segment(
                    int.Parse(x.Element.ElemTag), x.Ends![0], x.Ends[1],
                    Distance(nodeByTag[x.Ends[0]], nodeByTag[x.Ends[1]])));
            int current = sourceEnds[0];
            double cumulative = 0;
            var ordered = new List<(Segment Segment, bool Reversed, double Start)>();
            while (remaining.Count > 0)
            {
                var match = remaining.Values.FirstOrDefault(s => s.A == current || s.B == current);
                if (match is null) break;
                bool reversed = match.B == current;
                ordered.Add((match, reversed, cumulative));
                cumulative += match.Length;
                current = reversed ? match.A : match.B;
                remaining.Remove(match.ElementTag);
            }
            if (ordered.Count == 0 || current != sourceEnds[1]) continue;

            double memberLength = cumulative;
            foreach (var (segment, reversed, start) in ordered)
            {
                if (!locationsByElement.TryGetValue(segment.ElementTag, out var locations)) continue;
                foreach (var location in locations)
                {
                    double localDistance = Math.Clamp(location.DistanceFromElementStartM, 0, segment.Length);
                    if (reversed) localDistance = segment.Length - localDistance;
                    double position = start + localDistance;
                    rows.Add(new FemSectionLocationRow(
                        memberTag,
                        segment.ElementTag,
                        location.SectionTag,
                        location.IntegrationPoint,
                        position,
                        memberLength,
                        memberLength > 0 ? position / memberLength : 0,
                        availableStates.Contains((segment.ElementTag, location.IntegrationPoint))));
                }
            }
        }

        return rows.OrderBy(r => r.SourceMemberTag, StringComparer.Ordinal)
            .ThenBy(r => r.PositionFromMemberStartM)
            .ThenBy(r => r.IntegrationPoint)
            .ToList();
    }

    static int[]? ReadEnds(string json)
    {
        try
        {
            var ends = JsonSerializer.Deserialize<int[]>(json);
            return ends is { Length: 2 } ? ends : null;
        }
        catch (JsonException) { return null; }
    }

    static double Distance(FemMeshNode a, FemMeshNode b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
