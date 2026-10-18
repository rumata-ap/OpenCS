using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>
/// Начальные (упругие) жёсткости КЭ для полей расчёта, у которого сечения КЭ не хранят своего начального состояния
/// (<see cref="RcSecantFieldExtractor"/>): по номеру оболочки или стержня модели; null — как секущая.
/// </summary>
public interface IRcInitialStiffness
{
    ShellTangent? InitialShell(int shellId);
    (double[,] S, BeamShearStiffness Shear)? InitialBeam(int beamId);
}

/// <summary>
/// Сечения КЭ по записанному конечному состоянию секущего расчёта (<see cref="RcSecantStepFields.HasMatrices"/>) —
/// линейный расчёт на замороженных секущих жёсткостях (инженерная нелинейность). Пластина —
/// <see cref="SecantShellResponse"/> по ABD и As в осях сечения (построитель поворачивает её так же, как в секущем
/// прогоне); стержень — <see cref="SecantBeamResponse"/> по матрице КЭ, сдвиговым жёсткостям и GJ, стоявшим в матрице
/// жёсткости прогона (<see cref="RcSecantStepFields.HasElementMatrices"/>). У старой записи без матриц КЭ — средняя
/// податливость матриц трёх точек (<see cref="Exact"/> = false: совпадение с прогоном — до допуска его итераций).
/// <para>
/// Нижний предел: диагональ не меньше <c>eMin</c> от начальной упругой (по сечению <paramref name="elastic"/> —
/// жёсткость линейного расчёта) — строка и столбец масштабируются одним множителем, симметрия и положительная
/// определённость сохраняются. КЭ, упёршиеся в предел, — <see cref="FlooredShells"/>, <see cref="FlooredBeams"/>.
/// </para>
/// Построитель вызывает фабрику последовательно.
/// </summary>
public sealed class RecordedRcSectionFactory : IRcSectionFactory, IRcInitialStiffness
{
    private readonly RcSecantStepFields _f;
    private readonly double _eMin;
    private readonly IRcSectionFactory _elastic;
    private readonly Dictionary<int, int> _shellIndex = new(), _beamIndex = new();
    private readonly Dictionary<int, ShellTangent> _shellInitial = new();
    private readonly Dictionary<int, (double[,] S, BeamShearStiffness Shear)> _beamInitial = new();

    /// <param name="source">Поля конечного состояния секущего прогона с полными матрицами.</param>
    /// <param name="eMin">Нижний предел доли начальной жёсткости, 0 &lt; eMin &lt; 1.</param>
    /// <param name="elastic">Упругие сечения для начальной жёсткости (по умолчанию <see cref="LinearRcSectionFactory"/>).</param>
    public RecordedRcSectionFactory(RcSecantStepFields source, double eMin, IRcSectionFactory? elastic = null)
    {
        _f = source ?? throw new ArgumentNullException(nameof(source));
        if (!source.HasMatrices) throw new ArgumentException("В записи нет полных секущих матриц КЭ.", nameof(source));
        if (!(eMin > 0 && eMin < 1)) throw new ArgumentOutOfRangeException(nameof(eMin), "Нужно 0 < e_min < 1.");
        _eMin = eMin;
        _elastic = elastic ?? new LinearRcSectionFactory();
        for (int i = 0; i < source.ShellIds.Length; i++) _shellIndex[source.ShellIds[i]] = i;
        for (int i = 0; i < source.BeamIds.Length; i++) _beamIndex[source.BeamIds[i]] = i;
    }

    /// <summary>Матрицы стержней — те, что стояли в матрице жёсткости прогона (запись с матрицами КЭ).</summary>
    public bool Exact => _f.HasElementMatrices
        && _f.BeamElementMatrices.Length == _f.BeamIds.Length * RcSecantStepFields.BeamElementMatrixComponents;

    /// <summary>Номера оболочек и стержней, у которых сработал нижний предел.</summary>
    public List<int> FlooredShells { get; } = new();
    public List<int> FlooredBeams { get; } = new();

    public IShellSectionResponse Shell(RcShell shell)
    {
        if (!_shellIndex.TryGetValue(shell.Id, out int i))
            throw new InvalidOperationException($"Оболочки {shell.Id} нет в записи источника жёсткостей.");
        int o = i * RcSecantStepFields.ShellMatrixComponents;
        var m = _f.ShellMatrices;
        var abd = new double[6, 6];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                abd[r, c] = m[o + 3 * r + c];               // A
                abd[r, 3 + c] = m[o + 9 + 3 * r + c];       // B
                abd[3 + c, r] = m[o + 9 + 3 * r + c];       // Bᵀ
                abd[3 + r, 3 + c] = m[o + 18 + 3 * r + c];  // D
            }
        var shear = new double[2, 2];
        for (int r = 0; r < 2; r++) for (int c = 0; c < 2; c++) shear[r, c] = m[o + 27 + 2 * r + c];

