namespace CSfea.Torsion;

/// <summary>
/// Касательные напряжения от поперечных сил Vx/Vy (формулировка Тимошенко, порт
/// <c>element_stress</c> из sectionproperties/fea.py). Единичные узловые поля (без множителя
/// E·V/Δs — эти величины известны только на уровне конкретной задачи/загружения, не решателя)
/// вычисляются в <see cref="TorsionShearCenterSolver"/> из уже решённых полей ψ,φ; здесь —
/// только их физическая суперпозиция для заданных E, Vx, Vy.
/// </summary>
public static class TorsionShearStressPostprocessor
{
    internal readonly struct UnitFields
    {
        public required double[] VxUnitX { get; init; }
        public required double[] VxUnitY { get; init; }
        public required double[] VyUnitX { get; init; }
        public required double[] VyUnitY { get; init; }
    }

    /// <summary>
    /// Единичные поля τx,τy от Vx (VxUnitX/Y) и от Vy (VyUnitX/Y) в узлах сетки:
    /// VxUnitX = ∂ψ/∂x − ν/2·d1, VxUnitY = ∂ψ/∂y − ν/2·d2 (аналогично для φ и VyUnit*).
    /// </summary>
    internal static UnitFields Compute(TorsionMesh mesh, double[] psi, double[] phi,
        double xc, double yc, double ixx, double iyy, double ixy, double nu)
    {
        var (psiX, psiY) = TorsionPostprocessor.NodeGradient(mesh, psi);
        var (phiX, phiY) = TorsionPostprocessor.NodeGradient(mesh, phi);

        int n = mesh.NodesX.Length;
        var vxUnitX = new double[n]; var vxUnitY = new double[n];
        var vyUnitX = new double[n]; var vyUnitY = new double[n];
        for (int i = 0; i < n; i++)
        {
            double xr = mesh.NodesX[i] - xc, yr = mesh.NodesY[i] - yc;
            var (d1, d2, h1, h2) = WarpingAssembler.ShearParams(xr, yr, ixx, iyy, ixy);
            vxUnitX[i] = psiX[i] - nu / 2.0 * d1;
            vxUnitY[i] = psiY[i] - nu / 2.0 * d2;
            vyUnitX[i] = phiX[i] - nu / 2.0 * h1;
            vyUnitY[i] = phiY[i] - nu / 2.0 * h2;
        }
        return new UnitFields { VxUnitX = vxUnitX, VxUnitY = vxUnitY, VyUnitX = vyUnitX, VyUnitY = vyUnitY };
    }

    /// <summary>
    /// Физическая суперпозиция единичных полей: τx,τy = E/Δs·(Vx·VxUnit + Vy·VyUnit).
    /// Единицы — согласованные с входными (например, E в Па, Vx/Vy в Н, геометрия в м → τ в Па).
    /// </summary>
    public static (double[] tauX, double[] tauY) Combine(
        double[] vxUnitX, double[] vxUnitY, double[] vyUnitX, double[] vyUnitY,
        double deltaS, double e, double vx, double vy)
    {
        int n = vxUnitX.Length;
        var tauX = new double[n]; var tauY = new double[n];
        double kx = e * vx / deltaS, ky = e * vy / deltaS;
        for (int i = 0; i < n; i++)
        {
            tauX[i] = kx * vxUnitX[i] + ky * vyUnitX[i];
            tauY[i] = kx * vxUnitY[i] + ky * vyUnitY[i];
        }
        return (tauX, tauY);
    }
}
