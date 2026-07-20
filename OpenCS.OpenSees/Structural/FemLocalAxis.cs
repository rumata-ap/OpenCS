namespace OpenCS.OpenSees.Structural;

/// <summary>Фиксированная конвенция локальных осей стержня для geomTransf Linear.</summary>
public static class FemLocalAxis
{
    /// <summary>vecxz: глобальная Z для не-вертикальных стержней, глобальная X для вертикальных.</summary>
    public static (double X, double Y, double Z) Vecxz(FemLinearNode i, FemLinearNode j)
    {
        double dx = j.X - i.X, dy = j.Y - i.Y, dz = j.Z - i.Z;
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!double.IsFinite(len) || len < 1e-9)
            throw new InvalidOperationException("Нулевая длина стержня — невозможно построить локальные оси.");
        double ez = dz / len;
        return Math.Abs(ez) > 1 - 1e-6 ? (1, 0, 0) : (0, 0, 1);
    }
}
