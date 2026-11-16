using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Sp63Punching.Fem;

/// <summary>Способ определения продавливающей силы.</summary>
public enum PunchingFemForceMethod
{
    /// <summary>По стержням колонн: действие на плиту минус нагрузка в основании пирамиды (S).</summary>
    Column,
    /// <summary>По разрезу плиты по границе основания пирамиды — узловые силы КЭ или интеграл усилий (C).</summary>
    Contour,
}

/// <summary>
/// Усилия узла по одному ключу (загружение или сочетание), кН и кН·м, в осях узла.
/// </summary>
/// <param name="F">Модуль продавливающей силы главного способа (за вычетом нагрузки в основании пирамиды), кН.</param>
/// <param name="Side">Сторона приложения по знаку силы главного способа.</param>
/// <param name="Mx">Момент на плиту от стержней относительно оси x узла (Mloc), кН·м.</param>
/// <param name="My">Момент относительно оси y узла, кН·м.</param>
/// <param name="Method">Главный способ.</param>
/// <param name="ColumnAction">Действие стержней на плиту по нормали, кН (+ — вверх); null — нет сил стержней.</param>
/// <param name="BaseLoad">Нагрузка в основании пирамиды по нормали, кН (+ — вверх); null — неизвестна.</param>
/// <param name="ContourForce">Сила по разрезу основания: −(действие внешней части плиты)·n, кН; null — не считалась.</param>
public sealed record PunchingFemForces(
    double F, PunchingLoadSide Side, double Mx, double My, PunchingFemForceMethod Method,
    double? ColumnAction, double? BaseLoad, double? ContourForce)
{
    /// <summary>
    /// Пилон из пластин: сила по разрезу у торца −x, у торца +x и в середине, кН (со знаком силы продавливания);
    /// null — не пилон из пластин или разреза нет.
    /// </summary>
    public (double Minus, double Plus, double Middle)? PylonParts { get; init; }

    /// <summary>
    /// Узел стены: сила по разрезу на гранях основания поперёк стены, кН (в силу продавливания не входит; для
    /// самопроверки равновесия: <see cref="ColumnForce"/> = <see cref="ContourForce"/> + <see cref="WallExcluded"/>).
    /// </summary>
    public double? WallExcluded { get; init; }

    /// <summary>
    /// Торец стены, CSfea: самопроверка ΔM на П-контуре h0/2 — по моментам пластин в центрах КЭ (Plate, тем же способом,
    /// что ΔM) и по узловым силам разреза тела внутри П (Nodal), кН·м; Scale — Σ|m|·ds по контуру, кН·м. null — не торец
    /// или у источника нет узловых сил.
    /// </summary>
    public (double Plate, double Nodal, double Scale)? WallDeltaMCheck { get; init; }

    /// <summary>Сила по стержням со знаком: действие стержней плюс нагрузка основания, кН; null — нет данных.</summary>
    public double? ColumnForce => ColumnAction is double a ? a + (BaseLoad ?? 0) : null;

    /// <summary>Невязка способов: сила по стержням минус сила по разрезу, кН (у CSfea ≈ 0); null — нет одного из них.</summary>
    public double? Mismatch => ColumnForce is double s && ContourForce is double c ? s - c : null;
}

/// <summary>Действие стержней колонн на плиту и границы основания пирамиды узла.</summary>
public static class PunchingFemColumnAction
{
    /// <summary>
    /// Действие стержней узла на плиту в общих осях, Н и Н·м (момент — относительно начала осей узла): сумма сил узла
    /// на КЭ стержня у плиты с обратным знаком (<see cref="BeamEndForces"/> — силы узла на КЭ, местные оси
    /// <see cref="BeamLocalAxisConvention"/>). null — у какого-либо стержня нет сил по ключу.
    /// </summary>
    public static (PlanarVector3 F, PlanarVector3 M)? Compute(PunchingFemJoint joint, IReadOnlyDictionary<string, FemMeshNode> nodes,
        IReadOnlyDictionary<string, FemElement> elements, IFragmentParentResult result, FragmentMaskKey key)
    {
        ArgumentNullException.ThrowIfNull(joint);
        var f = PlanarVector3.Zero;
        var m = PlanarVector3.Zero;
        foreach (var bar in joint.Bars)
        {
            if (!elements.TryGetValue(bar.ElemTag, out var e) || FemElementGeometry.Of(e, nodes) is not { IsBar: true } g) return null;
            if (!result.TryBeamEndForces(key, bar.ElemTag, out var ends)) return null;
            bool atI = g.NodeTags[0] == bar.NodeTag;
            var end = atI ? ends.I : ends.J;
            var p = atI ? g.Points[0] : g.Points[1];
            var (ex, ey, ez) = BeamLocalAxisConvention.Frame(g.Points[0], g.Points[1], e.BeamRotationDeg ?? 0);
            var fe = ex * end.X + ey * end.Y + ez * end.Z;
            var me = ex * end.Rx + ey * end.Ry + ez * end.Rz;
            // Действие КЭ на узел — обратное силе узла на КЭ.
            f -= fe;
            m = m - me - (p - joint.Origin).Cross(fe);
        }
        return (f, m);
    }

