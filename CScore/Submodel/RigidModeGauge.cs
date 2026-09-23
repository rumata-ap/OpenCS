using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Кинематически заданный DOF (закрепление, заданное перемещение): точка и DOF 0..5.</summary>
public sealed record KinematicDofRef(PlanarVector3 Point, int Dof);

/// <summary>Кандидат в фиксацию жёстких мод: силовой DOF конца цепочки.</summary>
public sealed record GaugeCandidate(bool AtStart, PlanarVector3 Point, int Dof);

/// <summary>Итог дополнения фиксации: ранг до дополнения, добавленные DOF, достигнут ли ранг 6.</summary>
public sealed record GaugeResult(int RankBefore, IReadOnlyList<GaugeCandidate> Added, bool Complete);

/// <summary>
/// Проверка ранга постановки по шести жёсткотельным модам цепочки <c>u(x) = t + θ × (x − x₀)</c> и
/// жадное дополнение фиксации кандидатами до ранга 6. Строка поступательного DOF d в точке
/// <c>r = x − x₀</c> — <c>[e_d | (r × e_d)/L]</c>, вращательного — <c>[0 | e_d]</c>
/// (параметры t и θ·L, чтобы ранг не зависел от единиц длины).
/// </summary>
public static class RigidModeGauge
{
    const double RelativeTolerance = 1e-9;

    public static GaugeResult Complete(PlanarVector3 origin, double characteristicLength,
        IReadOnlyList<KinematicDofRef> constrained, IReadOnlyList<GaugeCandidate> candidates)
    {
        double scale = characteristicLength > 0 && double.IsFinite(characteristicLength) ? characteristicLength : 1.0;
        var basis = new List<double[]>();
        foreach (var dof in constrained)
            TryAdd(basis, Row(origin, scale, dof.Point, dof.Dof));
        int rankBefore = basis.Count;

        var added = new List<GaugeCandidate>();
        foreach (var candidate in candidates)
        {
            if (basis.Count == 6) break;
            if (TryAdd(basis, Row(origin, scale, candidate.Point, candidate.Dof)))
                added.Add(candidate);
        }
        return new GaugeResult(rankBefore, added, basis.Count == 6);
    }

    static double[] Row(PlanarVector3 origin, double scale, PlanarVector3 point, int dof)
    {
        if (dof is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(dof), dof, "DOF должен быть 0..5.");
        var row = new double[6];
        if (dof >= 3)
        {
            row[dof] = 1;
            return row;
        }
        var e = dof switch { 0 => new PlanarVector3(1, 0, 0), 1 => new PlanarVector3(0, 1, 0), _ => new PlanarVector3(0, 0, 1) };
        var rotation = (point - origin).Cross(e) * (1.0 / scale);
        row[dof] = 1;
        row[3] = rotation.X; row[4] = rotation.Y; row[5] = rotation.Z;
        return row;
    }

    /// <summary>Модифицированный Грам–Шмидт: добавляет строку, если её остаток не мал.</summary>
    static bool TryAdd(List<double[]> basis, double[] row)
    {
        double norm = Norm(row);
        if (norm == 0) return false;
        var residual = (double[])row.Clone();
        foreach (var q in basis)
        {
            double projection = Dot(residual, q);
            for (int i = 0; i < 6; i++) residual[i] -= projection * q[i];
        }
        double residualNorm = Norm(residual);
        if (residualNorm <= RelativeTolerance * Math.Max(1.0, norm)) return false;
        for (int i = 0; i < 6; i++) residual[i] /= residualNorm;
        basis.Add(residual);
        return true;
    }

    static double Dot(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < 6; i++) sum += a[i] * b[i];
        return sum;
    }

    static double Norm(double[] a) => Math.Sqrt(Dot(a, a));
}
