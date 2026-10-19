using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>Происхождение расчётной длины элемента в плоскости (<see cref="RcBucklingLengths"/>).</summary>
public enum RcBucklingLengthSource
{
    /// <summary>Элемент участвует в форме: N_cr = λ_k·N.</summary>
    Mode,

    /// <summary>
    /// Элемент в наинизшей форме с участием поворачивается почти как жёсткое тело (U ≪ λ·W — стойка-качалка): его держат
    /// другие, сам он теряет устойчивость только по форме без смещения — N_cr локальной задачи без смещения (без неё
    /// μ = 1).
    /// </summary>
    Rocking,

    /// <summary>
    /// N_cr — по локальной задаче: элемент не участвует в найденных формах (его концы держит остальная схема при
    /// фактическом сочетании) или участвует, но локальная форма без смещения ниже, чем λ_k·N.
    /// </summary>
    Local,

    /// <summary>Элемент не участвует ни в одной из найденных форм, локальной формы в плоскости нет — μ не назначен.</summary>
    NotInvolved,

    /// <summary>
    /// μ — по номограмме ASCE или табл. 31 СП 16 (<see cref="RcNomogramLengths"/>): подробности — в
    /// <see cref="RcBucklingPlaneLength.Nomogram"/>, назначение по формам — справочно в <see cref="RcBucklingPlaneLength.Form"/>.
    /// </summary>
    Nomogram,
}

/// <summary>
/// Расчётная длина элемента в плоскости изгиба: μ = π/l·√(EI/N_cr) (NaN — не назначена), N_cr, Н, EI, Н·м² (сечение
/// наиболее сжатого подэлемента). Для <see cref="RcBucklingLengthSource.Mode"/> и
/// <see cref="RcBucklingLengthSource.Rocking"/> — форма <paramref name="Mode"/> (индекс в
/// <see cref="RcBucklingResult.Lambda"/>), её λ, доля работы элемента в форме <paramref name="Share"/> (от работы всех
/// сжатых КЭ), участие <paramref name="Participation"/> (средний квадрат угла поворота оси относительно наибольшего по
/// элементам формы) и ρ = U/W (у <see cref="RcBucklingLengthSource.Local"/> — форма, в которой элемент участвует, или
/// −1). <paramref name="LocalAlpha"/>, <paramref name="LocalNcr"/> — локальная задача в плоскости (NaN — не решалась или
/// формы в плоскости нет): без смещения (<paramref name="LocalBraced"/>) — у участников и качающихся, с остальной
/// схемой — у неучастников. <paramref name="Equivalent"/> — эквивалентная стойка с пружинами из формы, по которой
/// назначена N_cr (общей при λ или локальной при α; у качания без локальной задачи — null);
/// <paramref name="Surround"/> — справочно, стойка с жёсткостями остальной схемы при фактическом сочетании (диагональ
/// сведённой матрицы, <see cref="StructuralLocalBuckling.Surround"/>), её N_cr = EI·u²/l². У
/// <see cref="RcBucklingLengthSource.Nomogram"/> — <paramref name="Nomogram"/> (G концов, смещение, формула) и
/// <paramref name="Form"/> — назначение по формам того же элемента (справочно).
/// </summary>
public sealed record RcBucklingPlaneLength(RcBucklingLengthSource Source, double Mu, double Ncr, double EI,
    int Mode = -1, double Lambda = double.NaN, double Share = 0, double Participation = 0, double Rho = double.NaN,
    double LocalAlpha = double.NaN, double LocalNcr = double.NaN, bool LocalBraced = false,
    RcEquivalentColumn? Equivalent = null, RcEquivalentColumn? Surround = null,
    RcNomogramPlane? Nomogram = null, RcBucklingPlaneLength? Form = null);