    /// <summary>
    /// Действие пилона из пластин на плиту в общих осях, Н и Н·м (момент — относительно начала осей узла): минус сумма
    /// узловых сил его пластин (f_int − f_нагр, сила узла на КЭ) в узлах примыкания. null — нет сил КЭ по ключу.
    /// </summary>
    public static (PlanarVector3 F, PlanarVector3 M)? ComputeShells(PunchingFemJoint joint, IReadOnlyDictionary<string, FemMeshNode> nodes,
        IFragmentParentNodalForces nodal, FragmentMaskKey key)
    {
        ArgumentNullException.ThrowIfNull(joint);
        ArgumentNullException.ThrowIfNull(nodal);
        var junction = joint.JunctionNodes.ToHashSet(StringComparer.Ordinal);
        var f = PlanarVector3.Zero;
        var m = PlanarVector3.Zero;
        foreach (var tag in joint.WallShells)
        {
            if (!nodal.TryElementNodeForces(key, tag, out var forces)) return null;
            foreach (var (n, v) in forces)
            {
                if (!junction.Contains(n) || !nodes.TryGetValue(n, out var mn)) continue;
                var p = new PlanarVector3(mn.X, mn.Y, mn.Z);
                f -= v.Force;
                m = m - v.Moment - (p - joint.Origin).Cross(v.Force);
            }
        }
        return (f, m);
    }

    /// <summary>
    /// Пилон из пластин: сила по разрезу основания у торцов и в середине, кН. Торец — часть основания, где |x| не меньше
    /// L/2 − Lmin − h0/2 (как ветви П-контура СП 430 длиной Lmin + h0 от торцевого отрезка на h0/2 за торцом).
    /// </summary>
    public static (double Minus, double Plus, double Middle)? PylonParts(PunchingFemJoint joint, IReadOnlyList<FragmentNodalTerm> terms)
    {
        if (joint.Kind != PunchingFemJointKind.PylonShells || joint.Area is not PunchingLoadArea.Rectangle r) return null;
        double h0 = joint.H0, half = Math.Max(r.Bx, r.By) / 2, tmin = Math.Min(r.Bx, r.By);
        bool alongX = r.Bx >= r.By;
        double limit = half - tmin - h0 / 2;
        double minus = 0, plus = 0, middle = 0;
        foreach (var t in terms)
        {
            var d = t.At - joint.Origin;
            double u = alongX ? d.Dot(joint.AxisX) : d.Dot(joint.AxisY);
            double v = -t.F.Dot(joint.Normal) / 1e3;
            if (u >= limit) plus += v;
            else if (u <= -limit) minus += v;
            else middle += v;
        }
        return (minus, plus, middle);
    }

    /// <summary>
    /// Границы основания пирамиды узла: вертикальные грани многоугольника основания (общие оси) и полоса
    /// ±<paramref name="halfBand"/> около плиты. Плита горизонтальна (поиск узлов это проверяет).
    /// </summary>
    public static FragmentBounds BaseBounds(PunchingFemJoint joint, double halfBand = 50, double tol = 1e-6)
    {
        ArgumentNullException.ThrowIfNull(joint);
        return FragmentBounds.OfFaces(joint.Origin.Z - halfBand, joint.Origin.Z + halfBand, BaseFaces(joint).Select(x => x.Face), tol);
    }

    /// <summary>Грани основания пирамиды: грань и её отрезок в общих осях (на отметке плиты).</summary>
    public static IReadOnlyList<(FragmentPrismFace Face, PlanarVector3 A, PlanarVector3 B)> BaseFaces(PunchingFemJoint joint)
    {
        var poly = joint.PyramidBase;
        var list = new List<(FragmentPrismFace, PlanarVector3, PlanarVector3)>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = joint.ToGlobal(poly[i]);
            var b = joint.ToGlobal(poly[(i + 1) % poly.Count]);
            var d = b - a;
            var n = new PlanarVector3(d.Y, -d.X, 0);
            if (n.Length < 1e-12) continue;
            n = n.Normalize();
            list.Add((new FragmentPrismFace(n, n.Dot(a)), a, b));
        }
        return list;
    }
}
