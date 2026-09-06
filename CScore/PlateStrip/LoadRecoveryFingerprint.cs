using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.PlateStrip;

/// <summary>Детерминированный отпечаток входа восстановления нагрузки (идиома
/// EquivalentSectionFingerprint). Покрывает всё, что влияет на результат: целевую эпюру,
/// длину полосы, отпечаток эквивалентного сечения, опорную схему, режим, базис, регуляризацию,
/// веса станций, концевые усилия, равнодействующую и оба допуска — иначе изменившийся расчёт
/// мог бы ошибочно считаться тем же самым.</summary>
public static class LoadRecoveryFingerprint
{
    public static string Compute(
        TargetBeamResultants target,
        double lengthM,
        string sectionFingerprint,
        StripBeamSupportScheme scheme,
        LoadRecoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(options);

        var parts = new List<string>
        {
            $"length:{F(lengthM)}",
            $"section:{sectionFingerprint}",
            $"support:{scheme.StartCondition}:{scheme.EndCondition}:{scheme.AxialRestraint}",
            $"mode:{options.Mode}",
            $"basis:{options.EffectiveBasis}",
            $"alpha:{F(options.EffectiveAlpha)}",
            $"tol-resultant:{F(options.ResultantTolerance)}",
            $"tol-accuracy:{F(options.AccuracyTolerance)}"
        };

        for (int i = 0; i < target.StationFractions.Count; i++)
            parts.Add($"s{i}:{F(target.StationFractions[i])}:" +
                      $"{F(target.N[i])}:{F(target.My[i])}:{F(target.Mz[i])}");

        if (options.StationWeights == null)
            parts.Add("weights:uniform");
        else
            for (int i = 0; i < options.StationWeights.Count; i++)
                parts.Add($"w{i}:{F(options.StationWeights[i])}");

        var end = options.KnownEndActions;
        parts.Add(end == null
            ? "end-actions:none"
            : $"end-actions:{F(end.StartN)}:{F(end.StartMy)}:{F(end.StartMz)}:" +
              $"{F(end.EndN)}:{F(end.EndMy)}:{F(end.EndMz)}");

        var equilibrium = options.EquilibriumTarget;
        parts.Add(equilibrium == null
            ? "equilibrium:none"
            : $"equilibrium:{F(equilibrium.Fx)}:{F(equilibrium.Fy)}:{F(equilibrium.Fz)}:" +
              $"{F(equilibrium.My)}:{F(equilibrium.Mz)}");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))));
    }

    static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
