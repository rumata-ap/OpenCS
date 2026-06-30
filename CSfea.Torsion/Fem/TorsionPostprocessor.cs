namespace CSfea.Torsion;

/// <summary>Постпроцессор МКЭ: It, поле τ/(GΘ), τ_max из решения φ.</summary>
public static class TorsionPostprocessor
{
    /// <summary>It = 2·∫_Ω φ dA через ∫N_i dA = A/3 (сумма по элементам).</summary>
    public static double ComputeIt(TorsionMesh mesh, double[] phi)
    {
        double it = 0.0;
        foreach (var el in mesh.Triangles)
        {
            double[] c =
            {
                mesh.NodesX[el[0]], mesh.NodesY[el[0]],
                mesh.NodesX[el[1]], mesh.NodesY[el[1]],
                mesh.NodesX[el[2]], mesh.NodesY[el[2]]
            };
            double[] m = PrandtlTri3.MassVector(c); // A/3 каждый
            it += m[0] * phi[el[0]] + m[1] * phi[el[1]] + m[2] * phi[el[2]];
        }
        return 2.0 * it;
    }

    /// <summary>
    /// Касательные напряжения в узлах: τx = −∂φ/∂y, τy = ∂φ/∂x.
    /// Производные внутри элемента постоянны (CST) и усредняются по примыкающим элементам.
    /// </summary>
    public static (double[] tauX, double[] tauY) ComputeStresses(TorsionMesh mesh, double[] phi)
    {
        int n = mesh.NodesX.Length;
        var sx = new double[n]; var sy = new double[n]; var cnt = new int[n];
        foreach (var el in mesh.Triangles)
        {
            double[] c =
            {
                mesh.NodesX[el[0]], mesh.NodesY[el[0]],
                mesh.NodesX[el[1]], mesh.NodesY[el[1]],
                mesh.NodesX[el[2]], mesh.NodesY[el[2]]
            };
            double area2 = PrandtlTri3.Det(c); // 2A со знаком
            double[] b = { c[3] - c[5], c[5] - c[1], c[1] - c[3] };
            double[] cc = { c[4] - c[2], c[0] - c[4], c[2] - c[0] };
            double dphidx = (b[0] * phi[el[0]] + b[1] * phi[el[1]] + b[2] * phi[el[2]]) / area2;
            double dphidy = (cc[0] * phi[el[0]] + cc[1] * phi[el[1]] + cc[2] * phi[el[2]]) / area2;
            for (int k = 0; k < 3; k++)
            {
                sx[el[k]] += -dphidy;  // τx = −∂φ/∂y
                sy[el[k]] += dphidx;   // τy = ∂φ/∂x
                cnt[el[k]]++;
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
}
