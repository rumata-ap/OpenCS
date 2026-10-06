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
    /// <summary>Секущая ABD в единицах CScore.</summary>
    public static SecantAbd Build(PlateSection section, ShellStrainState s, Diagramm cDiag, Diagramm rDiag,
        IReadOnlyList<Diagramm?>? layerDiags = null, PlateLayerState? layerState = null,
        bool? tensionOverride = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(s);
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];

        int nl = section.NLayers < 1 ? 1 : section.NLayers;
        for (int i = 0; i < nl; i++)
        {
            var p = section.EvaluateConcreteLayer(i, s, cDiag, tensionOverride, layerState);
            var q = RotatedLayerMatrix(p.Q11, p.Q12, p.Q22, p.G12, p.Theta);
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
        PlateLayerState? layerState = null, bool? tensionOverride = null)
    {
        ArgumentNullException.ThrowIfNull(materials);
        var abd = Build(section, s, materials.ConcreteDiagram, materials.RebarDiagram, materials.LayerDiagrams,
            layerState, tensionOverride);
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
