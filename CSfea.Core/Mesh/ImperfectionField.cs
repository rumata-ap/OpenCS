namespace CSfea.Core;

/// <summary>
/// Начальное несовершенство схемы — поэлементные начальные перемещения u₀ (глобальные оси): стержень — 12 DOF концов
/// (поступательные и повороты, кубика Эрмита: парабола погиби представляется точно), оболочка — 3 поступательных на
/// узел. Разрыв u₀ между КЭ допустим (эквивалентные силы, а не совместная геометрия). Правая часть второго порядка —
/// −g, g_e = K_σ,e(N)·u₀,e (<see cref="StructuralMesh.ImperfectionForce"/>); напряжений u₀ не создаёт, усилия КЭ —
/// по дополнительным перемещениям u относительно несовершенной оси.
/// </summary>
public sealed class ImperfectionField
{
    public ImperfectionField(int beams, int shells)
    {
        Beam = new double[beams][];
        Shell = new double[shells][];
    }

    /// <summary>u₀ стержня (индекс в <see cref="StructuralMesh.Beams"/>), 12 чисел в глобальных осях; null — нет.</summary>
    public double[]?[] Beam { get; }

    /// <summary>u₀ оболочки (индекс в <see cref="StructuralMesh.Shells"/>), 3·n узлов (ux, uy, uz), глобальные оси; null — нет.</summary>
    public double[]?[] Shell { get; }

    /// <summary>Ни у одного КЭ нет u₀.</summary>
    public bool IsEmpty => Beam.All(b => b == null) && Shell.All(s => s == null);

    /// <summary>Наибольшее по модулю поступательное u₀ (м) по всем КЭ — для отчёта.</summary>
    public double MaxTranslation()
    {
        double m = 0.0;
        foreach (var b in Beam)
            if (b != null)
                for (int k = 0; k < 12; k++)
                    if (k % 6 < 3) m = Math.Max(m, Math.Abs(b[k]));
        foreach (var s in Shell)
            if (s != null)
                foreach (double v in s) m = Math.Max(m, Math.Abs(v));
        return m;
    }

    /// <summary>Проверка размеров под сетку <paramref name="mesh"/>.</summary>
    public void CheckFor(StructuralMesh mesh)
    {
        if (Beam.Length != mesh.Beams.Count || Shell.Length != mesh.Shells.Count)
            throw new ArgumentException($"Несовершенство задано для {Beam.Length} стержней и {Shell.Length} оболочек, " +
                                        $"а в сетке {mesh.Beams.Count} и {mesh.Shells.Count}.");
        for (int i = 0; i < Beam.Length; i++)
            if (Beam[i] is { Length: not 12 })
                throw new ArgumentException($"Несовершенство стержня {i}: нужно 12 чисел, задано {Beam[i]!.Length}.");
        for (int i = 0; i < Shell.Length; i++)
            if (Shell[i] is { } s && s.Length != 3 * mesh.Shells[i].Nodes.Length)
                throw new ArgumentException($"Несовершенство оболочки {i}: нужно {3 * mesh.Shells[i].Nodes.Length} чисел, задано {s.Length}.");
    }
}
