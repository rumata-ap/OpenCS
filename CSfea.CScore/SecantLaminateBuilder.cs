using CScore;
using CSfea.Core;

namespace CSfea.CScoreBridge;

/// <summary>Секущая ABD слоистого сечения пластины в единицах CScore: A — кН/м, B — кН,
/// D — кН·м (на 1 м ширины, деформации безразмерные, кривизны 1/м).</summary>
public sealed record SecantAbd(double[,] A, double[,] B, double[,] D);

/// <summary>
/// Секущая ABD сечения <see cref="PlateSection"/> (слоистая модель) для деформаций (ε, κ):
/// N = A·ε + B·κ, M = B·ε + D·κ воспроизводят усилия <see cref="PlateSection.Compute"/> с тем же
/// состоянием слоёв, ν и ψs. Слой бетона — матрица Q из закона слоя в главных осях (σ₁, σ₂ по
/// диаграмме, ν до трещины, секущий G₁₂ из соосности вращающейся трещины), повёрнутая на угол
/// θ главной оси; арматура — одноосная секущая E = σs/εs по своему направлению. Интегрирование —
/// по тем же z, что и в CScore (центры слоёв бетона, координаты арматуры), не «стопкой» слоёв.
/// Матрица симметрична и положительно полуопределена (секущие модули неотрицательны).
/// </summary>
public static class SecantLaminateBuilder
{
    /// <summary>
    /// Секущая ABD в единицах CScore. <paramref name="zeroStrainBand"/> δ &gt; 0 — регуляризация бетона без растяжения
    /// (слой с трещиной или растяжение выключено) у нуля: при 0 &lt; ε_eq &lt; δ секущий модуль спадает линейно от E₀ до 0,
    /// а не скачком. Иначе у слоя с ε ≈ 0 модуль «мигает» между E₀ и 0 от знака ε, и итерации секущих не сходятся
    /// (поперечное направление полосы, почти недеформированное направление плиты). Цена — напряжение по секущей в
    /// полосе отличается от закона (σ = 0) не более чем на E₀·δ/4; вне полосы матрица точна. δ = 0 — без регуляризации.
    /// </summary>
    public static SecantAbd Build(PlateSection section, ShellStrainState s, Diagramm cDiag, Diagramm rDiag,
        IReadOnlyList<Diagramm?>? layerDiags = null, PlateLayerState? layerState = null,
        bool? tensionOverride = null, double zeroStrainBand = 0.0)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(s);
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        const double probe = 1e-9;
        double e0 = zeroStrainBand > 0.0 ? -cDiag.Sig(-probe, out _, tenB: false) / probe : 0.0;

        int nl = section.NLayers < 1 ? 1 : section.NLayers;
        for (int i = 0; i < nl; i++)
        {
            var p = section.EvaluateConcreteLayer(i, s, cDiag, tensionOverride, layerState);
            var (q11, q12, q22, g12) = zeroStrainBand > 0.0
                ? Regularized(p, zeroStrainBand, e0)
                : (p.Q11, p.Q12, p.Q22, p.G12);
            var q = RotatedLayerMatrix(q11, q12, q22, g12, p.Theta);
            Accumulate(a, b, d, q, p.Dz, p.Z);
        }

        for (int li = 0; li < section.RebarLayers.Count; li++)
            foreach (bool alongX in new[] { true, false })
            {
                var r = section.EvaluateRebar(li, alongX, s, rDiag, layerDiags, layerState);
                if (r.Area <= 0.0) continue;
                int k = alongX ? 0 : 1;
                double ea = r.ESecant * r.Area;
                a[k, k] += ea;
                b[k, k] += ea * r.Z;
                d[k, k] += ea * r.Z * r.Z;
            }

