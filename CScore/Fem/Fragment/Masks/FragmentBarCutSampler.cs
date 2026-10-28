using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>
/// Конец стержня фрагмента (спека разд. 4.2): перемещения и действие отброшенной части в точке КЭ стержня родителя.
/// </summary>
public static class FragmentBarCutSampler
{
    /// <summary>
    /// Перемещения в доле <paramref name="xi"/> КЭ I→J по перемещениям концов (общие оси): продольное и кручение —
    /// линейно, поперечные — Эрмит в местных осях (<see cref="BeamLocalAxisConvention"/>), повороты изгиба — производная.
    /// </summary>
    public static Dof6 Displacement(PlanarVector3 pi, PlanarVector3 pj, double rotationDeg, Dof6 ui, Dof6 uj, double xi)
    {
        var (ex, ey, ez) = BeamLocalAxisConvention.Frame(pi, pj, rotationDeg);
        double l = (pj - pi).Length;
        PlanarVector3 Loc(PlanarVector3 v) => new(v.Dot(ex), v.Dot(ey), v.Dot(ez));
        PlanarVector3 Glob(double x, double y, double z) => ex * x + ey * y + ez * z;
        var ti = Loc(ui.Force); var ri = Loc(ui.Moment);
        var tj = Loc(uj.Force); var rj = Loc(uj.Moment);

        double x2 = xi * xi, x3 = x2 * xi;
        double n1 = 1 - 3 * x2 + 2 * x3, n2 = xi - 2 * x2 + x3, n3 = 3 * x2 - 2 * x3, n4 = -x2 + x3;
        double d1 = (-6 * xi + 6 * x2) / l, d2 = 1 - 4 * xi + 3 * x2, d3 = (6 * xi - 6 * x2) / l, d4 = -2 * xi + 3 * x2;

        double ux = (1 - xi) * ti.X + xi * tj.X;
        double uy = n1 * ti.Y + n2 * l * ri.Z + n3 * tj.Y + n4 * l * rj.Z;
        double uz = n1 * ti.Z - n2 * l * ri.Y + n3 * tj.Z - n4 * l * rj.Y;
        double rx = (1 - xi) * ri.X + xi * rj.X;
        double rz = d1 * ti.Y + d2 * ri.Z + d3 * tj.Y + d4 * rj.Z;
        double ry = -(d1 * ti.Z - d2 * ri.Y + d3 * tj.Z - d4 * rj.Y);
        return Dof6.FromParts(Glob(ux, uy, uz), Glob(rx, ry, rz));
    }
}