/// <summary>
/// Расчётные длины элемента по сочетанию: длина <paramref name="Length"/>, м; <paramref name="N"/> — наибольшее сжатие
/// подэлементов (минус); плоскости местных осей: <paramref name="AboutZ"/> — изгиб вокруг z (EIz, плоскость xy),
/// <paramref name="AboutY"/> — вокруг y (EIy, плоскость xz).
/// </summary>
public sealed record RcBucklingUnitLength(double Length, double N, RcBucklingPlaneLength AboutZ, RcBucklingPlaneLength AboutY);

/// <summary>
/// Пороги энергетического разбора форм: <paramref name="Participation"/> — наименьшее участие элемента в форме
/// (средний квадрат угла поворота оси элемента относительно наибольшего по элементам формы); <paramref name="Rocking"/>
/// — ρ/λ, ниже которого элемент качается; <paramref name="BeamShare"/> — наименьшая доля работы стержней в работе
/// формы (меньше — форма пластин, стержням не назначается); <paramref name="LocalPlane"/> — наименьшая доля работы
/// плоскости в работе локальной формы, чтобы считать форму формой этой плоскости.
/// </summary>
public sealed record RcBucklingLengthOptions(double Participation = 0.1, double Rocking = 0.01, double BeamShare = 0.05,
    double LocalPlane = 0.5);

