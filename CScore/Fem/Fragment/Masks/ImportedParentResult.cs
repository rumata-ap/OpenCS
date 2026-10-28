using System.Globalization;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>
/// Соглашения о знаках усилий программы-источника относительно «положительной грани» сечения (вектор усилия на грани
/// с внешней нормалью +X1, в местных осях): множители на компоненты программы.
/// </summary>
/// <param name="Bar">N Qy Qz Mx My Mz стержня: std = множитель × программа.</param>
/// <param name="Shell">Nx Ny Txy Mx My Mxy Qx Qy пластины: OpenCS (<see cref="FragmentShellForces"/>) = множитель × программа.</param>
public sealed record ImportedForceConvention(double[] Bar, double[] Shell)
{
    /// <summary>
    /// ЛИРА и SCAD (общие правила знаков): N растяжение +; My + растягивает волокна со стороны −Z1 (нижние,
    /// <c>LiraForceMapper.MapBar</c>, сверено с подбором ЛИРЫ 30.09), Mz — по импорту наборов (<c>My OpenCS = −Mz</c>);
    /// поперечные силы — <c>Qz = dMy/dx</c>, <c>Qy = dMz/dx</c> в знаках программы (проверяется по снимку —
    /// <see cref="ImportedParentResult.ShearConsistency"/>); Mк — как std. Пластины: Mx My Mxy + растягивают низ (−Z1),
    /// инверсия как у импорта наборов (Mxy ЛИРЫ сверен 22.09); N, Q — без инверсии.
    /// </summary>
    public static ImportedForceConvention LiraScad { get; } = new([1, -1, -1, 1, -1, 1], [1, 1, 1, -1, -1, -1, 1, 1]);
}

/// <summary>
/// Результат родителя для снятия масок (спека 2026-10-28, разд. 4.1, 4.5) из снимка результатов программы
/// (<see cref="ImportedResultSnapshot"/>): соглашения программы → конвенция <see cref="IFragmentParentResult"/>.
/// <list type="bullet">
/// <item>Ключи — загружения снимка, у которых есть загружение родителя с тем же номером программы
/// (<see cref="FemLoadCase.SourceLoadNum"/>); остальные — в <see cref="UnmatchedLoads"/>.</item>
/// <item>Перемещения — как в снимке (общие оси).</item>
/// <item>Стержни: усилия сечения → std (<see cref="ImportedForceConvention.Bar"/>) → сила узла на КЭ: у узла 1
/// <c>−S(0)</c>, у узла 2 <c>+S(L)</c>; оси — <see cref="BeamLocalAxisConvention"/> с <see cref="FemElement.BeamRotationDeg"/>
/// (без поворота — осей нет, сил нет).</item>
/// <item>Пластины: оси выдачи — «узел 1 → узел 2», повёрнутая на <see cref="FemElement.LocalAxisAngleDeg"/> вокруг нормали
/// <see cref="FemElementGeometry.ShellFrame"/> (без угла — осей нет, усилий нет).</item>
/// <item>Нагрузка КЭ стержня — равномерная по замыканию сил концов, как у <c>CsfeaFieldsParentResult</c>.</item>
/// </list>
/// </summary>
public sealed class ImportedParentResult : IFragmentParentResult
{
    sealed class Entry(ImportedResultLoad load)
    {
        public ImportedResultLoad Load { get; } = load;
        public Dictionary<int, int> Nodes { get; } = Index(load.NodeNums);
        public Dictionary<int, int> Bars { get; } = Index(load.BarNums);
        public Dictionary<int, int> Shells { get; } = Index(load.ShellNums);

        static Dictionary<int, int> Index(int[] nums)
        {
            var d = new Dictionary<int, int>(nums.Length);
            for (int i = 0; i < nums.Length; i++) d.TryAdd(nums[i], i);
            return d;
        }
    }

    sealed record BeamFrame(PlanarVector3 X, PlanarVector3 Y, PlanarVector3 Z, double Length);

