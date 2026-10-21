using System.Globalization;

namespace CScore.Fem.Fields;

/// <summary>Перенос таблицы точек на пластины по плоским участкам (спека § 3.1).</summary>
public static class ShellFieldInterpolator
{
    public static FieldEvaluation Evaluate(IReadOnlyList<FieldPoint> points, bool hasZ, IReadOnlyList<FieldShell> shells, FieldSettings s)
    {
        var patches = PlanarPatchSplitter.Split(shells, s.AngleTolDeg, s.PlaneTolerance);
        var used = new bool[points.Count];
        var results = new List<FieldTargetValue>(shells.Count);
        var noZSteep = new List<string>();
        foreach (var patch in patches)
        {
            if (!hasZ && patch.IsSteep)
            {
                foreach (var sh in patch.Shells)
                {
                    noZSteep.Add(sh.Tag);
                    results.Add(new FieldTargetValue(sh.Tag, null, FieldHow.None, double.PositiveInfinity));
                }
                continue;
            }
            var uvNodes = patch.Shells.SelectMany(x => x.Nodes).Select(patch.Project).ToList();
            double u0 = uvNodes.Min(x => x.U), u1 = uvNodes.Max(x => x.U), v0 = uvNodes.Min(x => x.V), v1 = uvNodes.Max(x => x.V);
            // Точка «попала на участок» — в габарите с запасом MaxDistance; в триангуляцию берётся и запас на
            // размер участка: редкая таблица (значения только по углам) иначе теряет вершины треугольников,
            // накрывающих КЭ.
            double reach = s.MaxDistance + Math.Sqrt((u1 - u0) * (u1 - u0) + (v1 - v0) * (v1 - v0));
            var local = new List<(double U, double V)>();
            var localIdx = new List<int>();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i].P;
                if (!hasZ)
                    p = p with
                    {
                        Z = patch.Origin.Z - (patch.Normal.X * (p.X - patch.Origin.X) + patch.Normal.Y * (p.Y - patch.Origin.Y)) / patch.Normal.Z,
                    };
                else if (patch.DistanceToPlane(p) > s.PlaneTolerance) continue;
                var (u, v) = patch.Project(p);
                if (u < u0 - reach || u > u1 + reach || v < v0 - reach || v > v1 + reach) continue;
                local.Add((u, v));
                localIdx.Add(i);
                if (u >= u0 - s.MaxDistance && u <= u1 + s.MaxDistance && v >= v0 - s.MaxDistance && v <= v1 + s.MaxDistance)
                    used[i] = true;
            }
            var tree = new KdTree3(local.Select(q => new FieldVec3(q.U, q.V, 0)).ToList());
            var tri = s.Method == FieldMethod.Interpolate ? PlaneTriangulation.Build(local) : null;
            foreach (var sh in patch.Shells)
            {
                var (u, v) = patch.Project(sh.Centroid);
                results.Add(Value(sh.Tag, u, v, points, localIdx, tree, tri, s));
            }
        }

        var diags = new List<FieldDiagnostic>();
        var steep = noZSteep.ToHashSet(StringComparer.Ordinal);
        var none = results.Where(r => r.How == FieldHow.None && !steep.Contains(r.Tag)).Select(r => r.Tag).ToList();
        if (none.Count > 0)
            diags.Add(new("field_no_value", $"Нет значения: дальше {F(s.MaxDistance)} м от точек таблицы; КЭ", none));
        var outside = results.Where(r => s.Method == FieldMethod.Interpolate && r.How == FieldHow.Nearest).ToList();
        if (outside.Count > 0)
            diags.Add(new("field_nearest_outside",
                $"Вне облака точек — значение ближайшей точки (до {F(outside.Max(r => r.Distance))} м); КЭ",
                outside.Select(r => r.Tag).ToList()));
        if (noZSteep.Count > 0)
            diags.Add(new("field_no_z_vertical", "Таблица без Z не определяет значения на стенах и крутых скатах; КЭ", noZSteep));
        int unused = used.Count(u => !u);
        if (unused > 0)
            diags.Add(new("field_points_off_plane",
                $"Точек таблицы не попало ни на один участок (дальше {F(s.PlaneTolerance)} м от плоскостей или за их краями): {unused}", []));
        return new FieldEvaluation(results, diags, patches, points.Count, unused);
    }

    static FieldTargetValue Value(string tag, double u, double v, IReadOnlyList<FieldPoint> points, List<int> localIdx,
        KdTree3 tree, PlaneTriangulation? tri, FieldSettings s)
    {
        if (tree.Count == 0) return new FieldTargetValue(tag, null, FieldHow.None, double.PositiveInfinity);
        var q = new FieldVec3(u, v, 0);
        var nearest = tree.Nearest(q);
        double[] Vals(int local) => (double[])points[localIdx[local]].Values.Clone();

        if (s.Method == FieldMethod.Interpolate && tri != null && tri.TryLocate(u, v, out var ids, out var w))
        {
            var a = points[localIdx[ids.A]].Values;
            var b = points[localIdx[ids.B]].Values;
            var c = points[localIdx[ids.C]].Values;
            var vals = new double[a.Length];
            for (int k = 0; k < vals.Length; k++) vals[k] = w.A * a[k] + w.B * b[k] + w.C * c[k];
            return new FieldTargetValue(tag, vals, FieldHow.Interpolated, nearest.Distance);
        }
        if (s.Method == FieldMethod.Idw)
        {
            var near = tree.KNearest(q, s.IdwK).Where(x => x.Distance <= s.MaxDistance).ToList();
            if (near.Count == 0) return new FieldTargetValue(tag, null, FieldHow.None, nearest.Distance);
            if (near[0].Distance < 1e-9) return new FieldTargetValue(tag, Vals(near[0].Index), FieldHow.Idw, 0);
            var vals = new double[points[localIdx[near[0].Index]].Values.Length];
            double sw = 0;
            foreach (var (index, d) in near)
            {
                double wi = 1 / Math.Pow(d, s.IdwPower);
                var pv = points[localIdx[index]].Values;
                for (int k = 0; k < vals.Length; k++) vals[k] += wi * pv[k];
                sw += wi;
            }
            for (int k = 0; k < vals.Length; k++) vals[k] /= sw;
            return new FieldTargetValue(tag, vals, FieldHow.Idw, near[0].Distance);
        }
        return nearest.Distance <= s.MaxDistance
            ? new FieldTargetValue(tag, Vals(nearest.Index), FieldHow.Nearest, nearest.Distance)
            : new FieldTargetValue(tag, null, FieldHow.None, nearest.Distance);
    }

    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
