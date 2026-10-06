using CScore.Sp63;

namespace CScore.Fem;

/// <summary>
/// Продольный изгиб стены из плоскости (п. 8.1.15 СП 63) в проверке пластин по КЭ: вертикальная полоса
/// шириной 1 м рассматривается как внецентренно сжатый стержень. N — вертикальное мембранное усилие, M —
/// момент, изгибающий вертикальную полосу; η усиливает только его, остальные усилия КЭ не меняются.
/// i = t/√12, l0 = μ·l, l — высота полосы между перекрытиями (<see cref="FemWallStrip"/>).
/// <para>
/// Буквальный режим: D = kb·Eb·t³/12 + ks·Es·Σ As,v·z² (на 1 м), As,v — арматура вертикального направления.
/// Уточнённый: D = M/κv по слоистой модели (<see cref="ShellStrainSolver"/>) при всех шести усилиях КЭ.
/// </para>
/// </summary>
public static class FemWallEta
{
    /// <summary>Результат для одной строки.</summary>
    /// <param name="Eta">Коэффициент η (1 — поправка не нужна).</param>
    /// <param name="Ncr">Условная критическая сила, кН/м.</param>
    /// <param name="L0">Расчётная длина, м.</param>
    /// <param name="I">Радиус инерции полосы t/√12, м.</param>
    /// <param name="Nv">Вертикальное мембранное усилие, кН/м (сжатие — отрицательное).</param>
    /// <param name="Mv">Момент вертикальной полосы до усиления, кН·м/м.</param>
    /// <param name="Psi">ψ = M1l/M1.</param>
    /// <param name="Stable">false — потеря устойчивости (|Nv| ≥ Ncr).</param>
    public sealed record Outcome(double Eta, double Ncr, double L0, double I, double Nv, double Mv, double Psi, bool Stable)
    {
        /// <summary>Гибкость l0/i.</summary>
        public double Slenderness => L0 / I;
    }

    /// <summary>Усилия в осях «вертикаль (x) — горизонталь (y)».</summary>
    public static ShellLoadItem ToVertical(ShellLoadItem shellOut, FemWallStrip strip) =>
        ShellForceTransform.Rotate(shellOut, -strip.VerticalAngleDeg);

    /// <summary>Крайние отметки z арматуры вертикального направления, м; без неё — грани сечения.</summary>
    /// <param name="verticalInRebarDeg">Угол вертикали от оси x армирования, град.</param>
    public static (double Min, double Max) VerticalRebarZ(PlateSection section, double verticalInRebarDeg)
    {
        double b = verticalInRebarDeg * Math.PI / 180.0;
        bool alongX = Math.Abs(Math.Cos(b)) >= Math.Abs(Math.Sin(b));
        var z = section.RebarLayers.Where(l => (alongX ? l.Asx : l.Asy) > 0).Select(l => alongX ? l.Zsx : l.Zsy).ToList();
        return z.Count > 0 ? (z.Min(), z.Max()) : (-section.H / 2, section.H / 2);
    }

    /// <summary>
    /// Усиливает момент вертикальной полосы строки. Возвращает строку в осях выдачи усилий
    /// (<paramref name="shellOut"/>); при потере устойчивости — исходную.
    /// </summary>
    /// <param name="shellOut">Усилия КЭ в осях выдачи.</param>
    /// <param name="rebarAngleDeg">Угол оси X выдачи в осях армирования сечения (ForceAngleDeg), град.</param>
    /// <param name="psi">ψ = M1l/M1 для φl.</param>
    public static (ShellLoadItem Amplified, Outcome Result) Apply(
        ShellLoadItem shellOut, FemWallStrip strip, FemEtaParams eta, double psi,
        PlateSection section, Material concrete, Material rebar, CalcType calcType, double rebarAngleDeg)
    {
        var v = ToVertical(shellOut, strip);
        double t = section.H;
        double i = t / Math.Sqrt(12.0);
        double l0 = eta.MuX * strip.HeightM;
        double threshold = eta.SlendernessThreshold ?? EccentricityAmplifier.SlendernessThreshold;
        // Вертикаль в осях армирования: ось X выдачи там под углом rebarAngleDeg, вертикаль от неё — VerticalAngleDeg.
        double beta = strip.VerticalAngleDeg + rebarAngleDeg;

        EccentricityAmplifier.EtaResult r;
        if (!eta.Iterative)
        {
            double eb = ModulusOf(concrete, calcType);
            double es = ModulusOf(rebar, calcType);
            double b = beta * Math.PI / 180.0;
            double c2 = Math.Cos(b) * Math.Cos(b), s2 = Math.Sin(b) * Math.Sin(b);
            double is_ = section.RebarLayers.Sum(l => l.Asx * c2 * l.Zsx * l.Zsx + l.Asy * s2 * l.Zsy * l.Zsy);
            r = EccentricityAmplifier.AmplifyFormula(v.Nx, v.Mx, l0, h: t, i: i,
                eiConcrete: eb * t * t * t / 12.0, eiRebar: es * is_, psi: psi, slendernessThreshold: threshold);
        }
        else
        {
            var solver = Solver(section, concrete, rebar, calcType);
            double b = beta * Math.PI / 180.0;
            double c = Math.Cos(b), s = Math.Sin(b);
            r = EccentricityAmplifier.AmplifyIterative(v.Nx, v.Mx, l0, h: t, i: i, solveCurvature: m =>
            {
                var trial = ShellForceTransform.Rotate(WithMx(v, m), beta);
                var res = solver.Solve([trial.Nx, trial.Ny, trial.Nxy, trial.Mx, trial.My, trial.Mxy]);
                var st = res.StrainState;
                return st.Kx * c * c + st.Ky * s * s + st.Kxy * s * c;
            }, passes: 3, slendernessThreshold: threshold);
        }

        var outcome = new Outcome(r.Eta, r.Ncr, l0, i, v.Nx, v.Mx, psi, r.Stable);
        if (!r.Stable || r.Eta == 1.0) return (shellOut, outcome);
        return (ShellForceTransform.Rotate(WithMx(v, r.MEff), strip.VerticalAngleDeg), outcome);
    }

    static ShellLoadItem WithMx(ShellLoadItem v, double mx) => new()
    {
        Id = v.Id, Num = v.Num, Label = v.Label,
        Nx = v.Nx, Ny = v.Ny, Nxy = v.Nxy, Mx = mx, My = v.My, Mxy = v.Mxy, Qx = v.Qx, Qy = v.Qy,
        SourceElementNum = v.SourceElementNum, SourceSectionNum = v.SourceSectionNum,
    };

    static double ModulusOf(Material m, CalcType calcType) =>
        m.chars.TryGetValue(calcType, out var ch) && ch is { E: > 0 } ? ch.E : m.E;

    /// <summary>Решатель НДС слоистой модели с диаграммами проверки (как прочность слоистой модели).</summary>
    static ShellStrainSolver Solver(PlateSection section, Material concrete, Material rebar, CalcType calcType)
    {
        var cDiag = concrete.GetDiagramms(section.ConcreteDiagramType)?[calcType]
            ?? concrete.GetDiagramms(DiagrammType.L3)?[calcType]
            ?? throw new InvalidOperationException("Диаграмма бетона не построена (η стены)");
        var rDiag = rebar.GetDiagramms(DiagrammCompatibility.Coerce(rebar.Type, DiagrammType.L2))?[calcType]
            ?? throw new InvalidOperationException("Диаграмма арматуры не построена (η стены)");
        return new ShellStrainSolver(section, cDiag, rDiag);
    }
}
