using System.Globalization;
using CScore.Fem.Imperfections;
using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>
/// Предварительный упругий расчёт первого порядка для решений по несовершенствам (спека EN 1993-1-1 несовершенств, 5.4):
/// та же модель и те же сечения, что у секущего расчёта (<see cref="SecantRcSectionFactory"/> в начальном состоянии —
/// начальные жёсткости; при <see cref="RcSecantOptions.PhysicallyLinear"/> — упругие), без несовершенств и без K_σ, нагрузка
/// — конца последней стадии (сумма загружений всех стадий), одна факторизация. Теги КЭ и узлов — номера модели.
/// </summary>
public sealed class RcFirstOrderState : IFemFirstOrderState
{
    private readonly RcStructuralMeshBuild _build;
    private readonly double[] _u;
    private readonly Dictionary<int, int> _beamIndex = new();
    private readonly Dictionary<int, RcBeam> _beams = new();
    private readonly Dictionary<int, List<RcBeam>> _beamsAt = new();
    private readonly Dictionary<int, int> _supportMask = new();
    private readonly Dictionary<int, HashSet<int>> _springDofs = new();
    private readonly HashSet<int> _rigid = new();
    private readonly HashSet<int> _shellNodes = new();

    private RcFirstOrderState(RcStructuralModel model, RcStructuralMeshBuild build, double[] u, (double H, double V) reactions)
    {
        _build = build;
        _u = u;
        ReactionSums = reactions;
        for (int i = 0; i < build.BeamIds.Length; i++) _beamIndex[build.BeamIds[i]] = i;
        foreach (var b in model.Beams)
        {
            _beams.TryAdd(b.Id, b);
            foreach (int n in new[] { b.NodeI, b.NodeJ })
            {
                if (!_beamsAt.TryGetValue(n, out var list)) _beamsAt[n] = list = [];
                list.Add(b);
            }
        }
        foreach (var s in model.Supports) _supportMask[s.NodeId] = _supportMask.GetValueOrDefault(s.NodeId) | s.Mask;
        foreach (var s in model.Springs.Where(s => s.Stiffness != 0))
        {
            if (!_springDofs.TryGetValue(s.NodeId, out var set)) _springDofs[s.NodeId] = set = [];
            set.Add(s.Dof);
        }
        foreach (var r in model.RigidBodies.Where(r => (r.Mask & 0b111000) != 0))
        {
            _rigid.Add(r.Master);
            _rigid.UnionWith(r.Slaves);
        }
        foreach (var s in model.Shells) _shellNodes.UnionWith(s.NodeIds);
    }

    /// <summary>Перемещения сетки (NDof).</summary>
    public IReadOnlyList<double> U => _u;

    /// <summary>Сетка расчёта.</summary>
    public RcStructuralMeshBuild Build => _build;

    public (double H, double V) ReactionSums { get; }

    /// <summary>Расчёт; отмена — между фазами.</summary>
    public static RcFirstOrderState Run(RcStructuralModel model, RcSecantOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ct.ThrowIfCancellationRequested();
        options ??= new RcSecantOptions();
        var build = RcStructuralMeshBuilder.Build(model, new SecantRcSectionFactory(options));
        build.Mesh.MaxDegreeOfParallelism = options.Solver.MaxDegreeOfParallelism;
        var loads = model.Stages.SelectMany(s => s.Loads).ToList();
        var f = build.Combination(loads);
        var prescribed = build.PrescribedCombination(loads);
        ct.ThrowIfCancellationRequested();
        var u = build.Mesh.SolveLinear([f], build.Bc, null, prescribed != null ? [prescribed] : null)[0];
        ct.ThrowIfCancellationRequested();
        var r = build.Mesh.ComputeReactions(u, build.Bc, null, f);
        double rx = 0, ry = 0, rz = 0;
        for (int n = 0; n < build.Mesh.NNodes; n++)
        {
            rx += r[6 * n];
            ry += r[6 * n + 1];
            rz += r[6 * n + 2];
        }
        return new RcFirstOrderState(model, build, u, (Math.Sqrt(rx * rx + ry * ry), Math.Abs(rz)));
    }

    static int? Id(string tag) => int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : null;

    public double? AxialForce(string elemTag) =>
        Id(elemTag) is int id && _beamIndex.TryGetValue(id, out int i) ? _build.Mesh.BeamAxialForce(i, _u) : null;

    public double[]? NodeDisplacement(string nodeTag)
    {
        if (Id(nodeTag) is not int id || !_build.NodeIndex.ContainsKey(id)) return null;
        var d = new double[6];
        for (int c = 0; c < 6; c++) d[c] = _u[_build.Dof(id, c)];
        return d;
    }

    public double? BendingStiffness(string elemTag, bool alongLocalY)
    {
        if (Id(elemTag) is not int id || !_beamIndex.TryGetValue(id, out int i)) return null;
        var s = _build.Mesh.Beams[i].Section;
        if (s is SecantBeamResponse secant) return alongLocalY ? secant.Matrix[2, 2] : secant.Matrix[1, 1];
        var (_, eiy, eiz, _) = BeamElements.SectionStiffness(s);
        return alongLocalY ? eiz : eiy;
    }

    /// <summary>
    /// Конец воспринимает момент (п. 5.3.2(6) «соединение, воспринимающее момент»): у КЭ нет шарнира по моменту этой
    /// плоскости (бит 5 — Mz при прогибе вдоль местной y, бит 4 — My при прогибе вдоль z) и узел защемлён по повороту
    /// вокруг оси изгиба: закрепление или пружина по поворотам с проекцией оси ≥ 0,5, жёсткое тело с поворотами, другой
    /// стержень без полного шарнира по моментам у этого узла или оболочка.
    /// </summary>
    public bool? MomentEnd(string elemTag, string nodeTag, bool alongLocalY)
    {
        if (Id(elemTag) is not int id || Id(nodeTag) is not int node || !_beams.TryGetValue(id, out var b)) return null;
        int mask = node == b.NodeI ? b.ReleaseI : node == b.NodeJ ? b.ReleaseJ : -1;
        if (mask < 0) return null;
        int bit = alongLocalY ? 1 << 5 : 1 << 4;
        if ((mask & bit) != 0) return false;
        if (!_beamIndex.TryGetValue(id, out int i)) return null;
        var (t, _) = BeamElements.Beam3dT(_build.Mesh.BeamCoords(i), _build.Mesh.Beams[i].RefVec);
        int row = alongLocalY ? 5 : 4;   // строки T: 3 — местная x, 4 — y, 5 — z (повороты конца I)
        double[] axis = [t[row, 3], t[row, 4], t[row, 5]];
        return Restrained(node, id, axis);
    }

    private bool Restrained(int node, int beam, double[] axis)
    {
        double Share(Func<int, bool> fixedRotation)
        {
            double s = 0;
            for (int k = 0; k < 3; k++) if (fixedRotation(k)) s += axis[k] * axis[k];
            return Math.Sqrt(s);
        }
        if (_supportMask.TryGetValue(node, out int sm) && Share(k => (sm & 1 << (3 + k)) != 0) >= 0.5) return true;
        if (_springDofs.TryGetValue(node, out var sd) && Share(k => sd.Contains(3 + k)) >= 0.5) return true;
        if (_rigid.Contains(node) || _shellNodes.Contains(node)) return true;
        foreach (var other in _beamsAt.GetValueOrDefault(node) ?? [])
        {
            if (other.Id == beam) continue;
            int m = node == other.NodeI ? other.ReleaseI : other.ReleaseJ;
            if ((m & 0b110000) != 0b110000) return true;
        }
        return false;
    }
}
