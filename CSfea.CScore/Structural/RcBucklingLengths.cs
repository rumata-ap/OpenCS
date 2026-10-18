using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>Происхождение расчётной длины элемента в плоскости (<see cref="RcBucklingLengths"/>).</summary>
public enum RcBucklingLengthSource
{
    /// <summary>Элемент участвует в форме: N_cr = λ_k·N.</summary>
    Mode,

    /// <summary>
    /// Элемент в наинизшей форме с участием поворачивается почти как жёсткое тело (U ≪ λ·W — стойка-качалка): его держат
    /// другие, сам он теряет устойчивость только по форме без смещения — до локальной задачи μ = 1.
    /// </summary>
    Rocking,

    /// <summary>Элемент не участвует ни в одной из найденных форм — нужна локальная задача (срез 3), μ не назначен.</summary>
    NotInvolved,
}

/// <summary>
/// Расчётная длина элемента в плоскости изгиба: μ = π/l·√(EI/N_cr) (NaN — не назначена), N_cr, Н, EI, Н·м² (сечение
/// наиболее сжатого подэлемента). Для <see cref="RcBucklingLengthSource.Mode"/> и
/// <see cref="RcBucklingLengthSource.Rocking"/> — форма <paramref name="Mode"/> (индекс в
/// <see cref="RcBucklingResult.Lambda"/>), её λ, доля работы элемента в форме <paramref name="Share"/> (от работы всех
/// сжатых КЭ), участие <paramref name="Participation"/> (средний квадрат угла поворота оси относительно наибольшего по
/// элементам формы) и ρ = U/W.
/// </summary>
public sealed record RcBucklingPlaneLength(RcBucklingLengthSource Source, double Mu, double Ncr, double EI,
    int Mode = -1, double Lambda = double.NaN, double Share = 0, double Participation = 0, double Rho = double.NaN);

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
/// формы (меньше — форма пластин, стержням не назначается).
/// </summary>
public sealed record RcBucklingLengthOptions(double Participation = 0.1, double Rocking = 0.01, double BeamShare = 0.05);

/// <summary>
/// Энергетический разбор форм и назначение расчётных длин (спека 2026-10-18, пп. 3В–3Г; обобщение этажного подхода
/// ASCE на произвольную схему). Для элемента i, формы k, плоскости p: U — энергия изгиба, W — работа продольной силы
/// (<see cref="StructuralMesh.BeamEnergy"/>), Σ U = λ_k·Σ W. Участие — средний квадрат угла поворота оси
/// θ² = W/Σ(|N|·l) элемента относительно наибольшего по элементам <c>units</c> в форме: не зависит от числа стержней
/// (в этажной форме рамы все стойки участвуют одинаково) и от нормировки формы. Наинизшая форма, в которой участие не меньше порога, —
/// форма элемента в плоскости: N_cr = λ_k·|N|; если в ней ρ = U/W &lt; порог·λ_k — элемент качается (μ = 1 до
/// локальной задачи). Не участвует ни в одной форме — μ не назначается.
/// </summary>
public static class RcBucklingLengths
{
    /// <param name="result">Задача устойчивости по сочетанию.</param>
    /// <param name="units">Расчётные элементы — номера стержней исходной модели.</param>
    /// <returns>По элементу в порядке <paramref name="units"/>; элемент без сжатия — null.</returns>
    public static RcBucklingUnitLength?[] Assign(RcBucklingResult result, IReadOnlyList<IReadOnlyList<int>> units,
        RcBucklingLengthOptions? options = null)
    {
        options ??= new RcBucklingLengthOptions();
        var mesh = result.Build.Mesh;
        int nb = mesh.Beams.Count, nm = result.Modes.Length;
        var axial = new double[nb];
        var length = new double[nb];
        for (int b = 0; b < nb; b++)
        {
            axial[b] = mesh.BeamAxialForce(b, result.URef);
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

            RcBucklingPlaneLength Plane(bool xy, double ei)
            {
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
                        return new(RcBucklingLengthSource.Rocking, 1.0, Math.PI * Math.PI * ei / (l * l), ei,
                            k, lambda, w / total, participation, rho);
                    double ncr = -lambda * n;
                    return new(RcBucklingLengthSource.Mode, Math.PI / l * Math.Sqrt(ei / ncr), ncr, ei,
                        k, lambda, w / total, participation, rho);
                }
                return new(RcBucklingLengthSource.NotInvolved, double.NaN, double.NaN, ei);
            }
            lengths[i] = new RcBucklingUnitLength(l, n, Plane(true, eIz), Plane(false, eIy));
        }
        return lengths;
    }

    static double Sq(double x) => x * x;

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
