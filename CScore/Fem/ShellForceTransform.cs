namespace CScore.Fem;

/// <summary>Преобразование усилий пластинчатого КЭ между системами осей в его плоскости.</summary>
public static class ShellForceTransform
{
    /// <summary>
    /// Усилия строки в осях (X, Y), относительно которых исходная ось x повёрнута на
    /// <paramref name="angleDeg"/> против часовой стрелки (если смотреть с конца оси z). Нормаль общая.
    /// Мембранные усилия, моменты и напряжения поворачиваются как тензоры, поперечные силы — как вектор;
    /// соглашение о знаках моментов не меняется. Метка и номера КЭ/сечения сохраняются.
    /// </summary>
    public static ShellLoadItem Rotate(ShellLoadItem item, double angleDeg)
    {
        ArgumentNullException.ThrowIfNull(item);
        double a = angleDeg * Math.PI / 180.0;
        double c = Math.Cos(a), s = Math.Sin(a);

        var (nx, ny, nxy) = Tensor(item.Nx, item.Ny, item.Nxy, c, s);
        var (mx, my, mxy) = Tensor(item.Mx, item.My, item.Mxy, c, s);
        var result = new ShellLoadItem
        {
            Id = item.Id, Num = item.Num, Label = item.Label,
            Nx = nx, Ny = ny, Nxy = nxy,
            Mx = mx, My = my, Mxy = mxy,
            Qx = c * item.Qx - s * item.Qy,
            Qy = s * item.Qx + c * item.Qy,
            SourceElementNum = item.SourceElementNum,
            SourceSectionNum = item.SourceSectionNum,
        };
        // Напряжения — источник мембранных усилий строки (ShellLoadItem.ResolveN): поворачиваются все три.
        if (item.SigmaX != null || item.SigmaY != null || item.TauXY != null)
        {
            var (sx, sy, txy) = Tensor(item.SigmaX ?? 0, item.SigmaY ?? 0, item.TauXY ?? 0, c, s);
            result.SigmaX = sx; result.SigmaY = sy; result.TauXY = txy;
        }
        return result;
    }

    // X = c·x − s·y, Y = s·x + c·y (x, y — исходные орты): T_XX = X·T·X и т. д.
    static (double Xx, double Yy, double Xy) Tensor(double xx, double yy, double xy, double c, double s) =>
        (c * c * xx - 2 * s * c * xy + s * s * yy,
         s * s * xx + 2 * s * c * xy + c * c * yy,
         s * c * (xx - yy) + (c * c - s * s) * xy);
}
