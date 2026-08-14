namespace CSfea.Torsion;

/// <summary>
/// Касательные напряжения от поперечных сил Vx/Vy (формулировка Тимошенко, порт
/// <c>element_stress</c> из sectionproperties/fea.py). Единичные узловые поля (без множителя
/// V/Δs — величина V известна только на уровне конкретной задачи/загружения, не решателя)
/// вычисляются в <see cref="TorsionShearCenterSolver"/> из уже решённых полей ψ,φ; здесь —
/// только их физическая суперпозиция для заданных Vx, Vy.
///
/// В исходной формуле sectionproperties (<c>sig_zx_vx = E·Vx/Δs·(...)</c>) присутствует явный
/// множитель E, но там Δs и ψ,φ — модуль-взвешенные величины (∝E¹ и ∝E² соответственно для
/// однородного материала), тогда как наши Δs,ψ,φ вычислены как чисто геометрические (неявно
/// E≡1). При подстановке E сокращается полностью: τ от силовой (не деформационной) упругой
/// задачи для однородного сечения не зависит от E — как и σ от N/M (простая механика материалов)
/// и τ от кручения T (уже было верно реализовано без E). Отсюда — никакого параметра E здесь нет.
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
    /// Физическая суперпозиция единичных полей: τx,τy = (Vx·VxUnit + Vy·VyUnit)/Δs.
    /// Единицы — согласованные с входными (например, Vx/Vy в Н, геометрия в м → τ в Па).
    /// Без множителя E — см. обоснование в доке класса.
    /// </summary>
    public static (double[] tauX, double[] tauY) Combine(
        double[] vxUnitX, double[] vxUnitY, double[] vyUnitX, double[] vyUnitY,
        double deltaS, double vx, double vy)
    {
        int n = vxUnitX.Length;
        var tauX = new double[n]; var tauY = new double[n];
        double kx = vx / deltaS, ky = vy / deltaS;
        for (int i = 0; i < n; i++)
        {
            tauX[i] = kx * vxUnitX[i] + ky * vyUnitX[i];
            tauY[i] = kx * vxUnitY[i] + ky * vyUnitY[i];
        }
        return (tauX, tauY);
    }
}
