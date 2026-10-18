using CScore;
using CScore.Fem;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Слой бетона пластины в состоянии сечения: центр z и толщина слоя (м), деформации середины слоя и напряжения в осях
/// выдачи (σ — МПа), по направлению главного момента — <see cref="EpsDir"/>, <see cref="SigDir"/>; трещина слоя.
/// </summary>
public sealed record FemCsfeaPlateLayer(int Index, double Z, double Dz, double EpsX, double EpsY, double GammaXY,
    double SigX, double SigY, double TauXY, double EpsDir, double SigDir, bool Cracked);

/// <summary>
/// Арматурный слой по направлению в состоянии сечения: номер слоя сечения, направление (в осях сечения —
/// армирования), z (м), площадь (м²/м), ε, σ (МПа, с ψs) и ψs (1 — εs,crc не определена или поправки нет).
/// </summary>
public sealed record FemCsfeaPlateRebar(int Layer, bool AlongX, double Z, double Area, double Eps, double SigmaMpa,
    double Psi, bool HasEpsCrc);

/// <summary>
/// Состояние слоистого сечения пластины в центре КЭ по записанным деформациям (спека НДС сечений, п. 2.3). Усилия —
/// в осях выдачи (Nx, Ny, Nxy, кН/м; Mx, My, Mxy, кН·м/м): по интегралу слоёв и записанные. <see cref="DirectionDeg"/> —
/// направление наибольшего по модулю главного момента от оси x выдачи (против часовой с конца оси z), град.
/// Доли треснувших слоёв нижней (z &lt; 0) и верхней половин и наименьший ψs — восстановленные окном и записанные
/// расчётом (NaN — у КЭ нет записи состояния).
/// </summary>
public sealed record FemCsfeaPlateSectionPoint(
    double[] Strains, double[] Forces, double[] Recorded, double DirectionDeg, double ForceAngleDeg, double Thickness,
    IReadOnlyList<FemCsfeaPlateLayer> Layers, IReadOnlyList<FemCsfeaPlateRebar> Rebar,
    double CrackBottom, double CrackTop, double PsiMin, double RecordedCrackBottom, double RecordedCrackTop,
    double RecordedPsiMin)
{
    /// <summary>Главный момент по направлению <see cref="DirectionDeg"/>, кН·м/м (по записанным усилиям).</summary>
    public double DirectionMoment { get; init; }

    /// <summary>
    /// Расхождение усилий интеграла слоёв с записанными: max(|ΔN|·h, |ΔM|) / max(|N|·h, |M|) по записанным (по всем
    /// компонентам), h — толщина; масштаб снизу — 0,1 кН·м/м.
    /// </summary>
    public double Discrepancy
    {
        get
        {
            double h = Thickness > 0 ? Thickness : 1.0, d = 0, s = 0.1;
            for (int i = 0; i < 6; i++)
            {
                double k = i < 3 ? h : 1.0;
                d = Math.Max(d, Math.Abs(Forces[i] - Recorded[i]) * k);
                s = Math.Max(s, Math.Abs(Recorded[i]) * k);
            }
            return d / s;
        }
    }

    /// <summary>Есть ли треснувшие слои.</summary>
    public bool Cracked => Layers.Any(l => l.Cracked);
}