/// <summary>
/// Энергетический разбор форм и назначение расчётных длин (спека 2026-10-18, пп. 3В–3Г; обобщение этажного подхода
/// ASCE на произвольную схему). Для элемента i, формы k, плоскости p: U — энергия изгиба, W — работа продольной силы
/// (<see cref="StructuralMesh.BeamEnergy"/>), Σ U = λ_k·Σ W. Участие — средний квадрат угла поворота оси
/// θ² = W/Σ(|N|·l) элемента относительно наибольшего по элементам <c>units</c> в форме: не зависит от числа стержней
/// (в этажной форме рамы все стойки участвуют одинаково) и от нормировки формы. Наинизшая форма, в которой участие не меньше порога, —
/// форма элемента в плоскости: N_cr = min(λ_k·|N|, N_cr,лок без смещения) — минимум ловит форму без смещения слабой
/// перегруженной стойки; если в ней ρ = U/W &lt; порог·λ_k — элемент качается: N_cr,лок без смещения. Не участвует ни
/// в одной форме — N_cr,лок с остальной схемой при фактическом сочетании (Бритвин). N_cr,лок = α·|N| — наинизшая
/// локальная форма, в которой на плоскость приходится не меньше <see cref="RcBucklingLengthOptions.LocalPlane"/> работы
/// (<see cref="RcBucklingAnalysis.SolveLocal"/>); без локальных задач — μ = 1 у качающихся, μ не назначается у
/// неучастников.
/// </summary>
public static class RcBucklingLengths
{
    /// <param name="result">Задача устойчивости по сочетанию.</param>
    /// <param name="units">Расчётные элементы — номера стержней исходной модели.</param>
    /// <param name="local">Локальные задачи элементов (<see cref="RcBucklingAnalysis.SolveLocal"/>), null — нет.</param>
    /// <returns>По элементу в порядке <paramref name="units"/>; элемент без сжатия — null.</returns>
    public static RcBucklingUnitLength?[] Assign(RcBucklingResult result, IReadOnlyList<IReadOnlyList<int>> units,
        RcBucklingLengthOptions? options = null, IReadOnlyList<StructuralLocalBuckling?>? local = null)
    {
        options ??= new RcBucklingLengthOptions();
        var mesh = result.Build.Mesh;
        int nb = mesh.Beams.Count, nm = result.Modes.Length;
        var axial = new double[nb];
        var length = new double[nb];
        for (int b = 0; b < nb; b++)
        {
            axial[b] = result.Axial(b);
            var c = mesh.BeamCoords(b);
            length[b] = Math.Sqrt(Sq(c[1][0] - c[0][0]) + Sq(c[1][1] - c[0][1]) + Sq(c[1][2] - c[0][2]));
        }

        // Энергии сжатых стержней по формам; работа сжатых оболочек — для доли стержней в работе формы.
        var energy = new BeamModeEnergy?[nm][];
        var workBeams = new double[nm];
        for (int k = 0; k < nm; k++)
        {
            var mode = result.Modes[k];
            var e = new BeamModeEnergy?[nb];
            Parallel.For(0, nb, b => { if (axial[b] < 0) e[b] = mesh.BeamEnergy(b, mode, axial[b]); });
            energy[k] = e;
            foreach (var eb in e) if (eb != null) workBeams[k] += eb.Wxy + eb.Wxz;
        }
        var workShells = ShellWork(mesh, result);

        // Энергии элементов (сумма по подэлементам) и средний квадрат угла поворота θ² = W/Σ|N|·l по плоскостям.
        var subs = units.Select(u => u.SelectMany(id => result.SubBeams[id]).ToArray()).ToArray();
        var unitEnergy = new BeamModeEnergy[units.Count][];
        var nl = new double[units.Count];
        var thetaMax = new double[nm];
        for (int i = 0; i < units.Count; i++)
        {
            nl[i] = subs[i].Where(b => axial[b] < 0).Sum(b => -axial[b] * length[b]);
            unitEnergy[i] = new BeamModeEnergy[nm];
            for (int k = 0; k < nm; k++)
            {
                double uxy = 0, wxy = 0, uxz = 0, wxz = 0;
                foreach (int b in subs[i])
                    if (energy[k][b] is { } eb) { uxy += eb.Uxy; wxy += eb.Wxy; uxz += eb.Uxz; wxz += eb.Wxz; }
                unitEnergy[i][k] = new BeamModeEnergy(uxy, wxy, uxz, wxz);
                if (nl[i] > 0) thetaMax[k] = Math.Max(thetaMax[k], Math.Max(wxy, wxz) / nl[i]);
            }
        }

        var lengths = new RcBucklingUnitLength?[units.Count];
        for (int i = 0; i < units.Count; i++)
        {
            var sub = subs[i];
            double l = sub.Sum(b => length[b]);
            int governing = sub.MinBy(b => axial[b]);
            double n = axial[governing];
            if (!(n < 0)) continue;
            var (_, eIy, eIz, _) = BeamElements.SectionStiffness(mesh.Beams[governing].Section);

            var loc = local?[i];
            // Наинизшая локальная форма плоскости: α, N_cr = α·|N|, закрепления концов в ней.
            (double Alpha, double Ncr, PlaneRestraint? Restraint) Local(StructuralLocalModes? modes, bool xy)
            {
                if (modes == null) return (double.NaN, double.NaN, null);
                for (int m = 0; m < modes.Alpha.Length; m++)
                {
                    var e = modes.Energy[m];
                    double w = xy ? e.Wxy : e.Wxz, total = e.Wxy + e.Wxz;
                    if (total > 0 && w >= options.LocalPlane * total)
                        return (modes.Alpha[m], -modes.Alpha[m] * n, PlaneOf(modes.Restraints[m], xy));
                }
                return (double.NaN, double.NaN, null);
            }
            double Mu(double ei, double ncr) => Math.PI / l * Math.Sqrt(ei / ncr);
            // Эквивалентная стойка — корень около найденной N_cr.
            RcEquivalentColumn? Equivalent(PlaneRestraint? r, double ei, double ncr) =>
                r == null ? null : RcRestrainedColumn.Solve(r, ei, l, l * Math.Sqrt(ncr / ei));

            RcBucklingPlaneLength Plane(bool xy, double ei)
            {
                var plane = PlaneCore(xy, ei);
                var around = PlaneOf(loc?.Surround, xy);
                return around == null ? plane : plane with { Surround = RcRestrainedColumn.Solve(around, ei, l) };
            }

            RcBucklingPlaneLength PlaneCore(bool xy, double ei)
            {
                var (ab, nb, rb) = Local(loc?.Braced, xy);
                for (int k = 0; k < nm; k++)
                {
                    double total = workBeams[k] + workShells[k];
                    if (!(total > 0) || workBeams[k] < options.BeamShare * total) continue;
                    var ue = unitEnergy[i][k];
                    double u = xy ? ue.Uxy : ue.Uxz, w = xy ? ue.Wxy : ue.Wxz;
                    if (!(w > 0)) continue;
                    double participation = w / nl[i] / thetaMax[k];
                    if (participation < options.Participation) continue;
                    double lambda = result.Lambda[k], rho = u / w;
                    if (rho < options.Rocking * lambda)
                    {
                        double nr = double.IsNaN(nb) ? Math.PI * Math.PI * ei / (l * l) : nb;
                        return new(RcBucklingLengthSource.Rocking, Mu(ei, nr), nr, ei,
                            k, lambda, w / total, participation, rho, ab, nb, !double.IsNaN(nb), Equivalent(rb, ei, nr));
                    }
                    double ncr = -lambda * n;
                    // Та же форма в обеих задачах (стойка, закреплённая от смещения схемой) различается округлением.
                    bool braced = nb < (1 - LocalTolerance) * ncr;
                    var source = braced ? RcBucklingLengthSource.Local : RcBucklingLengthSource.Mode;
                    if (braced) ncr = nb;
                    var restraint = braced ? rb : PlaneOf(mesh.GroupRestraints(sub, result.URef, result.Modes[k], lambda, result.BeamAxial), xy);
                    return new(source, Mu(ei, ncr), ncr, ei, k, lambda, w / total, participation, rho, ab, nb,
                        !double.IsNaN(nb), Equivalent(restraint, ei, ncr));
                }
                var (asw, nsw, rsw) = Local(loc?.Sway, xy);
                return double.IsNaN(nsw)
                    ? new(RcBucklingLengthSource.NotInvolved, double.NaN, double.NaN, ei)
                    : new(RcBucklingLengthSource.Local, Mu(ei, nsw), nsw, ei, LocalAlpha: asw, LocalNcr: nsw,
                        Equivalent: Equivalent(rsw, ei, nsw));
            }
            lengths[i] = new RcBucklingUnitLength(l, n, Plane(true, eIz), Plane(false, eIy));
        }
        return lengths;
    }

