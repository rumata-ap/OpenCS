namespace CScore.Import;

/// <summary>Маппинг усилий SCAD → LoadItem / ShellLoadItem и фильтр форм.</summary>
public static class ScadXlsForceMapper
{
    public static bool IsAcceptedForm(string? form)
    {
        if (string.IsNullOrWhiteSpace(form)) return true;
        return form.Trim().Equals("LS+SD", StringComparison.OrdinalIgnoreCase);
    }

    public static LoadItem MapBar(
        double n, double mk, double my, double qz, double mz, double qy,
        ScadXlsImportOptions opt)
    {
        double f = opt.TonToKnFactor;
        double sign = opt.InvertBarBendingMoments ? -1.0 : 1.0;
        return new LoadItem
        {
            N  = n * f,
            T  = mk * f,
            My = my * f * sign,
            Mx = mz * f * sign,
            Vx = qz * f,
            Vy = qy * f,
        };
    }

    /// <summary>
    /// SCAD: sX,sY,txy — напряжения (Т/м²); Nx=sX·h и т.д. (h в м → Т/м), затем × TonToKnFactor.
    /// Mx,My,Mxy,Qx,Qy — уже погонные (Т·м/м, Т/м), только смена единиц.
    /// </summary>
    public static ShellLoadItem MapShell(
        double sx, double sy, double txy, double mx, double my, double mxy,
        double qx, double qy, double thicknessM, ScadXlsImportOptions opt)
    {
        double f = opt.TonToKnFactor;
        double h = thicknessM;
        return new ShellLoadItem
        {
            Nx  = sx * h * f,
            Ny  = sy * h * f,
            Nxy = txy * h * f,
            Mx  = mx * f,
            My  = my * f,
            Mxy = mxy * f,
            Qx  = qx * f,
            Qy  = qy * f,
        };
    }
}
