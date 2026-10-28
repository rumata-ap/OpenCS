using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.Submodel;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Результат родителя для снятия масок (спека 2026-10-28, разд. 4.1) по записанным полям CSfea
/// (<see cref="RcSecantStepFields"/>): поля линейного загружения или последнего сошедшегося шага стадии секущего
/// расчёта на ключ. Теги узлов и КЭ — номера модели (теги сетки родителя).
/// <list type="bullet">
/// <item>Перемещения — по <see cref="RcSecantStepFields.NodeIds"/>, общие оси.</item>
/// <item>Силы концов стержня — как записаны: местные оси CSfea (<see cref="BeamLocalAxisConvention"/> с поворотом
/// <see cref="FemRcModelAdapter.BeamRotationDeg"/> — у КЭ своей схемы это поворот КонЭ), сила узла на КЭ. Экстрактору
/// масок КЭ передаются с этим поворотом (<see cref="WithBeamRotation"/>).</item>
/// <item>Усилия пластин — в осях выдачи постановки: x — <see cref="FemRcModelAdapter.ShellForceAxis"/> (угол КЭ или ось
/// области), y = нормаль контура × x; поле уже повёрнуто из осей армирования (<c>ShellForceAngles</c>).</item>
/// <item>Нагрузка КЭ стержня — равномерная по замыканию сил концов q = −(f_I + f_J)/L (общие оси), как в детальных
/// эпюрах (<see cref="FemCsfeaBarDetail"/>): точна для равномерной нагрузки, у неравномерной и сосредоточенной в
/// пролёте усилие внутри КЭ приближённое.</item>
/// </list>
/// Отсутствие узла, КЭ или ключа — false, а не ноль.
/// </summary>
public sealed class CsfeaFieldsParentResult : IFragmentParentResult
{
    sealed class Entry(RcSecantStepFields fields)
    {
        public RcSecantStepFields Fields { get; } = fields;
        public Dictionary<int, int> Nodes { get; } = Index(fields.NodeIds);
        public Dictionary<int, int> Shells { get; } = Index(fields.ShellIds);
        public Dictionary<int, int> Beams { get; } = Index(fields.BeamIds);

        static Dictionary<int, int> Index(int[] ids)
        {
            var d = new Dictionary<int, int>(ids.Length);
            for (int i = 0; i < ids.Length; i++) d.TryAdd(ids[i], i);
            return d;
        }
    }

    /// <summary>Местные оси и длина КЭ стержня.</summary>
    sealed record BeamFrame(PlanarVector3 X, PlanarVector3 Y, PlanarVector3 Z, double Length);

    readonly List<FragmentMaskKey> _keys = [];
    readonly Dictionary<FragmentMaskKey, Entry> _entries = [];
    readonly Dictionary<string, (PlanarVector3 X, PlanarVector3 Y)> _shellAxes = new(StringComparer.Ordinal);
    readonly Dictionary<string, BeamFrame> _beamFrames = new(StringComparer.Ordinal);