        return new SecantAbd(a, b, d);
    }

    /// <summary>Секущая касательная в единицах CSfea (Н, м) с упругой As сечения (решение спеки:
    /// поперечный сдвиг не меняется).</summary>
    public static ShellTangent BuildTangent(PlateSection section, ShellStrainState s, PlateSectionMaterials materials,
        PlateLayerState? layerState = null, bool? tensionOverride = null, double zeroStrainBand = 0.0)
    {
        ArgumentNullException.ThrowIfNull(materials);
        var abd = Build(section, s, materials.ConcreteDiagram, materials.RebarDiagram, materials.LayerDiagrams,
            layerState, tensionOverride, zeroStrainBand);
        var asMat = materials.AsOverride ?? section.BuildAs(materials.ConcreteE_MPa, materials.Nu, materials.KShear);
        return ToCsfea(abd, asMat);
    }

    /// <summary>Перевод ABD и As из единиц CScore в единицы CSfea.</summary>
    public static ShellTangent ToCsfea(SecantAbd abd, double[,] asCScore)
        => new(Scale(abd.A, UnitScale.ShellForce), Scale(abd.B, UnitScale.ShellForce),
               Scale(abd.D, UnitScale.ShellMoment), Scale(asCScore, UnitScale.ShellForce));

    /// <summary>
    /// Матрица слоя в осях x–y: Q̄ = Tᵀ·Q·T, где Q = [[Q11, Q12, 0], [Q12, Q22, 0], [0, 0, G12]]
    /// на (ε₁, ε₂, γ₁₂), T — поворот (εx, εy, γxy) → (ε₁, ε₂, γ₁₂) на угол θ главной оси 1 к x
    /// (γ — инженерные). Напряжения σ = Q̄·ε совпадают с поворотом σ₁, σ₂ в CScore.
    /// </summary>
    internal static double[,] RotatedLayerMatrix(double q11, double q12, double q22, double g12, double theta)
    {
        double c = Math.Cos(theta), s = Math.Sin(theta);
        double[,] t =
        {
            { c * c, s * s, c * s },
            { s * s, c * c, -c * s },
            { -2.0 * c * s, 2.0 * c * s, c * c - s * s },
        };
        double[,] q = { { q11, q12, 0.0 }, { q12, q22, 0.0 }, { 0.0, 0.0, g12 } };
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = i; j < 3; j++)
            {
                double v = 0.0;
                for (int k = 0; k < 3; k++)
                    for (int l = 0; l < 3; l++)
                        v += t[k, i] * q[k, l] * t[l, j];
                r[i, j] = r[j, i] = v;
            }
        return r;
    }

    /// <summary>
    /// Матрица слоя с регуляризацией у нуля: главное направление с нулевым секущим модулем (растяжение не работает) и
    /// 0 &lt; ε_eq &lt; δ получает E = E₀·(1 − ε_eq/δ); Q и соосный G₁₂ пересобираются по закону слоя (Дарвин — Пекнольд с
    /// ν слоя). Остальные слои — без изменений.
    /// </summary>
    private static (double Q11, double Q12, double Q22, double G12) Regularized(PlateConcreteLayerPoint p,
        double band, double e0)
    {
        static bool InBand(double eps, double e, double band) => e == 0.0 && eps > 0.0 && eps < band;
        bool r1 = InBand(p.Eps1Eq, p.E1, band), r2 = InBand(p.Eps2Eq, p.E2, band);
        if (!r1 && !r2) return (p.Q11, p.Q12, p.Q22, p.G12);
        double e1 = r1 ? e0 * (1.0 - p.Eps1Eq / band) : p.E1;
        double e2 = r2 ? e0 * (1.0 - p.Eps2Eq / band) : p.E2;
        double k = 1.0 / (1.0 - p.Nu * p.Nu);
        double q11 = k * e1, q22 = k * e2, q12 = k * p.Nu * Math.Sqrt(Math.Max(0.0, e1 * e2));
        double sig1 = q11 * p.Eps1 + q12 * p.Eps2, sig2 = q12 * p.Eps1 + q22 * p.Eps2;
        double dEps = p.Eps1 - p.Eps2;
        double g12 = dEps > 1e-14 ? (sig1 - sig2) / (2.0 * dEps) : 0.25 * (q11 + q22) - 0.5 * q12;
        return (q11, q12, q22, Math.Max(g12, 0.0));
    }

    private static void Accumulate(double[,] a, double[,] b, double[,] d, double[,] q, double dz, double z)
    {
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double v = q[i, j] * dz;
                a[i, j] += v;
                b[i, j] += v * z;
                d[i, j] += v * z * z;
            }
    }

    private static double[,] Scale(double[,] m, double k)
    {
        var r = new double[m.GetLength(0), m.GetLength(1)];
        for (int i = 0; i < m.GetLength(0); i++)
            for (int j = 0; j < m.GetLength(1); j++)
                r[i, j] = m[i, j] * k;
        return r;
    }
}
