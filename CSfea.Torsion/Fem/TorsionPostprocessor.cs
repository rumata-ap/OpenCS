namespace CSfea.Torsion;

/// <summary>Постпроцессор МКЭ: It, поле τ/(GΘ), τ_max из решения φ. Работает и с T3, и с T6 сеткой.</summary>
public static class TorsionPostprocessor
{
    /// <summary>It = 2·∫_Ω φ dA через аналитический ∫N_i dA (сумма по элементам).</summary>
    public static double ComputeIt(TorsionMesh mesh, double[] phi) => 2.0 * IntegrateField(mesh, phi);

    /// <summary>∫_Ω field dA — точно (через ∫N_i dA), т.к. field линейна/квадратична на элементе,
    /// как и форм-функции (T3/T6), и интеграл суммы совпадает с суммой интегралов по узлам.</summary>
    public static double IntegrateField(TorsionMesh mesh, double[] field)
    {
        double total = 0.0;
        foreach (var el in mesh.Triangles)
        {
            double[] c = BuildCoords(mesh, el);
            double[] m = el.Length == 6 ? PrandtlTri6.MassVector(c) : PrandtlTri3.MassVector(c);
            for (int k = 0; k < el.Length; k++) total += m[k] * field[el[k]];
        }
        return total;
    }

    /// <summary>∫_Ω field² dA — приближённо, 4-точечная квадратура Гаусса (см. <see cref="WarpingAssembler.Quad4"/>),
    /// точная для T3 (field² — квадратичный многочлен), приближённая для T6 (field² — 4-й степени;
    /// сходится с измельчением сетки, как и другие МКЭ-величины этого модуля).</summary>
    public static double IntegrateFieldSquared(TorsionMesh mesh, double[] field)
    {
        double total = 0.0;
        Span<double> n = stackalloc double[6];
        Span<double> dNdL1 = stackalloc double[6];
        Span<double> dNdL2 = stackalloc double[6];
        Span<double> dNdx = stackalloc double[6];
        Span<double> dNdy = stackalloc double[6];

        foreach (var el in mesh.Triangles)
        {
            double[] c = BuildCoords(mesh, el);
            var fEl = new double[el.Length];
            for (int k = 0; k < el.Length; k++) fEl[k] = field[el[k]];

            foreach (var (l1, l2, w) in WarpingAssembler.Quad4)
            {
                double l3 = 1.0 - l1 - l2;
                double detJ;
                if (el.Length == 6)
                {
                    PrandtlTri6.ShapeFunctions(l1, l2, n);
                    PrandtlTri6.ShapeFunctionDerivatives(l1, l2, dNdL1, dNdL2);
                    detJ = PrandtlTri6.Jacobian(c, dNdL1, dNdL2, dNdx, dNdy);
                }
                else
                {
                    n[0] = l1; n[1] = l2; n[2] = l3;
                    detJ = PrandtlTri3.Det(c);
                }
                double weight = w * Math.Abs(detJ);
                double val = 0.0;
                for (int k = 0; k < el.Length; k++) val += n[k] * fEl[k];
                total += weight * val * val;
            }
        }
        return total;
    }

    /// <summary>Градиент узлового поля (T3: постоянный на элементе, CST; T6: линейный внутри
    /// элемента через <see cref="PrandtlTri6.NodeGradient"/>), усреднённый по примыкающим элементам.</summary>
    internal static (double[] gx, double[] gy) NodeGradient(TorsionMesh mesh, double[] field)
    {
        int n = mesh.NodesX.Length;
        var sx = new double[n]; var sy = new double[n]; var cnt = new int[n];
        foreach (var el in mesh.Triangles)
        {
            double[] c = BuildCoords(mesh, el);
            if (el.Length == 6)
            {
                var elField = new double[6];
                for (int k = 0; k < 6; k++) elField[k] = field[el[k]];
                for (int k = 0; k < 6; k++)
                {
                    var (dfdx, dfdy) = PrandtlTri6.NodeGradient(k, c, elField);
                    sx[el[k]] += dfdx;
                    sy[el[k]] += dfdy;
                    cnt[el[k]]++;
                }
            }
            else
            {
                double area2 = PrandtlTri3.Det(c); // 2A со знаком
                double[] b = { c[3] - c[5], c[5] - c[1], c[1] - c[3] };
                double[] cc = { c[4] - c[2], c[0] - c[4], c[2] - c[0] };
                double dfdx = (b[0] * field[el[0]] + b[1] * field[el[1]] + b[2] * field[el[2]]) / area2;
                double dfdy = (cc[0] * field[el[0]] + cc[1] * field[el[1]] + cc[2] * field[el[2]]) / area2;
                for (int k = 0; k < 3; k++)
                {
                    sx[el[k]] += dfdx;
                    sy[el[k]] += dfdy;
                    cnt[el[k]]++;
                }
            }
        }
        var gx = new double[n]; var gy = new double[n];
        for (int i = 0; i < n; i++)
        {
            gx[i] = cnt[i] > 0 ? sx[i] / cnt[i] : 0.0;
            gy[i] = cnt[i] > 0 ? sy[i] / cnt[i] : 0.0;
        }
        return (gx, gy);
    }

    /// <summary>
    /// Касательные напряжения в узлах: τx = −∂φ/∂y, τy = ∂φ/∂x.
    /// T3: производные постоянны на элементе (CST), усредняются по примыкающим элементам.
    /// T6: производные линейны внутри элемента — вычисляются отдельно в каждом узле
    /// (<see cref="PrandtlTri6.NodeGradient"/>), затем усредняются по примыкающим элементам так же.
    /// </summary>
    public static (double[] tauX, double[] tauY) ComputeStresses(TorsionMesh mesh, double[] phi)
    {
        var (dphidx, dphidy) = NodeGradient(mesh, phi);
        int n = dphidx.Length;
        var tauX = new double[n]; var tauY = new double[n];
        for (int i = 0; i < n; i++)
        {
            tauX[i] = -dphidy[i];
            tauY[i] = dphidx[i];
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