/// <summary>
/// Слоистое сечение пластины (<see cref="PlateSection"/>, модель «layered») по записанным в полях шага CSfea деформациям
/// центра КЭ (оси выдачи). Память трещин слоёв не записывается — трещины восстанавливаются: по показанным деформациям тем
/// же критерием, что в расчёте (<see cref="PlateSection.UpdateCracks"/>), и по записанным долям треснувших слоёв половин
/// (от грани внутрь); правило «сечение» — все слои. εs,crc для ψs — при соотношении записанных усилий
/// (<see cref="PlateSection.ActivatePsi"/>; в расчёте — при соотношении в момент первой трещины). При монотонном
/// пропорциональном нагружении совпадает с расчётом. Деформации и усилия поворачиваются в оси сечения на угол
/// <c>ForceAngleDeg</c> источника армирования (<see cref="FemRcModelResult.ShellForceAngles"/>), эпюры и усилия окна —
/// в осях выдачи.
/// </summary>
public static class FemCsfeaPlateSectionState
{
    /// <summary>
    /// Состояние в центре пластины с индексом <paramref name="shellIndex"/> полей: нужен блок деформаций
    /// (<see cref="RcSecantStepFields.HasStrains"/>). <paramref name="section"/> — сечение расчёта (клон с работой бетона
    /// на растяжение и ν постановки), только чтение.
    /// </summary>
    public static FemCsfeaPlateSectionPoint Compute(PlateSection section, PlateSectionMaterials materials, bool psi,
        PlateCrackRule rule, double forceAngleDeg, RcSecantStepFields f, int shellIndex)
    {
        if (!f.HasStrains) throw new InvalidOperationException("В полях шага нет деформаций сечений.");
        int so = shellIndex * RcSecantStepFields.ShellStrainComponents;
        var strains = f.ShellStrains.AsSpan(so, RcSecantStepFields.ShellStrainComponents).ToArray();
        int fo = shellIndex * RcSecantStepFields.ShellForceComponents;
        var recorded = Enumerable.Range(0, 6).Select(i => f.ShellForces[fo + i] / (i < 3 ? UnitScale.ShellForce : UnitScale.ShellMoment)).ToArray();
        int to = shellIndex * RcSecantStepFields.ShellStateComponents;
        var state = f.ShellStates.Length >= to + 3 ? f.ShellStates.AsSpan(to, 3).ToArray() : [double.NaN, double.NaN, double.NaN];
        return Compute(section, materials, psi, rule, forceAngleDeg, strains, recorded, state);
    }

    /// <summary>
    /// Состояние по деформациям центра (εx, εy, γxy, κx, κy, κxy в осях выдачи), записанным усилиям в осях выдачи
    /// (кН/м, кН·м/м) и записанному состоянию (доли трещин низа и верха, ψs; NaN — нет).
    /// </summary>
    public static FemCsfeaPlateSectionPoint Compute(PlateSection section, PlateSectionMaterials materials, bool psi,
        PlateCrackRule rule, double forceAngleDeg, double[] strains, double[] recorded, double[] recordedState)
    {
        // Оси выдачи повёрнуты на угол против часовой от осей сечения (RcSecantFieldExtractor).
        var eS = RcSecantFieldExtractor.RotateStrains([strains[0], strains[1], strains[2]], -forceAngleDeg);
        var kS = RcSecantFieldExtractor.RotateStrains([strains[3], strains[4], strains[5]], -forceAngleDeg);
        var s = new ShellStrainState(eS[0], eS[1], eS[2], kS[0], kS[1], kS[2]);
        var c = materials.ConcreteDiagram;

        var layers = PlateLayerState.For(section);
        var upd = section.UpdateCracks(layers, s, c);
        // Память расчёта — доли треснувших слоёв половин: в половине отмечаются слои с наибольшей главной растягивающей
        // деформацией (фронт трещины монотонен по ней) — у КЭ, где после трещины деформация с ψs опустилась ниже
        // ε_bt,ult, критерий по показанным деформациям трещину не видит.
        int n = layers.ConcreteLayerCount, half = n / 2;
        bool marked = false;
        var e1 = Enumerable.Range(0, n).Select(i => section.EvaluateConcreteLayer(i, s, c).Eps1Eq).ToArray();
        foreach (var (from, count, frac) in new[] { (0, half, recordedState[0]), (half, n - half, recordedState[1]) })
        {
            if (!double.IsFinite(frac)) continue;
            foreach (int i in Enumerable.Range(from, count).OrderByDescending(i => e1[i]).Take((int)Math.Round(frac * count)))
            {
                layers.MarkCracked(i);
                marked = true;
            }
        }
        if (rule == PlateCrackRule.Section && layers.CrackedCount > 0)
        {
            for (int i = 0; i < n; i++) layers.MarkCracked(i);
            marked = true;
        }
        if (marked) upd = section.UpdateCracks(layers, s, c);
        if (psi && upd.PendingPsi.Count > 0)
        {
            var sec = ShellForceTransform.Rotate(Item(recorded), forceAngleDeg);
            section.ActivatePsi(layers, upd.PendingPsi, [sec.Nx, sec.Ny, sec.Nxy, sec.Mx, sec.My, sec.Mxy], c, materials.RebarDiagram);
        }

        var r = section.Compute(s, c, materials.RebarDiagram, materials.LayerDiagrams, computeStiffness: false, layerState: layers);
        var outF = ShellForceTransform.Rotate(new ShellLoadItem { Nx = r.Nx, Ny = r.Ny, Nxy = r.Nxy, Mx = r.Mx, My = r.My, Mxy = r.Mxy }, -forceAngleDeg);
        double[] forces = [outF.Nx, outF.Ny, outF.Nxy, outF.Mx, outF.My, outF.Mxy];

        var (dir, mDir) = PrincipalMoment(recorded[3], recorded[4], recorded[5]);
        double a = dir * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a), rot = forceAngleDeg * Math.PI / 180;
        int nl = section.NLayers < 1 ? 1 : section.NLayers;
        var layerList = new List<FemCsfeaPlateLayer>(nl);
        int bottom = 0, top = 0, crBottom = 0, crTop = 0;
        for (int i = 0; i < nl; i++)
        {
            var p = section.EvaluateConcreteLayer(i, s, c, null, layers);
            double ex = strains[0] + p.Z * strains[3], ey = strains[1] + p.Z * strains[4], g = strains[2] + p.Z * strains[5];
            // Главные напряжения слоя — по направлению θ от оси x сечения; в осях выдачи угол θ − угол выдачи.
            double t = p.Theta - rot, ct = Math.Cos(t), st = Math.Sin(t);
            double sx = (p.Sig1 * ct * ct + p.Sig2 * st * st) / 1e3, sy = (p.Sig1 * st * st + p.Sig2 * ct * ct) / 1e3,
                txy = (p.Sig1 - p.Sig2) * st * ct / 1e3;
            layerList.Add(new FemCsfeaPlateLayer(i, p.Z, p.Dz, ex, ey, g, sx, sy, txy,
                ex * ca * ca + ey * sa * sa + g * sa * ca, sx * ca * ca + sy * sa * sa + 2 * txy * sa * ca, p.Cracked));
            if (p.Z < 0) { bottom++; if (p.Cracked) crBottom++; }
            else { top++; if (p.Cracked) crTop++; }
        }