    readonly ImportedForceConvention _convention;
    readonly List<FragmentMaskKey> _keys = [];
    readonly Dictionary<FragmentMaskKey, Entry> _entries = [];
    readonly Dictionary<string, (PlanarVector3 X, PlanarVector3 Y)> _shellAxes = new(StringComparer.Ordinal);
    readonly Dictionary<string, BeamFrame> _beamFrames = new(StringComparer.Ordinal);

    /// <param name="snapshot">Снимок программы.</param>
    /// <param name="loadCaseIdByNum">Номер загружения программы → Id загружения родителя.</param>
    /// <param name="nodes">Узлы сетки родителя по тегу.</param>
    /// <param name="elements">КЭ сетки родителя.</param>
    /// <param name="convention">Знаки программы; null — <see cref="ImportedForceConvention.LiraScad"/>.</param>
    public ImportedParentResult(ImportedResultSnapshot snapshot, IReadOnlyDictionary<int, int> loadCaseIdByNum,
        IReadOnlyDictionary<string, FemMeshNode> nodes, IReadOnlyList<FemElement> elements,
        ImportedForceConvention? convention = null)
    {
        _convention = convention ?? ImportedForceConvention.LiraScad;
        foreach (var load in snapshot.Loads)
        {
            if (!loadCaseIdByNum.TryGetValue(load.Num, out int id)) { UnmatchedLoads.Add(load.Num); continue; }
            var key = FragmentMaskKey.OfLoadCase(id);
            if (_entries.TryAdd(key, new Entry(load))) _keys.Add(key);
        }
        foreach (var e in elements)
        {
            if (FemElementGeometry.Of(e, nodes) is not { } g) continue;
            if (g.IsShell)
            {
                if (e.LocalAxisAngleDeg is not { } angle) { ShellsWithoutAxes.Add(e.ElemTag); continue; }
                var (x1, _, n) = g.ShellFrame();
                double a = angle * Math.PI / 180;
                var x = (x1 * Math.Cos(a) + n.Cross(x1) * Math.Sin(a)).Normalize();
                _shellAxes.TryAdd(e.ElemTag, (x, n.Cross(x).Normalize()));
            }
            else if (g.IsBar && g.Length > 1e-12)
            {
                if (e.BeamRotationDeg is not { } rot) { BarsWithoutAxes.Add(e.ElemTag); continue; }
                try
                {
                    var (x, y, z) = BeamLocalAxisConvention.Frame(g.Points[0], g.Points[1], rot);
                    _beamFrames.TryAdd(e.ElemTag, new BeamFrame(x, y, z, g.Length));
                }
                catch (InvalidOperationException) { }
            }
        }
    }

    /// <summary>Номера загружений снимка без загружения родителя (ключа нет).</summary>
    public List<int> UnmatchedLoads { get; } = [];

    /// <summary>Пластины без угла осей выдачи: усилий нет.</summary>
    public List<string> ShellsWithoutAxes { get; } = [];

    /// <summary>Стержни без поворота сечения: усилий нет.</summary>
    public List<string> BarsWithoutAxes { get; } = [];

    public IReadOnlyList<FragmentMaskKey> Keys => _keys;

    public bool KnowsBeamLoads => true;

    public bool TryNodeDisplacement(FragmentMaskKey key, string meshNodeTag, out Dof6 displacement)
    {
        displacement = Dof6.Zero;
        if (!_entries.TryGetValue(key, out var en) || !TryNum(meshNodeTag, out int num) || !en.Nodes.TryGetValue(num, out int i))
            return false;
        var d = en.Load.Displacements;
        int o = i * ImportedResultLoad.NodeComponents;
        displacement = new Dof6(d[o], d[o + 1], d[o + 2], d[o + 3], d[o + 4], d[o + 5]);
        return true;
    }

    public bool TryBeamEndForces(FragmentMaskKey key, string elemTag, out BeamEndForces forces)
    {
        forces = null!;
        if (!_beamFrames.ContainsKey(elemTag) || !TrySections(key, elemTag, out var first, out var last)) return false;
        forces = new BeamEndForces(-first, last);
        return true;
    }

