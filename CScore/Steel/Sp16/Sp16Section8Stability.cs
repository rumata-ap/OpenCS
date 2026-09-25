namespace CScore.Sp16;

/// <summary>
/// 8.4 СП 16: общая устойчивость изгибаемых элементов сплошного сечения — 8.4.1 (69), (70) с φb по
/// прил. Ж; 8.4.4 а), б) (табл. 11) — случаи, когда проверка не требуется; 8.4.6 (76), (77) — балки
/// 2-го и 3-го классов. Усилия — канонические оси, кН, кН·м. Бимомент не учитывается.
/// </summary>
public static class Sp16Section8Stability
{
    /// <summary>Проверки общей устойчивости; пусто — изгиба в плоскости наибольшей жёсткости нет.</summary>
    public static List<Sp16CheckResult> Check(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S; var p = m.P;
        const string title = "Общая устойчивость балки";
        if (Math.Abs(f.Mx) <= 1e-9) return res;
        if (s.Kind is not (SteelProfileKind.IBeam or SteelProfileKind.Channel or SteelProfileKind.Tee))
        {
            res.Add(Sp16CheckResult.NotApplicableFor("8.4.1", "(69)", title, s.Kind switch
            {
                SteelProfileKind.Generic => "профиль не распознан — задайте профиль вручную",
                SteelProfileKind.Box or SteelProfileKind.Pipe => "замкнутое сечение — расчёт по 8.4 не предусмотрен",
                _ => "8.4 и прил. Ж распространяются на двутавровые, тавровые и швеллерные сечения",
            }));
            return res;
        }
        if (s.Ix <= s.Iy)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("8.4.1", "(69)", title,
                "Ix ≤ Iy — изгиб не в плоскости наибольшей жёсткости, потеря устойчивости плоской формы изгиба не рассматривается (Ж.1)"));
            return res;
        }
        bool topCompressed = f.Mx < 0;
        bool plastic = p.AllowPlastic && Sp16Section8Strength.PlasticNotApplicableReason(m) == null;

        if (p.ContinuousRigidDeck)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("8.4.4", "(69)", title,
                "проверка не требуется по 8.4.4 а): нагрузка передаётся через сплошной жёсткий настил, непрерывно опирающийся на сжатый пояс и связанный с ним"
                + (plastic ? "; для балок 2-го и 3-го классов — по 8.4.6" : "")));
            return res;
        }

        var b = Exemption(m, f, topCompressed, plastic);
        if (plastic)
        {
            if (b.Result != null)
            {
                var r = b.Result;
                var notes846 = new List<string?>(r.Notes)
                {
                    r.Status == CheckStatus.Ok ? null
                        : "устойчивость балки с учётом пластических деформаций не обеспечена; расчёт по (69) для балок 2-го и 3-го классов не предусмотрен — уменьшите lef или выполните расчёт без учёта пластических деформаций",
                };
                res.Add(Sp16CheckResult.Limit("8.4.6", "(76)", "Общая устойчивость балки 2-го/3-го класса: λ̄b ≤ δλ̄ub",
                    r.Applied, r.Allowable, r.Variables.Select(v => (v.Key, v.Value)), notes846));
            }
            else
                res.Add(new Sp16CheckResult
                {
                    Clause = "8.4.6", Formula = "(76)", Description = "Общая устойчивость балки 2-го/3-го класса",
                    Utilization = double.PositiveInfinity, Status = CheckStatus.Fail,
                    Notes = [$"устойчивость обеспечивается только при выполнении 8.4.4 а) или б): {b.Reason}; выполните расчёт без учёта пластических деформаций"],
                });
            return res;
        }

        if (b.Result is { Status: CheckStatus.Ok } ok)
        {
            res.Add(ok);
            return res;
        }

        var phi = Sp16PhiB.Compute(m, topCompressed);
        if (phi.NotApplicable != null)
        {
            res.Add(new Sp16CheckResult
            {
                Clause = "8.4.1", Formula = "(69)", Description = title, Utilization = double.PositiveInfinity,
                Status = CheckStatus.Fail, Notes = ["φb не определён по прил. Ж: " + phi.NotApplicable],
            });
            return res;
        }

        double rg = s.Mat.Ry * p.GammaC;
        double wcx = s.Wx(topCompressed);
        double mx = Math.Abs(f.Mx);
        double term1 = mx / (phi.PhiB * wcx * rg);
        var notes = new List<string?>
        {
            b.Result != null ? $"8.4.4 б) не выполнено: λ̄b = {b.Result.Applied:0.###} > λ̄ub = {b.Result.Allowable:0.###}" : $"8.4.4 б) не применим: {b.Reason}",
            "Mx — наибольший момент на участке lef; опорные сечения закреплены от боковых смещений и поворота",
            s.Kind != SteelProfileKind.IBeam ? "8.4.1 сформулирован для двутавров; для швеллера/тавра φb по прил. Ж, проверка по (69)/(70)" : null,
        };
        notes.AddRange(phi.Notes);
        var vars = new List<(string, double)> { ("Mx", f.Mx) };
        vars.AddRange(phi.Vars);
        vars.AddRange([("φb", phi.PhiB), ("Wcx", wcx), ("Ry", s.Mat.Ry), ("γc", p.GammaC)]);

        if (Math.Abs(f.My) <= 1e-9)
        {
            res.Add(Sp16CheckResult.Of("8.4.1", "(69)", title, term1, vars, mx, phi.PhiB * wcx * rg, notes));
            return res;
        }

        // (70): второй член со знаком «+», если My вызывает сжатие в точке сжатого пояса.
        double worst = double.NegativeInfinity, wcyW = 0, xW = 0;
        foreach (double x in CompressedFlangeTips(s, topCompressed))
        {
            double wcy = s.Iy / Math.Abs(x);
            double sigma = f.My * x / s.Iy;                           // > 0 — растяжение (My = ∫σ·x dA)
            double v = term1 + (-sigma) / rg;
            if (v > worst) { worst = v; wcyW = wcy; xW = x; }
        }
        vars.AddRange([("My", f.My), ("x", xW), ("Wcy", wcyW)]);
        notes.Add("бимомент B не учитывается (вне объёма)");
        res.Add(Sp16CheckResult.Of("8.4.1", "(70)", title + " (изгиб в двух плоскостях)", worst, vars, notes: notes));
        return res;
    }

    /// <summary>Абсциссы (от ц.т.) крайних точек сжатого пояса.</summary>
    static double[] CompressedFlangeTips(Sp16Section s, bool topCompressed)
    {
        if (s.Kind == SteelProfileKind.Channel) return [-s.XLeft, s.XRight];
        double bf = topCompressed ? s.BfTop : s.BfBottom;
        if (bf <= 0) bf = s.Tw;
        return [-bf / 2, bf / 2];
    }

    /// <summary>
    /// 8.4.4 б) с табл. 11 (и 8.4.6 — с множителем δ): результат-ограничение λ̄b ≤ λ̄ub либо причина неприменимости.
    /// Для балок 1-го класса выполненное условие даёт статус Ok (проверка по (69) не требуется).
    /// </summary>
    static (Sp16CheckResult? Result, string? Reason) Exemption(Sp16Member m, SteelForces f, bool topCompressed, bool plastic)
    {
        var s = m.S; var p = m.P;
        if (s.Kind != SteelProfileKind.IBeam) return (null, "8.4.4 б) — только для двутавровых балок");
        if (p.Cantilever) return (null, "табл. 11 не распространяется на консоли");
        double bc = topCompressed ? s.BfTop : s.BfBottom, tc = topCompressed ? s.TfTop : s.TfBottom;
        double bt = topCompressed ? s.BfBottom : s.BfTop, tt = topCompressed ? s.TfBottom : s.TfTop;
        bool symmetric = s.Profile.IsDoublySymmetricIBeam;
        bool compressedMore = tc * Math.Pow(bc, 3) >= tt * Math.Pow(bt, 3);
        if (!symmetric && !compressedMore)
            return (null, "табл. 11 — для симметричных двутавров или с более развитым сжатым поясом" + (plastic ? " (8.4.6: при менее развитом сжатом поясе — только 8.4.4 а)" : ""));
        if (bt / bc < 0.75) return (null, $"bраст/bсж = {bt / bc:0.##} < 0,75");
        double h = s.Profile.H - s.TfTop / 2 - s.TfBottom / 2;
        double hb = h / bc, btRatio = bc / tc;
        if (hb < 1 || hb > 6) return (null, $"h/b = {hb:0.##} вне диапазона табл. 11 (1…6)");
        if (btRatio > 35) return (null, $"b/t = {btRatio:0.#} > 35 — вне диапазона табл. 11");
        double bt15 = Math.Max(15, btRatio);

        double lef = p.LefBOrY;
        double lambdaB = lef / bc * Math.Sqrt(s.Mat.Ry / s.Mat.E);
        bool loadOnTop = topCompressed ? !p.LtbLoadOnTensionFlange : p.LtbLoadOnTensionFlange;
        string formula; double lub;
        if (p.LtbRestraints != LtbRestraints.None || p.LtbLoad == LtbLoadKind.PureBending)
        {
            formula = "(73)"; lub = 0.41 + 0.0032 * bt15 + (0.73 - 0.016 * bt15) / hb;
        }
        else if (loadOnTop)
        {
            formula = "(71)"; lub = 0.35 + 0.0032 * bt15 + (0.76 - 0.02 * bt15) / hb;
        }
        else
        {
            formula = "(72)"; lub = 0.57 + 0.0032 * bt15 + (0.92 - 0.02 * bt15) / hb;
        }
        var vars = new List<(string, double)> { ("lef", lef), ("b", bc), ("t", tc), ("h", h), ("λ̄b", lambdaB), ("λ̄ub " + formula, lub) };
        var notes = new List<string?> { $"табл. 11, формула {formula}", btRatio < 15 ? "b/t < 15 — принято b/t = 15 (прим. 1)" : null };
        if (p.FrictionFlangeJoints) { lub *= 1.2; notes.Add("фрикционные поясные соединения: ×1,2 (прим. 2)"); }
        double wc = s.Wx(topCompressed);
        double sigma = Math.Abs(f.Mx) / (wc * p.GammaC);
        double kSigma = Math.Sqrt(s.Mat.Ry / sigma);
        lub *= kSigma;
        vars.AddRange([("σ", sigma), ("√(Ryf/σ)", kSigma)]);
        notes.Add("прим. 3: λ̄ub × √(Ryf/σ), σ = M/(Wc·γc)");
        string clause = "8.4.4", desc = "Общая устойчивость не требует проверки: λ̄b ≤ λ̄ub (8.4.4 б)";
        if (plastic)
        {
            var e1 = Sp16Tables.TableE1(s)!;
            var (cx, _, _) = Sp16Section8Strength.PlasticC(m, e1);
            double ratio = Math.Abs(f.Mx) / (s.WxMin * s.Mat.Ry * p.GammaC);
            double delta = 1;
            if (ratio > 1 && cx > 1)
            {
                double beta = Sp16Section8Strength.Beta52(m, f);
                double c1x = Math.Min(cx, Math.Max(ratio, beta * cx));                         // (77)
                delta = c1x > 1 ? 1 - 0.6 * (c1x - 1) / (cx - 1) : 1;                           // (76)
                vars.AddRange([("c1x", c1x), ("cx", cx), ("β", beta), ("δ", delta)]);
            }
            else
                notes.Add("σ = M/Wn,min ≤ Ryγc — участок без пластических деформаций, δ = 1");
            lub *= delta;
            clause = "8.4.6";
        }
        vars.Add(("λ̄ub", lub));
        var r = Sp16CheckResult.Limit(clause, "табл. 11", desc, lambdaB, lub, vars, notes);
        return (r, null);
    }
}