        var rebar = new List<FemCsfeaPlateRebar>();
        double psiMin = 1.0;
        for (int li = 0; li < section.RebarLayers.Count; li++)
            foreach (bool alongX in new[] { true, false })
            {
                var p = section.EvaluateRebar(li, alongX, s, materials.RebarDiagram, materials.LayerDiagrams, layers);
                if (p.Area <= 0.0) continue;
                bool has = layers.HasEpsCrc(li, alongX);
                if (has) psiMin = Math.Min(psiMin, p.Psi);
                rebar.Add(new FemCsfeaPlateRebar(li + 1, alongX, p.Z, p.Area, p.Eps, p.Sig / 1e3, has ? p.Psi : 1.0, has));
            }

        return new FemCsfeaPlateSectionPoint(strains, forces, recorded, dir, forceAngleDeg, section.H, layerList, rebar,
            bottom > 0 ? (double)crBottom / bottom : 0.0, top > 0 ? (double)crTop / top : 0.0, psiMin,
            recordedState[0], recordedState[1], recordedState[2]) { DirectionMoment = mDir };
    }

    /// <summary>
    /// Наибольший по модулю главный момент и его направление от оси x (град, −90…90]: Mα = Mx·c² + My·s² + 2·Mxy·s·c.
    /// </summary>
    public static (double AngleDeg, double Moment) PrincipalMoment(double mx, double my, double mxy)
    {
        double a = 0.5 * Math.Atan2(2 * mxy, mx - my), avg = (mx + my) / 2, rad = Math.Sqrt((mx - my) * (mx - my) / 4 + mxy * mxy);
        double m1 = avg + rad, m2 = avg - rad;
        if (Math.Abs(m2) > Math.Abs(m1)) { a += Math.PI / 2; m1 = m2; }
        double deg = a * 180 / Math.PI;
        if (deg > 90) deg -= 180;
        if (deg <= -90) deg += 180;
        return (deg, m1);
    }

    static ShellLoadItem Item(double[] f) => new() { Nx = f[0], Ny = f[1], Nxy = f[2], Mx = f[3], My = f[4], Mxy = f[5] };
}
