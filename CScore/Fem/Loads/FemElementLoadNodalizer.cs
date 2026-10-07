using CScore.Planar;

namespace CScore.Fem.Loads;

/// <summary>Узловое воздействие на узел сетки: силы и моменты в глобальных осях, Н и Н·м.</summary>
public sealed record FemNodalForce(string NodeTag, double Fx, double Fy, double Fz, double Mx, double My, double Mz);

/// <summary>
/// Перенос нагрузок на КЭ в согласованные узловые силы сетки. Пластина: равномерная — ∫Nᵢ dA, по узлам — ∫NᵢNⱼ dA,
/// сосредоточенная — Nᵢ в точке (моменты от неё не создаются). Стержень: равномерная и сосредоточенная — эрмитовы
/// функции (поперечная часть даёт концевые моменты), продольная — линейные.
/// </summary>
public static class FemElementLoadNodalizer
{
    /// <summary>Добавляет узловые силы нагрузки (с множителем <paramref name="factor"/>) в <paramref name="forces"/>.</summary>
    public static void Accumulate(FemElementLoad load, FemLoadMeshContext mesh, double factor,
        Dictionary<string, double[]> forces, List<FemValidationDiagnostic> diagnostics)
    {
        var elements = FemLoadTargets.Resolve(load, mesh, diagnostics);
        var skipped = new List<string>();
        string? reason = null;
        foreach (var e in elements)
        {
            string? why = AccumulateElement(load, e, mesh, factor, forces);
            if (why == null) continue;
            skipped.Add(e.ElemTag);
            reason ??= why;
        }
        if (skipped.Count > 0)
            diagnostics.Add(new("element_load_skipped",
                $"Нагрузка {FemLoadTargets.Describe(load)}: пропущены КЭ {FemLoadTargets.Sample(skipped)} — {reason}.",
                true, skipped.ToArray()));
    }

    /// <summary>Собственный вес всей сетки с коэффициентом (вниз по −Z): пластины γ·h, стержни γ·A.</summary>
    public static void AccumulateSelfWeight(double coefficient, IEnumerable<FemElement> elements, FemLoadMeshContext mesh,
        Dictionary<string, double[]> forces, List<FemValidationDiagnostic> diagnostics)
    {
        var skipped = new List<string>();
        foreach (var e in elements)
        {
            var g = mesh.Geometry(e);
            double? q = g == null ? null : SelfWeightIntensity(e, g, mesh.SelfWeight);
            if (q is not { } value) { skipped.Add(e.ElemTag); continue; }
            Distribute(g!, new PlanarVector3(0, 0, -value * coefficient), forces);
        }
        if (skipped.Count > 0)
            diagnostics.Add(new("self_weight_skipped",
                $"Собственный вес не учтён у КЭ {FemLoadTargets.Sample(skipped)}: нет удельного веса, толщины или площади сечения.",
                false, skipped.ToArray()));
    }

    /// <summary>Интенсивность собственного веса КЭ: пластина γ·h (Па), стержень γ·A (Н/м); null — данных нет.</summary>
    public static double? SelfWeightIntensity(FemElement e, FemElementGeometry g, IFemSelfWeightSource? source)
    {
        if (source?.UnitWeight(e) is not { } gamma || gamma <= 0) return null;
        if (g.IsShell) return e.ThicknessM is { } h && h > 0 ? gamma * h : null;
        return source.BarArea(e) is { } a && a > 0 ? gamma * a : null;
    }