    public bool TryShellForces(FragmentMaskKey key, string elemTag, out FragmentShellForces forces)
    {
        forces = null!;
        if (!_entries.TryGetValue(key, out var en) || !_shellAxes.TryGetValue(elemTag, out var axes)
            || !TryNum(elemTag, out int num) || !en.Shells.TryGetValue(num, out int i))
            return false;
        int o = i * ImportedResultLoad.ShellComponents;
        var v = new double[FragmentShellForces.Components];
        for (int k = 0; k < v.Length; k++) v[k] = _convention.Shell[k] * en.Load.ShellForces[o + k];
        forces = new FragmentShellForces(axes.X, axes.Y, v);
        return true;
    }

    public IReadOnlyList<FragmentBeamSpanLoad> BeamLoads(FragmentMaskKey key, string elemTag)
    {
        if (!_beamFrames.TryGetValue(elemTag, out var fr) || !TryBeamEndForces(key, elemTag, out var f)) return [];
        // Равномерная нагрузка из равновесия сил КЭ: f_I + f_J + q·L = 0 (местные оси → общие).
        var sum = f.I.Force + f.J.Force;
        var q = (fr.X * sum.X + fr.Y * sum.Y + fr.Z * sum.Z) * (-1 / fr.Length);
        double scale = Math.Max(f.I.MaxForceAbs, f.J.MaxForceAbs);
        if (q.Length * fr.Length <= 1e-9 * scale) return [];
        return [FragmentBeamSpanLoad.Distributed(0, 1, q, q)];
    }

    /// <summary>
    /// Согласованность поперечных сил с градиентом моментов по всем стержням и ключам, std:
    /// <c>dMy/dx = Qz</c>, <c>dMz/dx = −Qy</c>; для равномерной нагрузки точно <c>(M(L) − M(0))/L = (Q(0) + Q(L))/2</c>.
    /// Множитель МНК «градиент = r · средняя Q» по плоскостям XZ (<c>Rz</c>) и XY (<c>Ry</c>): верные знаки — около +1,
    /// перевёрнутые — около −1; null — изгиба в плоскости нет. <c>Bars</c> — число стержне-ключей.
    /// </summary>
    public (double? Ry, double? Rz, int Bars) ShearConsistency()
    {
        double gyq = 0, qy2 = 0, gzq = 0, qz2 = 0;
        int count = 0;
        foreach (var key in _keys)
            foreach (var (tag, fr) in _beamFrames)
            {
                if (!TrySections(key, tag, out var s0, out var s1)) continue;
                count++;
                double qz = (s0.Z + s1.Z) / 2, qy = -(s0.Y + s1.Y) / 2;
                gzq += (s1.Ry - s0.Ry) / fr.Length * qz; qz2 += qz * qz;
                gyq += (s1.Rz - s0.Rz) / fr.Length * qy; qy2 += qy * qy;
            }
        return (qy2 > 0 ? gyq / qy2 : null, qz2 > 0 ? gzq / qz2 : null, count);
    }

    /// <summary>Усилия первого и последнего сечения в std (положительная грань, местные оси).</summary>
    bool TrySections(FragmentMaskKey key, string elemTag, out Dof6 first, out Dof6 last)
    {
        first = last = Dof6.Zero;
        if (!_entries.TryGetValue(key, out var en) || !TryNum(elemTag, out int num) || !en.Bars.TryGetValue(num, out int i))
            return false;
        var b = en.Load.BarForces;
        var c = _convention.Bar;
        int o = i * ImportedResultLoad.BarComponents;
        first = new Dof6(c[0] * b[o], c[1] * b[o + 1], c[2] * b[o + 2], c[3] * b[o + 3], c[4] * b[o + 4], c[5] * b[o + 5]);
        o += 6;
        last = new Dof6(c[0] * b[o], c[1] * b[o + 1], c[2] * b[o + 2], c[3] * b[o + 3], c[4] * b[o + 4], c[5] * b[o + 5]);
        return true;
    }

    static bool TryNum(string tag, out int num) => int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out num);
}
