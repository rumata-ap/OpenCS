namespace CSfea.Torsion;

/// <summary>Постпроцессор МКЭ: It, поле τ/(GΘ), τ_max из решения φ. Работает и с T3, и с T6 сеткой.</summary>
public static class TorsionPostprocessor
{
    /// <summary>It = 2·∫_Ω φ dA через аналитический ∫N_i dA (сумма по элементам).</summary>
    public static double ComputeIt(TorsionMesh mesh, double[] phi)
    {
        double it = 0.0;
        foreach (var el in mesh.Triangles)
        {
            double[] c = BuildCoords(mesh, el);
            double[] m = el.Length == 6 ? PrandtlTri6.MassVector(c) : PrandtlTri3.MassVector(c);
            for (int k = 0; k < el.Length; k++) it += m[k] * phi[el[k]];
        }
        return 2.0 * it;
    }

    /// <summary>
    /// Касательные напряжения в узлах: τx = −∂φ/∂y, τy = ∂φ/∂x.
    /// T3: производные постоянны на элементе (CST), усредняются по примыкающим элементам.
    /// T6: производные линейны внутри элемента — вычисляются отдельно в каждом узле
    /// (<see cref="PrandtlTri6.NodeGradient"/>), затем усредняются по примыкающим элементам так же.
    /// </summary>
    public static (double[] tauX, double[] tauY) ComputeStresses(TorsionMesh mesh, double[] phi)
    {
        int n = mesh.NodesX.Length;
        var sx = new double[n]; var sy = new double[n]; var cnt = new int[n];
        foreach (var el in mesh.Triangles)
        {
            double[] c = BuildCoords(mesh, el);
            if (el.Length == 6)
            {
                var elPhi = new double[6];
                for (int k = 0; k < 6; k++) elPhi[k] = phi[el[k]];
                for (int k = 0; k < 6; k++)
                {
                    var (dphidx, dphidy) = PrandtlTri6.NodeGradient(k, c, elPhi);
                    sx[el[k]] += -dphidy;
                    sy[el[k]] += dphidx;
                    cnt[el[k]]++;
                }
            }
            else
            {
                double area2 = PrandtlTri3.Det(c); // 2A со знаком
                double[] b = { c[3] - c[5], c[5] - c[1], c[1] - c[3] };
                double[] cc = { c[4] - c[2], c[0] - c[4], c[2] - c[0] };
                double dphidx = (b[0] * phi[el[0]] + b[1] * phi[el[1]] + b[2] * phi[el[2]]) / area2;
                double dphidy = (cc[0] * phi[el[0]] + cc[1] * phi[el[1]] + cc[2] * phi[el[2]]) / area2;
                for (int k = 0; k < 3; k++)
                {
                    sx[el[k]] += -dphidy;
                    sy[el[k]] += dphidx;
                    cnt[el[k]]++;
                }
            }
        }
        var tauX = new double[n]; var tauY = new double[n];
        for (int i = 0; i < n; i++)
        {
            tauX[i] = cnt[i] > 0 ? sx[i] / cnt[i] : 0.0;
            tauY[i] = cnt[i] > 0 ? sy[i] / cnt[i] : 0.0;
        }
        return (tauX, tauY);
    }

    static double[] BuildCoords(TorsionMesh mesh, int[] el)
    {
        var c = new double[el.Length * 2];
        for (int k = 0; k < el.Length; k++)
        {
            c[2 * k] = mesh.NodesX[el[k]];
            c[2 * k + 1] = mesh.NodesY[el[k]];
        }
        return c;
    }
}
