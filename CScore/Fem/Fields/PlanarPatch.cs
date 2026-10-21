namespace CScore.Fem.Fields;

/// <summary>Пластина-цель поля: тег, теги узлов по контуру и их координаты.</summary>
public sealed record FieldShell(string Tag, IReadOnlyList<string> NodeTags, IReadOnlyList<FieldVec3> Nodes)
{
    public FieldVec3 Centroid => new(Nodes.Average(n => n.X), Nodes.Average(n => n.Y), Nodes.Average(n => n.Z));

    /// <summary>Нормаль по Ньюэллу (не единичная): длина — удвоенная площадь.</summary>
    FieldVec3 NewellNormal
    {
        get
        {
            double nx = 0, ny = 0, nz = 0;
            for (int i = 0; i < Nodes.Count; i++)
            {
                var a = Nodes[i];
                var b = Nodes[(i + 1) % Nodes.Count];
                nx += (a.Y - b.Y) * (a.Z + b.Z);
                ny += (a.Z - b.Z) * (a.X + b.X);
                nz += (a.X - b.X) * (a.Y + b.Y);
            }
            return new FieldVec3(nx, ny, nz);
        }
    }

    public FieldVec3 Normal => NewellNormal.Normalized();
    public double Area => NewellNormal.Length / 2;
}

/// <summary>Плоский участок целей: плоскость (начало, нормаль, оси U, V в плоскости) и его пластины.</summary>
public sealed record PlanarPatch(int Index, FieldVec3 Origin, FieldVec3 Normal, FieldVec3 U, FieldVec3 V,
    IReadOnlyList<FieldShell> Shells)
{
    /// <summary>Оси в плоскости: у пологого участка U — проекция глобальной X, у крутого — горизонталь ez × n.</summary>
    public static PlanarPatch Create(int index, FieldVec3 origin, FieldVec3 normal, IReadOnlyList<FieldShell> shells)
    {
        var n = normal.Normalized();
        var ex = new FieldVec3(1, 0, 0);
        var ez = new FieldVec3(0, 0, 1);
        var u = Math.Abs(n.Z) > 0.9 ? (ex - n * n.Dot(ex)).Normalized() : ez.Cross(n).Normalized();
        return new PlanarPatch(index, origin, n, u, n.Cross(u), shells);
    }

    public (double U, double V) Project(FieldVec3 p) { var d = p - Origin; return (d.Dot(U), d.Dot(V)); }

    public double DistanceToPlane(FieldVec3 p) => Math.Abs((p - Origin).Dot(Normal));

    /// <summary>Стена или крутой скат (наклон круче 60°): таблица без Z на нём значений не определяет.</summary>
    public bool IsSteep => Math.Abs(Normal.Z) < 0.5;
}

/// <summary>Деление пластин-целей на плоские участки.</summary>
public static class PlanarPatchSplitter
{
    /// <summary>
    /// Участок растёт от первой неразобранной пластины по общим рёбрам: соседняя пластина входит, если её нормаль
    /// отклоняется от нормали затравки не больше <paramref name="angleTolDeg"/> и все её узлы ближе
    /// <paramref name="distanceTol"/> к плоскости затравки. Плоскость участка затем уточняется по площадям.
    /// </summary>
    public static IReadOnlyList<PlanarPatch> Split(IReadOnlyList<FieldShell> shells, double angleTolDeg, double distanceTol)
    {
        var owners = new Dictionary<(string, string), List<int>>();
        for (int i = 0; i < shells.Count; i++)
        {
            var t = shells[i].NodeTags;
            for (int k = 0; k < t.Count; k++)
            {
                string a = t[k], b = t[(k + 1) % t.Count];
                var key = string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
                if (!owners.TryGetValue(key, out var list)) owners[key] = list = [];
                list.Add(i);
            }
        }
        var neighbours = Enumerable.Range(0, shells.Count).Select(_ => new List<int>()).ToArray();
        foreach (var list in owners.Values)
            foreach (int a in list)
                foreach (int b in list)
                    if (a != b) neighbours[a].Add(b);

        double cosTol = Math.Cos(angleTolDeg * Math.PI / 180);
        var patchOf = Enumerable.Repeat(-1, shells.Count).ToArray();
        var patches = new List<PlanarPatch>();
        for (int seed = 0; seed < shells.Count; seed++)
        {
            if (patchOf[seed] >= 0) continue;
            int index = patches.Count;
            var n0 = shells[seed].Normal;
            var o0 = shells[seed].Centroid;
            var members = new List<int> { seed };
            patchOf[seed] = index;
            var queue = new Queue<int>([seed]);
            while (queue.Count > 0)
            {
                foreach (int j in neighbours[queue.Dequeue()])
                {
                    if (patchOf[j] >= 0) continue;
                    var s = shells[j];
                    if (Math.Abs(s.Normal.Dot(n0)) < cosTol) continue;
                    if (s.Nodes.Any(p => Math.Abs((p - o0).Dot(n0)) > distanceTol)) continue;
                    patchOf[j] = index;
                    members.Add(j);
                    queue.Enqueue(j);
                }
            }
            FieldVec3 sumN = default, sumC = default;
            double sumA = 0;
            foreach (int m in members)
            {
                var s = shells[m];
                double area = s.Area;
                var n = s.Normal.Dot(n0) < 0 ? s.Normal * -1 : s.Normal;
                sumN += n * area;
                sumC += s.Centroid * area;
                sumA += area;
            }
            patches.Add(PlanarPatch.Create(index, sumA > 0 ? sumC * (1 / sumA) : o0, sumA > 0 ? sumN : n0,
                members.Select(m => shells[m]).ToList()));
        }
        return patches;
    }
}