    /// <summary>Узловые силы одного КЭ; возвращает причину пропуска или null.</summary>
    static string? AccumulateElement(FemElementLoad load, FemElement e, FemLoadMeshContext mesh, double factor,
        Dictionary<string, double[]> forces)
    {
        var g = mesh.Geometry(e);
        if (g == null) return "нет узлов сетки";
        var v = load.Values;

        if (load.LoadKind == FemElementLoadKinds.SelfWeight)
        {
            if (v.Count < 1) return "нет коэффициента";
            if (SelfWeightIntensity(e, g, mesh.SelfWeight) is not { } q) return "нет удельного веса, толщины или площади";
            Distribute(g, new PlanarVector3(0, 0, -q * v[0] * factor), forces);
            return null;
        }

        if (Direction(load, g) is not { } dir) return g.IsBar && load.IsLocal
            ? "местные оси y/z стержня не поддерживаются" : "неизвестное направление";

        switch (load.LoadKind)
        {
            case FemElementLoadKinds.Uniform:
                if (v.Count < 1) return "нет значения";
                Distribute(g, dir * (v[0] * factor), forces);
                return null;

            case FemElementLoadKinds.Nodal:
                if (!g.IsShell) return "интенсивность по узлам — только для пластин";
                if (v.Count != g.Points.Count) return $"ожидалось {g.Points.Count} значений по узлам";
                var m = g.ShellConsistentMatrix();
                for (int a = 0; a < g.Points.Count; a++)
                {
                    double s = 0;
                    for (int b = 0; b < g.Points.Count; b++) s += m[a, b] * v[b];
                    Add(forces, g.NodeTags[a], dir * (s * factor), PlanarVector3.Zero);
                }
                return null;

            case FemElementLoadKinds.Point when g.IsShell:
            {
                if (v.Count < 3) return "ожидалось [P, x, y]";
                var (ex, ey, _) = g.ShellFrame();
                var point = g.Points[g.Contour[0]] + ex * v[1] + ey * v[2];
                if (g.ShellShapeAt(point) is not { } n) return "точка вне КЭ";
                for (int k = 0; k < n.Length; k++) Add(forces, g.NodeTags[k], dir * (v[0] * n[k] * factor), PlanarVector3.Zero);
                return null;
            }

            case FemElementLoadKinds.Point:
            {
                if (v.Count < 2) return "ожидалось [P, a]";
                double l = g.Length, a = v[1];
                if (a < -1e-9 || a > l + 1e-9) return "точка вне стержня";
                BarPoint(g, dir * (v[0] * factor), Math.Clamp(a, 0, l), forces);
                return null;
            }

            default:
                return $"неизвестный вид «{load.LoadKind}»";
        }
    }

    /// <summary>Направление нагрузки (единичный вектор); null — не определено.</summary>
    internal static PlanarVector3? Direction(FemElementLoad load, FemElementGeometry g)
    {
        int axis = load.AxisIndex;
        if (axis < 0) return null;
        if (!load.IsLocal)
            return axis switch { 0 => new(1, 0, 0), 1 => new(0, 1, 0), _ => new(0, 0, 1) };
        if (g.IsShell)
        {
            var (x, y, z) = g.ShellFrame();
            return axis switch { 0 => x, 1 => y, _ => z };
        }
        // Стержень: местная x — по оси I → J однозначно; y/z зависят от конвенции программы-источника.
        return axis == 0 ? (g.Points[1] - g.Points[0]).Normalize() : null;
    }

    /// <summary>Равномерная интенсивность <paramref name="q"/> (Па или Н/м) по КЭ.</summary>
    static void Distribute(FemElementGeometry g, PlanarVector3 q, Dictionary<string, double[]> forces)
    {
        if (g.IsShell)
        {
            var w = g.ShellNodeWeights();
            for (int k = 0; k < w.Length; k++) Add(forces, g.NodeTags[k], q * w[k], PlanarVector3.Zero);
            return;
        }
        double l = g.Length;
        var axis = (g.Points[1] - g.Points[0]) * (1 / l);
        var transverse = q - axis * q.Dot(axis);
        var m = axis.Cross(transverse) * (l * l / 12);
        Add(forces, g.NodeTags[0], q * (l / 2), m);
        Add(forces, g.NodeTags[1], q * (l / 2), m * -1);
    }

    /// <summary>Сосредоточенная сила на стержне в точке a от узла I: эрмитовы функции для поперечной части.</summary>
    static void BarPoint(FemElementGeometry g, PlanarVector3 p, double a, Dictionary<string, double[]> forces)
    {
        double l = g.Length, b = l - a;
        var axis = (g.Points[1] - g.Points[0]) * (1 / l);
        var axial = axis * p.Dot(axis);
        var transverse = p - axial;
        var c = axis.Cross(transverse);
        Add(forces, g.NodeTags[0], axial * (b / l) + transverse * (b * b * (3 * a + b) / (l * l * l)), c * (a * b * b / (l * l)));
        Add(forces, g.NodeTags[1], axial * (a / l) + transverse * (a * a * (a + 3 * b) / (l * l * l)), c * (-a * a * b / (l * l)));
    }

    static void Add(Dictionary<string, double[]> forces, string tag, PlanarVector3 f, PlanarVector3 m)
    {
        if (!forces.TryGetValue(tag, out var v)) forces[tag] = v = new double[6];
        v[0] += f.X; v[1] += f.Y; v[2] += f.Z;
        v[3] += m.X; v[4] += m.Y; v[5] += m.Z;
    }
}