    // Относительный запас, с которым N_cr локальной задачи без смещения должна быть ниже λ·N, чтобы заменить её.
    const double LocalTolerance = 1e-6;

    static double Sq(double x) => x * x;

    static PlaneRestraint? PlaneOf(BeamGroupRestraints? r, bool xy) => xy ? r?.Xy : r?.Xz;

    // Работа сжатых оболочек по формам: ½·φᵀ·(−K_σ)·φ по КЭ, положительные части (K_σ КЭ — один раз на все формы).
    static double[] ShellWork(StructuralMesh mesh, RcBucklingResult result)
    {
        int nm = result.Modes.Length;
        var work = new double[nm];
        if (mesh.Shells.Count == 0 || nm == 0) return work;
        var gate = new object();
        Parallel.For(0, mesh.Shells.Count, () => new double[nm], (e, _, local) =>
        {
            var kg = mesh.ElementKGeometric(e, result.URef);
            var dofs = mesh.ElementDofs(e);
            for (int k = 0; k < nm; k++)
            {
                var phi = result.Modes[k];
                double s = 0;
                for (int p = 0; p < dofs.Length; p++)
                {
                    double row = 0;
                    for (int q = 0; q < dofs.Length; q++) row += kg[p, q] * phi[dofs[q]];
                    s += phi[dofs[p]] * row;
                }
                if (s < 0) local[k] -= 0.5 * s;
            }
            return local;
        }, local => { lock (gate) for (int k = 0; k < nm; k++) work[k] += local[k]; });
        return work;
    }
}
