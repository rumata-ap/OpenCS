using CSfea.Core;
using CSfea.Sparse;
using CScore.PlateStrip;

namespace CSfea.CScoreBridge;

/// <summary>Section resultants одной точки интегрирования RVE-патча — в общем базисе патча
/// (не в локальном базисе конкретного элемента), с весом area-averaging.</summary>
public readonly record struct PatchSectionResultant(
    double Nx, double Ny, double Nxy, double Mx, double My, double Mxy, double Weight);

/// <summary>Вычисляет section resultants RVE-патча из решённого глобального перемещения,
/// проецируя КАЖДЫЙ элемент в ОБЩИЙ базис патча (не через ShellGeometry.LocalFrame — тот
/// строит базис из первого ребра/диагонали каждого элемента отдельно, что даёт разную
/// ориентацию constitutive law по соседним Gmsh-элементам, см. спеку, раздел «Constitutive
/// frame в CSfea: input projection, не output rotation»).</summary>
public static class ShellMeshPatchPostprocessor
{
    public static IReadOnlyList<PatchSectionResultant> SectionResultantsAt(
        ShellMesh mesh, double[] uGlobal, double[] patchOriginWorld, double[,] patchBasis)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(uGlobal);
        ArgumentNullException.ThrowIfNull(patchOriginWorld);
        ArgumentNullException.ThrowIfNull(patchBasis);

        var result = new List<PatchSectionResultant>();

        for (int e = 0; e < mesh.Elements.Length; e++)
        {
            int[] el = mesh.Elements[e];
            int n = el.Length;
            var coords = new double[n][];
            for (int i = 0; i < n; i++) coords[i] = mesh.Nodes[el[i]];

            var xy = new double[n, 2];
            for (int i = 0; i < n; i++)
            {
                var d = Dense.SubV(coords[i], patchOriginWorld);
                for (int k = 0; k < 2; k++)
                    xy[i, k] = d[0] * patchBasis[k, 0] + d[1] * patchBasis[k, 1] + d[2] * patchBasis[k, 2];
            }

            var tFull = ShellGeometry.BuildTMatrix(patchBasis, n);
            var uElementGlobal = new double[6 * n];
            for (int i = 0; i < n; i++)
                for (int c = 0; c < 6; c++)
                    uElementGlobal[6 * i + c] = uGlobal[6 * el[i] + c];
            var uLocal = Dense.MatVec(Dense.Transpose(tFull), uElementGlobal); // 5n: [u,v,w,thetaX,thetaY] на узел, в базисе патча

            if (n == 3)
            {
                var (bm, bb, area) = Shell3.BMatricesBendingMembrane(xy);
                AddPoint(result, mesh, e, bm, bb, uLocal, weight: area);
            }
            else if (n == 4)
            {
                var (pts, wts) = Shell4.Gauss2x2();
                for (int g = 0; g < pts.Length; g++)
                {
                    var (bm, bb, detJ) = Shell4.BMatricesBendingMembrane(xy, pts[g][0], pts[g][1]);
                    AddPoint(result, mesh, e, bm, bb, uLocal, weight: wts[g] * detJ);
                }
            }
            else
            {
                throw new ArgumentException($"Элемент {e}: поддерживаются только 3 или 4 узла.");
            }
        }

        return result;
    }

    static void AddPoint(List<PatchSectionResultant> result, ShellMesh mesh, int elementIndex,
                         double[,] bm, double[,] bb, double[] uLocal, double weight)
    {
        double[] epsM = Dense.MatVec(bm, uLocal);
        double[] kappa = Dense.MatVec(bb, uLocal);
        var gamma = new double[2]; // KUBC этого среза даёт тождественно нулевой поперечный сдвиг — не вычисляется отдельно.
        ShellForces forces = mesh.Section(elementIndex).Forces(epsM, kappa, gamma);
        result.Add(new PatchSectionResultant(
            forces.N[0], forces.N[1], forces.N[2],
            forces.M[0], forces.M[1], forces.M[2],
            weight));
    }

    /// <summary>Area-weighted среднее по точкам интегрирования — Hill-Mandel гомогенизация.</summary>
    public static PlateResultants Average(IReadOnlyList<PatchSectionResultant> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
            throw new ArgumentException("Список точек интегрирования не должен быть пустым.", nameof(points));

        double sumW = 0, nx = 0, ny = 0, nxy = 0, mx = 0, my = 0, mxy = 0;
        foreach (var p in points)
        {
            sumW += p.Weight;
            nx += p.Nx * p.Weight; ny += p.Ny * p.Weight; nxy += p.Nxy * p.Weight;
            mx += p.Mx * p.Weight; my += p.My * p.Weight; mxy += p.Mxy * p.Weight;
        }
        if (!(sumW > 0.0))
            throw new InvalidOperationException("Суммарный вес точек интегрирования должен быть положительным.");

        return new PlateResultants(nx / sumW, ny / sumW, nxy / sumW, mx / sumW, my / sumW, mxy / sumW);
    }
}