    /// <param name="steps">Поля на ключ (повтор ключа — берётся первый).</param>
    /// <param name="nodes">Узлы сетки родителя по тегу.</param>
    /// <param name="elements">КЭ сетки родителя.</param>
    /// <param name="members">КонЭ родителя (поворот стержней, ось области пластин).</param>
    /// <param name="regionAxes">Ось x выдачи пластин по тегу КонЭ (<see cref="FemCsfeaInputBuilder.RegionAxes(IReadOnlyList{FemMember}, IReadOnlyList{PlanarRegion})"/>).</param>
    public CsfeaFieldsParentResult(IReadOnlyList<(FragmentMaskKey Key, RcSecantStepFields Fields)> steps,
        IReadOnlyDictionary<string, FemMeshNode> nodes, IReadOnlyList<FemElement> elements, IReadOnlyList<FemMember> members,
        IReadOnlyDictionary<string, PlanarVector3>? regionAxes)
    {
        foreach (var (key, fields) in steps)
            if (_entries.TryAdd(key, new Entry(fields))) _keys.Add(key);
        var memberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var m in members) memberByTag.TryAdd(m.ElemTag, m);
        foreach (var e in elements)
        {
            if (FemElementGeometry.Of(e, nodes) is not { } g) continue;
            if (g.IsShell)
            {
                var n = g.ShellFrame().Z;
                var x = FemRcModelAdapter.ShellForceAxis(e, g, memberByTag, regionAxes);
                _shellAxes.TryAdd(e.ElemTag, (x, n.Cross(x).Normalize()));
            }
            else if (g.IsBar && g.Length > 1e-12)
            {
                try
                {
                    var (x, y, z) = BeamLocalAxisConvention.Frame(g.Points[0], g.Points[1],
                        FemRcModelAdapter.BeamRotationDeg(e, memberByTag));
                    _beamFrames.TryAdd(e.ElemTag, new BeamFrame(x, y, z, g.Length));
                }
                catch (InvalidOperationException) { }
            }
        }
    }

    /// <summary>
    /// Копии КЭ стержней с поворотом сечения, в осях которого записаны силы концов (<see cref="FemRcModelAdapter.BeamRotationDeg"/>);
    /// прочие КЭ — как есть. Вход экстрактора масок берёт поворот из <see cref="FemElement.BeamRotationDeg"/>.
    /// </summary>
    public static List<FemElement> WithBeamRotation(IReadOnlyList<FemElement> elements, IReadOnlyList<FemMember> members)
    {
        var memberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var m in members) memberByTag.TryAdd(m.ElemTag, m);
        return elements.Select(e =>
        {
            if (e.ElemType != "beam" || e.BeamRotationDeg != null) return e;
            var copy = e.ShallowCopy();
            copy.BeamRotationDeg = FemRcModelAdapter.BeamRotationDeg(e, memberByTag);
            return copy;
        }).ToList();
    }

    public IReadOnlyList<FragmentMaskKey> Keys => _keys;

    public bool KnowsBeamLoads => true;

    public bool TryNodeDisplacement(FragmentMaskKey key, string meshNodeTag, out Dof6 displacement)
    {
        displacement = Dof6.Zero;
        if (!_entries.TryGetValue(key, out var en) || !TryId(meshNodeTag, out int id) || !en.Nodes.TryGetValue(id, out int i))
            return false;
        var d = en.Fields.Displacements;
        int o = i * RcSecantStepFields.NodeComponents;
        if (o + 6 > d.Length) return false;
        displacement = new Dof6(d[o], d[o + 1], d[o + 2], d[o + 3], d[o + 4], d[o + 5]);
        return true;
    }

    public bool TryBeamEndForces(FragmentMaskKey key, string elemTag, out BeamEndForces forces)
    {
        forces = null!;
        if (!_entries.TryGetValue(key, out var en) || !TryId(elemTag, out int id) || !en.Beams.TryGetValue(id, out int i))
            return false;
        var b = en.Fields.BeamForces;
        int o = i * RcSecantStepFields.BeamForceComponents;
        if (o + 12 > b.Length) return false;
        forces = new BeamEndForces(new Dof6(b[o], b[o + 1], b[o + 2], b[o + 3], b[o + 4], b[o + 5]),
            new Dof6(b[o + 6], b[o + 7], b[o + 8], b[o + 9], b[o + 10], b[o + 11]));
        return true;
    }

    public bool TryShellForces(FragmentMaskKey key, string elemTag, out FragmentShellForces forces)
    {
        forces = null!;
        if (!_entries.TryGetValue(key, out var en) || !_shellAxes.TryGetValue(elemTag, out var axes)
            || !TryId(elemTag, out int id) || !en.Shells.TryGetValue(id, out int i))
            return false;
        var s = en.Fields.ShellForces;
        int o = i * RcSecantStepFields.ShellForceComponents;
        if (o + FragmentShellForces.Components > s.Length) return false;
        forces = new FragmentShellForces(axes.X, axes.Y, s[o..(o + FragmentShellForces.Components)]);
        return true;
    }

    public IReadOnlyList<FragmentBeamSpanLoad> BeamLoads(FragmentMaskKey key, string elemTag)
    {
        if (!_beamFrames.TryGetValue(elemTag, out var fr) || !TryBeamEndForces(key, elemTag, out var f)) return [];
        // Равномерная нагрузка из равновесия сил КЭ: f_I + f_J + q·L = 0 (местные оси → общие).
        var sum = f.I.Force + f.J.Force;
        var q = (fr.X * sum.X + fr.Y * sum.Y + fr.Z * sum.Z) * (-1 / fr.Length);
        // Без пролётной нагрузки сумма сил концов — шум решения: порог от модуля сил концов.
        double scale = Math.Max(f.I.MaxForceAbs, f.J.MaxForceAbs);
        if (q.Length * fr.Length <= 1e-9 * scale) return [];
        return [FragmentBeamSpanLoad.Distributed(0, 1, q, q)];
    }

    static bool TryId(string tag, out int id) => int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
}
