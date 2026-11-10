using System.Globalization;
using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>
/// Пробная сетка черновика фрагмента для проверки равнодействующих (без Gmsh): области — CDT контура
/// (<see cref="PlanarRegionTriangulation"/>) с равномерным делением треугольников до шага сетки области
/// (<see cref="FemPlanarMeshPlan.MeshSize"/>), стержни — отрезки не длиннее шага. КЭ получают <c>SourceMemberTag</c>
/// своего КонЭ — нагрузки с целью «КонЭ» разрешаются как на настоящей сетке.
/// </summary>
public static class FragmentTrialMesh
{
    /// <summary>Шаг стержней без своего шага КонЭ, м.</summary>
    public const double DefaultBarStepM = 0.5;

    public static (List<FemMeshNode> Nodes, List<FemElement> Elements) Build(StoreyFragmentDraft draft, double? plateStepM = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var nodes = new List<FemMeshNode>();
        var elements = new List<FemElement>();
        var byKey = new Dictionary<(long, long, long), string>();
        static (long, long, long) Key(PlanarVector3 p) => ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6), (long)Math.Round(p.Z * 1e6));
        // Сохранённая сетка — своими КЭ и узлами (теги родителя: нагрузки на сохранённые КЭ ссылаются на них); новые теги —
        // выше наибольшего сохранённого.
        static int Max(IEnumerable<string> tags) =>
            tags.Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0).DefaultIfEmpty(0).Max();
        foreach (var n in draft.KeptMeshNodes)
        {
            nodes.Add(new FemMeshNode { NodeTag = n.NodeTag, X = n.X, Y = n.Y, Z = n.Z });
            byKey.TryAdd(Key(new PlanarVector3(n.X, n.Y, n.Z)), n.NodeTag);
        }
        var keptElements = draft.Members.SelectMany(m => m.KeptElements ?? []).ToList();
        elements.AddRange(keptElements.Select(e => e.ShallowCopy()));
        int nextNode = Max(nodes.Select(n => n.NodeTag)) + 1;
        int nextElement = Max(keptElements.Select(e => e.ElemTag)) + 1;
        string Node(PlanarVector3 p)
        {
            var key = Key(p);
            if (byKey.TryGetValue(key, out var tag)) return tag;
            tag = (nextNode++).ToString(CultureInfo.InvariantCulture);
            nodes.Add(new FemMeshNode { NodeTag = tag, X = p.X, Y = p.Y, Z = p.Z });
            return byKey[key] = tag;
        }
        void Element(string type, string member, params PlanarVector3[] points) => elements.Add(new FemElement
        {
            ElemTag = (nextElement++).ToString(CultureInfo.InvariantCulture), ElemType = type, SourceMemberTag = member,
            NodeIdsJson = "[" + string.Join(",", points.Select(Node)) + "]",
        });

        var draftNodes = draft.Nodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        foreach (var part in draft.Members)
        {
            var (member, region, _) = part;
            if (part.KeptElements is not null) continue;
            if (region is not null)
            {
                double h = FemPlanarMeshPlan.MeshSize(member, region, plateStepM);
                var (v, triangles) = PlanarRegionTriangulation.Triangulate(region);
                PlanarVector3 G(double u, double w) => PlanarBoundaryFrameConverter.ToGlobalPoint(region.Frame, new PlanarVector3(u, w, 0));
                foreach (var (ia, ib, ic) in triangles)
                {
                    var (a, b, c) = (v[ia], v[ib], v[ic]);
                    double edge = Math.Max(Dist(a, b), Math.Max(Dist(b, c), Dist(c, a)));
                    int n = h > 0 ? Math.Max(1, (int)Math.Ceiling(edge / h - 1e-9)) : 1;
                    PlanarVector3 At(int i, int j) =>
                        G(a.X + ((b.X - a.X) * i + (c.X - a.X) * j) / n, a.Y + ((b.Y - a.Y) * i + (c.Y - a.Y) * j) / n);
                    for (int i = 0; i < n; i++)
                        for (int j = 0; i + j < n; j++)
                        {
                            Element("shell", member.ElemTag, At(i, j), At(i + 1, j), At(i, j + 1));
                            if (i + j < n - 1) Element("shell", member.ElemTag, At(i + 1, j), At(i + 1, j + 1), At(i, j + 1));
                        }
                }
            }
            else if (member.ElemType == "beam")
            {
                var ids = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [];
                if (ids.Length < 2
                    || !draftNodes.TryGetValue(ids[0].ToString(CultureInfo.InvariantCulture), out var na)
                    || !draftNodes.TryGetValue(ids[^1].ToString(CultureInfo.InvariantCulture), out var nb)) continue;
                PlanarVector3 a = new(na.X, na.Y, na.Z), b = new(nb.X, nb.Y, nb.Z);
                double step = member.TargetMeshLengthM is > 0 and var t ? t : DefaultBarStepM;
                int n = Math.Max(1, (int)Math.Ceiling((b - a).Length / step - 1e-9));
                for (int k = 0; k < n; k++) Element("beam", member.ElemTag, a + (b - a) * ((double)k / n), a + (b - a) * ((double)(k + 1) / n));
            }
        }
        return (nodes, elements);
    }

    static double Dist((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