        ShellTangent? elastic = null;
        try { elastic = _elastic.Shell(shell).Tangent(new double[3], new double[3], new double[2]); }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { }
        if (elastic is { } e0)
        {
            _shellInitial[shell.Id] = e0;
            var d0 = new[] { e0.A[0, 0], e0.A[1, 1], e0.A[2, 2], e0.D[0, 0], e0.D[1, 1], e0.D[2, 2] };
            bool floored = StiffnessFloor.Apply(abd, d0, _eMin);
            floored |= StiffnessFloor.Apply(shear, new[] { e0.As[0, 0], e0.As[1, 1] }, _eMin);
            if (floored) FlooredShells.Add(shell.Id);
        }
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++) { a[r, c] = abd[r, c]; b[r, c] = abd[r, 3 + c]; d[r, c] = abd[3 + r, 3 + c]; }
        return new SecantShellResponse(new ShellTangent(a, b, d, shear));
    }

    public IBeamSectionResponse Beam(RcBeam beam)
    {
        if (!_beamIndex.TryGetValue(beam.Id, out int i))
            throw new InvalidOperationException($"Стержня {beam.Id} нет в записи источника жёсткостей.");
        double[,] s;
        double gaY, gaZ, gj;
        if (Exact)
        {
            int o = i * RcSecantStepFields.BeamElementMatrixComponents;
            s = Matrix3(_f.BeamElementMatrices, o);
            (gaY, gaZ, gj) = (_f.BeamElementMatrices[o + 9], _f.BeamElementMatrices[o + 10], _f.BeamElementMatrices[o + 11]);
        }
        else
        {
            int o = i * RcSecantStepFields.BeamMatrixComponents;
            var p = new double[3][,];
            for (int k = 0; k < 3; k++) p[k] = Matrix3(_f.BeamMatrices, o + 11 * k);
            try { s = BeamElements.MeanCompliance(p[0], p[1], p[2]); }
            catch (InvalidOperationException)
            {
                s = new double[3, 3];
                for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) s[r, c] = (p[0][r, c] + 4 * p[1][r, c] + p[2][r, c]) / 6.0;
            }
            (gaY, gaZ) = (_f.BeamMatrices[o + 9], _f.BeamMatrices[o + 10]);
            gj = RecordedStiffness(i, 20) ?? double.NaN;
        }

        double[,]? s0 = null;
        double gj0 = double.NaN;
        try
        {
            var el = _elastic.Beam(beam);
            s0 = el.Tangent(0, 0, 0);
            gj0 = el.TorsionalStiffness();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { }
        if (s0 == null && RecordedStiffness(i, 15) is double ea0)
            s0 = new[,] { { ea0, 0, 0 }, { 0, RecordedStiffness(i, 16) ?? 0, 0 }, { 0, 0, RecordedStiffness(i, 17) ?? 0 } };
        if (!double.IsFinite(gj)) gj = double.IsFinite(gj0) ? gj0 : 0.0;

        var shear = new BeamShearStiffness(gaY > 0 ? gaY : double.PositiveInfinity, gaZ > 0 ? gaZ : double.PositiveInfinity);
        bool floored = false;
        if (s0 != null)
        {
            _beamInitial[beam.Id] = (s0, BeamShearStiffness.Rigid);
            floored = StiffnessFloor.Apply(s, new[] { s0[0, 0], s0[1, 1], s0[2, 2] }, _eMin);
        }
        // Сдвиг — от начальной сдвиговой жёсткости секущего прогона (у линейного стержня сдвига нет).
        double ga0Y = RecordedStiffness(i, 18) ?? double.NaN, ga0Z = RecordedStiffness(i, 19) ?? double.NaN;
        if (double.IsFinite(shear.GAvY) && double.IsFinite(ga0Y) && shear.GAvY < _eMin * ga0Y) { shear = shear with { GAvY = _eMin * ga0Y }; floored = true; }
        if (double.IsFinite(shear.GAvZ) && double.IsFinite(ga0Z) && shear.GAvZ < _eMin * ga0Z) { shear = shear with { GAvZ = _eMin * ga0Z }; floored = true; }
        if (floored) FlooredBeams.Add(beam.Id);
        return new SecantBeamResponse(s, gj, shear);
    }

    public ShellTangent? InitialShell(int shellId) => _shellInitial.GetValueOrDefault(shellId);

    public (double[,] S, BeamShearStiffness Shear)? InitialBeam(int beamId) =>
        _beamInitial.TryGetValue(beamId, out var v) ? v : null;

    /// <summary>Член <paramref name="k"/> блока жёсткостей стержня с индексом <paramref name="i"/>; null — блока нет.</summary>
    private double? RecordedStiffness(int i, int k) =>
        _f.HasStiffness && _f.BeamStiffness.Length >= (i + 1) * RcSecantStepFields.BeamStiffnessComponents
            ? _f.BeamStiffness[i * RcSecantStepFields.BeamStiffnessComponents + k] : null;

    private static double[,] Matrix3(double[] a, int o)
    {
        var m = new double[3, 3];
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) m[r, c] = a[o + 3 * r + c];
        return m;
    }
}

/// <summary>Нижний предел диагонали симметричной матрицы жёсткости сечения.</summary>
public static class StiffnessFloor
{
    /// <summary>
    /// k[i,i] ≥ eMin·k0[i] для каждого i с k0[i] &gt; 0: строка и столбец i умножаются на √(eMin·k0[i]/k[i,i]) — диагональ
    /// становится ровно пределом, прочие диагональные члены не меняются, симметрия и положительная определённость
    /// сохраняются. Неположительная диагональ — строка и столбец обнуляются, на диагонали — предел. true — сработал.
    /// </summary>
    public static bool Apply(double[,] k, IReadOnlyList<double> k0, double eMin)
    {
        int n = k.GetLength(0);
        bool any = false;
        for (int i = 0; i < n && i < k0.Count; i++)
        {
            double target = eMin * k0[i];
            if (!(target > 0) || k[i, i] >= target) continue;
            any = true;
            if (k[i, i] > 0)
            {
                double f = Math.Sqrt(target / k[i, i]);
                for (int j = 0; j < n; j++) { k[i, j] *= f; k[j, i] *= f; }
            }
            else
            {
                for (int j = 0; j < n; j++) { k[i, j] = 0; k[j, i] = 0; }
                k[i, i] = target;
            }
        }
        return any;
    }
}
