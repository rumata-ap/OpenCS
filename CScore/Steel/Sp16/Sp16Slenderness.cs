namespace CScore.Sp16;

/// <summary>10.4 СП 16: предельные гибкости λ = lef/i ≤ λu (табл. 32 — сжатые, табл. 33 — растянутые; 10.4.2 — группа 4).</summary>
public static class Sp16Slenderness
{
    /// <summary>
    /// Проверка предельной гибкости. Для сжатых элементов α = N/(φARyγc) ≥ 0,5 по наименьшему φ
    /// (для сжато-изгибаемых в соответствующих случаях вместо φ — φe: передаётся <paramref name="phiOverride"/>).
    /// </summary>
    public static List<Sp16CheckResult> Check(Sp16Member m, SteelForces f, double? phiOverride = null)
    {
        var res = new List<Sp16CheckResult>();
        bool tension = f.N > 0;
        double k = m.P.Group4 ? 1.1 : 1.0;
        string groupNote = m.P.Group4 ? "группа 4 (прил. В): предел повышен на 10 % (10.4.2)" : "";
        var axes = m.IsAngle
            ? new List<(string Name, double Lambda)> { ("ось минимальной жёсткости", m.LambdaMinAxis) }
            : [($"ось {m.Axis(true)}", m.Lambda(true)), ($"ось {m.Axis(false)}", m.Lambda(false))];

        if (tension)
        {
            double? lu = Sp16Tables.TensionSlendernessLimit(m.P.TensionCategory, m.P.TensionLoad);
            foreach (var (name, lambda) in axes)
            {
                if (lu == null)
                {
                    res.Add(Sp16CheckResult.NotApplicableFor("10.4.1", "табл. 33", $"Предельная гибкость растянутого элемента ({name})",
                        "Для выбранной позиции табл. 33 и вида нагрузки предельная гибкость не установлена"));
                    continue;
                }
                res.Add(Sp16CheckResult.Limit("10.4.1", "табл. 33", $"Предельная гибкость растянутого элемента ({name})", lambda, lu.Value * k,
                    [("λ", lambda), ("λu", lu.Value * k)],
                    [groupNote, "прим. 1 табл. 33: при отсутствии динамических воздействий достаточно проверки в вертикальных плоскостях"]));
            }
            return res;
        }

        double? phi = phiOverride ?? m.GoverningPhi()?.Phi;
        double alpha = 1;
        string alphaNote = "";
        if (Sp16Tables.CompressionLimitDependsOnAlpha(m.P.CompressionCategory))
        {
            if (phi == null)
            {
                alphaNote = "φ не определён — α принято равным 1";
            }
            else
            {
                alpha = Math.Abs(f.N) / (phi.Value * m.S.A * m.S.Mat.Ry * m.P.GammaC);
                alphaNote = alpha < 0.5 ? "α < 0,5 — принято α = 0,5" : "";
            }
        }
        double luC = Sp16Tables.CompressionSlendernessLimit(m.P.CompressionCategory, alpha) * k;
        foreach (var (name, lambda) in axes)
            res.Add(Sp16CheckResult.Limit("10.4.1", "табл. 32", $"Предельная гибкость сжатого элемента ({name})", lambda, luC,
                [("λ", lambda), ("α", Math.Max(alpha, 0.5)), ("λu", luC)], [alphaNote, groupNote]));
        return res;
    }
}
