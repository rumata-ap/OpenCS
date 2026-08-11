namespace CScore.PlateStrip;

/// <summary>Локальные (в осях патча) перемещения/повороты узла RVE-патча под заданным
/// ShellStrainState — полиномиальное Kirchhoff-поле (мембранные перемещения линейны, повороты
/// линейны, прогиб при постоянной кривизне квадратичен).</summary>
public readonly record struct RvePatchNodeState(double U, double V, double W, double ThetaX, double ThetaY);

/// <summary>Чистая (без движка) геометрия и кинематика RVE-патча стержневой аналогии полосы
/// плиты. Патч строится прямо в системе координат исходного PlanarRegion (см. Frame-контракт
/// docs/superpowers/specs/2026-08-11-plate-strip-shell-mesh-adapter-design.md) — контур и поле
/// заданы в тех же (u, v) единицах, что и сам регион.</summary>
public static class RvePatchKinematics
{
    /// <summary>Квадратный контур со стороной sizeM вокруг (centerU, centerV), CCW,
    /// в единицах координат региона.</summary>
    public static (double U, double V)[] SquareContourUV(double centerU, double centerV, double sizeM)
    {
        if (!double.IsFinite(centerU) || !double.IsFinite(centerV))
            throw new ArgumentException("Центр RVE-патча должен быть конечным.");
        if (!(sizeM > 0.0) || !double.IsFinite(sizeM))
            throw new ArgumentOutOfRangeException(nameof(sizeM), "Сторона RVE-патча должна быть конечной и положительной.");

        double h = sizeM / 2.0;
        return
        [
            (centerU - h, centerV - h),
            (centerU + h, centerV - h),
            (centerU + h, centerV + h),
            (centerU - h, centerV + h),
        ];
    }

    /// <summary>Локальные перемещения/повороты узла (nodeU, nodeV) под ShellStrainState — см.
    /// вывод формул в спеке (раздел «RVE-патч и Kirchhoff-поле»).</summary>
    public static RvePatchNodeState NodeField(
        ShellStrainState state, double centerU, double centerV, double nodeU, double nodeV)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!double.IsFinite(nodeU) || !double.IsFinite(nodeV))
            throw new ArgumentException("Координаты узла должны быть конечными.");

        double x = nodeU - centerU;
        double y = nodeV - centerV;

        double u = state.Eps0x * x + 0.5 * state.Gamma0xy * y;
        double v = 0.5 * state.Gamma0xy * x + state.Eps0y * y;
        double w = -0.5 * state.Kx * x * x - 0.5 * state.Ky * y * y - 0.5 * state.Kxy * x * y;
        double thetaX = -state.Ky * y - 0.5 * state.Kxy * x;
        double thetaY = state.Kx * x + 0.5 * state.Kxy * y;

        return new RvePatchNodeState(u, v, w, thetaX, thetaY);
    }
}
