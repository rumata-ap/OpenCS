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
    /// SCAD: sX,sY,txy — напряжения (сила/длина² в единицах листа); пишутся в SigmaX/SigmaY/TauXY
    /// (кПа = кН/м²), с коррекцией TonToKnFactor и LengthM (если длина листа не метры).
    /// Домножение на толщину — отдельным шагом (ResolveN).
    /// Mx,My,Mxy — «сила×длина/длина», длина сокращается, коррекции по LengthM не требуют.
    /// Qx,Qy — «сила/длина», требуют коррекции на LengthM (как Sigma, но в первой степени).
    /// </summary>
    public static ShellLoadItem MapShell(
        double sx, double sy, double txy, double mx, double my, double mxy,
        double qx, double qy, ScadXlsImportOptions opt)
    {
        double f = opt.TonToKnFactor;
        double lenM = opt.LengthM > 0 ? opt.LengthM : 1.0;
        double sign = opt.InvertShellBendingMoments ? -1.0 : 1.0;
        return new ShellLoadItem
        {
            SigmaX = sx * f / (lenM * lenM),
            SigmaY = sy * f / (lenM * lenM),
            TauXY  = txy * f / (lenM * lenM),
            Mx  = mx * f * sign,
            My  = my * f * sign,
            Mxy = mxy * f * sign,
            Qx  = qx * f / lenM,
            Qy  = qy * f / lenM,
        };
    }
}
