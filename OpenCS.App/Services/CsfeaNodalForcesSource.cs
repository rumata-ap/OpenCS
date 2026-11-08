using System.Globalization;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Submodel;
using CSfea.CScoreBridge.Structural;
using CSfea.Sparse;

namespace OpenCS.Services;

/// <summary>
/// Узловые силы КЭ родителя по записанным полям CSfea (спека 2026-11-05, разд. 5.2–5.3): пластины —
/// (K_e [+ K_σ,e])·u_e сборки той же модели и фабрики сечений, что у расчёта, минус согласованные силы своих нагрузок;
/// стержни — записанные силы концов (сила узла на КЭ, уже без нагрузок) в общих осях, при замороженном состоянии (P-Δ)
/// плюс K_σ,e·u_e (поля линейного расчёта пишут K·u без K_σ). Перемещения ключа — поля его шага.
/// Теги — номера модели (теги сетки родителя). Матрицы КЭ строятся по запросу и кэшируются.
/// </summary>
public sealed class CsfeaNodalForcesSource : IFragmentParentNodalForces
{
    sealed class State(RcStructuralMeshBuild build, double[] u, double[]? uDet, bool inPlane)
    {
        public RcStructuralMeshBuild Build { get; } = build;
        public double[] U { get; } = u;
        public double[]? UDet { get; } = uDet;
        public bool InPlane { get; } = inPlane;
        public Dictionary<string, IReadOnlyDictionary<string, Dof6>> Shells { get; } = new(StringComparer.Ordinal);
    }

    static readonly IReadOnlyDictionary<string, Dof6> Empty = new Dictionary<string, Dof6>(StringComparer.Ordinal);

    readonly RcStructuralModel _model;
    readonly CsfeaFieldsParentResult _fields;
    readonly IReadOnlyDictionary<FragmentMaskKey, RcSecantStepFields> _steps;
    readonly IReadOnlyDictionary<int, FragmentParentCaseLoads> _loads;
    readonly Func<int, int?> _parentLoadCase;
    readonly Func<FragmentMaskKey, (IRcSectionFactory Sections, RcSecantStepFields? Geometric, bool InPlane)> _sections;
    readonly Dictionary<FragmentMaskKey, State> _state = [];
    readonly Dictionary<IRcSectionFactory, RcStructuralMeshBuild> _builds = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<string, int> _shellIndex = new(StringComparer.Ordinal), _beamIndex = new(StringComparer.Ordinal);
    readonly Dictionary<string, (string I, string J)> _beamNodes = new(StringComparer.Ordinal);
    readonly List<(string, string?)> _links = [];
    readonly Dictionary<(FragmentMaskKey, string), IReadOnlyDictionary<string, Dof6>> _elementLoads = [];
    readonly Dictionary<(FragmentMaskKey, string), IReadOnlyList<FemLoadPiece>> _pieces = [];
    readonly Dictionary<FragmentMaskKey, IReadOnlyDictionary<string, Dof6>> _nodeLoads = [], _reactions = [];

    CsfeaNodalForcesSource(RcStructuralModel model, CsfeaFieldsParentResult fields,
        IReadOnlyDictionary<FragmentMaskKey, RcSecantStepFields> steps, IReadOnlyDictionary<int, FragmentParentCaseLoads> loads,
        Func<int, int?> parentLoadCase, Func<FragmentMaskKey, (IRcSectionFactory, RcSecantStepFields?, bool)> sections)
    {
        _model = model;
        _fields = fields;
        _steps = steps;
        _loads = loads;
        _parentLoadCase = parentLoadCase;
        _sections = sections;
        for (int i = 0; i < model.Shells.Count; i++) _shellIndex[Tag(model.Shells[i].Id)] = i;
        for (int i = 0; i < model.Beams.Count; i++)
        {
            var b = model.Beams[i];
            _beamNodes[Tag(b.Id)] = (Tag(b.NodeI), Tag(b.NodeJ));
            _beamIndex[Tag(b.Id)] = i;
        }
        foreach (var rb in model.RigidBodies)
            foreach (int s in rb.Slaves) _links.Add((Tag(rb.Master), Tag(s)));
        foreach (var sp in model.Springs) _links.Add((Tag(sp.NodeId), null));
        foreach (var sh in model.Shells)
            if (sh.FoundationC1 is > 0)
                foreach (int n in sh.NodeIds) _links.Add((Tag(n), null));
    }

    /// <summary>Линейный расчёт: одна сборка на все ключи с сечениями и K_σ настройки раннера.</summary>
    /// <param name="parentLoadCase">Id загружения ключа → Id загружения родителя (ключ <paramref name="loads"/>); null — нет.</param>
    public static CsfeaNodalForcesSource Linear(RcStructuralModel model, FemCsfeaLinearSetup setup, CsfeaFieldsParentResult fields,
        IReadOnlyDictionary<FragmentMaskKey, RcSecantStepFields> steps, IReadOnlyDictionary<int, FragmentParentCaseLoads> loads,
        Func<int, int?> parentLoadCase)
    {
        ArgumentNullException.ThrowIfNull(setup);
        IRcSectionFactory sections = (IRcSectionFactory?)setup.Sections ?? new LinearRcSectionFactory();
        return new(model, fields, steps, loads, parentLoadCase, _ => (sections, setup.GeometricFields, setup.ShellInPlanePDelta));
    }

    /// <summary>Нижний предел для записанных секущих матриц: в секущем прогоне предела не было — практически без него.</summary>
    const double SecantFloor = 1e-9;

    /// <summary>
    /// Секущий расчёт: сборка на ключ по записанным секущим матрицам его шага (<see cref="RecordedRcSectionFactory"/>, нужны
    /// <see cref="RcSecantStepFields.HasElementMatrices"/>). <paramref name="secondOrder"/> — расчёт по теории второго
    /// порядка (геометрическая нелинейность без «Больших перемещений», спека 2026-11-06, срез 2): к K_s,e добавляется
    /// K_σ,e(N) по перемещениям шага — та же матрица, на которой решено u (<c>StructuralMesh.ElementKSecondOrder</c>);
    /// <paramref name="inPlane"/> — K_σ пластин и в их плоскости.
    /// </summary>
    /// <remarks>
    /// Стержни без шарниров — тоже K_s,e·u_e − f_нагр,e по записанным матрицам: записанные силы концов — интегралы по
    /// сечению при u и отличаются от K_s·u на допуск итераций, а равновесие узлов точно только для K_s·u (u — решение на
    /// этих матрицах). У стержней с шарнирами нагрузки конденсируются в ядре — для них записанные силы концов (во втором
    /// порядке — плюс K_σ,e·u_e, как у линейного источника с P-Δ). Во втором порядке K_σ решателя — по перемещениям
    /// предыдущей итерации, здесь — по итоговым: узлы уравновешены до допуска итераций, а не точно.
    /// </remarks>
    public static CsfeaNodalForcesSource Secant(RcStructuralModel model, CsfeaFieldsParentResult fields,
        IReadOnlyDictionary<FragmentMaskKey, RcSecantStepFields> steps, IReadOnlyDictionary<int, FragmentParentCaseLoads> loads,
        Func<int, int?> parentLoadCase, bool secondOrder = false, bool inPlane = true) =>
        new(model, fields, steps, loads, parentLoadCase,
            key => (new RecordedRcSectionFactory(steps[key], SecantFloor), secondOrder ? steps[key] : null, inPlane))
        {
            _beamsByMatrix = true,
        };

    /// <summary>Стержни без шарниров — по матрице КЭ сборки (секущий источник).</summary>
    bool _beamsByMatrix;

    public IReadOnlyCollection<(string A, string? B)> NodeLinks => _links;

    public bool TryElementNodeForces(FragmentMaskKey key, string elemTag, out IReadOnlyDictionary<string, Dof6> forces)
    {
        forces = Empty;
        if (!_steps.ContainsKey(key)) return false;
        if (_beamNodes.TryGetValue(elemTag, out var ends))
        {
            if (_beamsByMatrix && _beamIndex.TryGetValue(elemTag, out int bm)
                && _model.Beams[bm] is { ReleaseI: 0, ReleaseJ: 0 })
            {
                var sst = StateOf(key);
                int e0 = sst.Build.Mesh.Shells.Count + bm;
                var kf = Multiply(sst, e0, sst.UDet != null
                    ? sst.Build.Mesh.ElementKSecondOrder(e0, sst.UDet, sst.InPlane)
                    : sst.Build.Mesh.ElementK(e0));
                var bl = ElementLoads(key, elemTag);
                forces = new Dictionary<string, Dof6>(StringComparer.Ordinal)
                {
                    [ends.I] = bl.TryGetValue(ends.I, out var li) ? kf[0] - li : kf[0],
                    [ends.J] = bl.TryGetValue(ends.J, out var lj) ? kf[1] - lj : kf[1],
                };
                return true;
            }
            if (!_fields.TryBeamEndForcesGlobal(key, elemTag, out var i, out var j)) return false;
            var bst = StateOf(key);
            if (bst.UDet != null && _beamIndex.TryGetValue(elemTag, out int b))
            {
                // Силы концов в полях — K·u без K_σ (линейный с P-Δ источника, секущий второго порядка): добавить K_σ,e·u_e.
                var g = Multiply(bst, bst.Build.Mesh.Shells.Count + b, bst.Build.Mesh.ElementKGeometric(
                    bst.Build.Mesh.Shells.Count + b, bst.UDet, bst.InPlane));
                i += g[0];
                j += g[1];
            }
            forces = new Dictionary<string, Dof6>(StringComparer.Ordinal) { [ends.I] = i, [ends.J] = j };
            return true;
        }
        if (!_shellIndex.TryGetValue(elemTag, out int e)) return false;
        var st = StateOf(key);
        if (!st.Shells.TryGetValue(elemTag, out var f)) st.Shells[elemTag] = f = ShellForces(key, st, e, elemTag);
        forces = f;
        return true;
    }

    /// <summary>k·u_e КЭ <paramref name="e"/> (единая нумерация сетки) по узлам КЭ, 6 компонент на узел.</summary>
    static Dof6[] Multiply(State st, int e, double[,] k)
    {
        var dofs = st.Build.Mesh.ElementDofs(e);
        var result = new Dof6[dofs.Length / 6];
        var v = new double[6];
        for (int a = 0; a < result.Length; a++)
        {
            for (int c = 0; c < 6; c++)
            {
                double sum = 0;
                int row = 6 * a + c;
                for (int q = 0; q < dofs.Length; q++) sum += k[row, q] * st.U[dofs[q]];
                v[c] = sum;
            }
            result[a] = new Dof6(v[0], v[1], v[2], v[3], v[4], v[5]);
        }
        return result;
    }

    State StateOf(FragmentMaskKey key)
    {
        if (_state.TryGetValue(key, out var s)) return s;
        var (sections, geometric, inPlane) = _sections(key);
        if (!_builds.TryGetValue(sections, out var build)) _builds[sections] = build = RcStructuralMeshBuilder.Build(_model, sections);
        var u = RcLinearAnalysis.StateVector(build, _steps[key]);
        var uDet = geometric != null ? RcLinearAnalysis.StateVector(build, geometric) : null;
        return _state[key] = new State(build, u, uDet, inPlane);
    }

    IReadOnlyDictionary<string, Dof6> ShellForces(FragmentMaskKey key, State st, int e, string tag)
    {
        var mesh = st.Build.Mesh;
        var k = mesh.ElementK(e);
        if (st.UDet != null) Dense.AddScaledInPlace(k, mesh.ElementKGeometric(e, st.UDet, st.InPlane), 1.0);
        var f = Multiply(st, e, k);
        var loads = ElementLoads(key, tag);
        var nodesE = mesh.Shells[e].Nodes;
        var result = new Dictionary<string, Dof6>(StringComparer.Ordinal);
        for (int a = 0; a < nodesE.Length; a++)
        {
            string n = Tag(st.Build.NodeIds[nodesE[a]]);
            result[n] = loads.TryGetValue(n, out var l) ? f[a] - l : f[a];
        }
        return result;
    }

    public IReadOnlyDictionary<string, Dof6> ElementLoads(FragmentMaskKey key, string elemTag)
    {
        if (_elementLoads.TryGetValue((key, elemTag), out var cached)) return cached;
        var result = new Dictionary<string, Dof6>(StringComparer.Ordinal);
        foreach (var (lc, factor) in key.Terms())
        {
            if (_parentLoadCase(lc) is not int p || !_loads.TryGetValue(p, out var parts)) continue;
            if (parts.Shells.TryGetValue(elemTag, out var d))
                foreach (var (n, v) in d) Add(result, n, v, factor);
            else if (parts.Beams.TryGetValue(elemTag, out var b))
            {
                Add(result, b.NodeI, b.I, factor);
                Add(result, b.NodeJ, b.J, factor);
            }
        }
        return _elementLoads[(key, elemTag)] = result;
    }

    /// <summary>Части нагрузок КЭ по виду (<see cref="FragmentParentCaseLoads.Pieces"/>) по слагаемым ключа с коэффициентами.</summary>
    public IReadOnlyList<FemLoadPiece> ElementLoadPieces(FragmentMaskKey key, string elemTag)
    {
        if (_pieces.TryGetValue((key, elemTag), out var cached)) return cached;
        var result = new List<FemLoadPiece>();
        foreach (var (lc, factor) in key.Terms())
            if (_parentLoadCase(lc) is int p && _loads.TryGetValue(p, out var parts) && parts.Pieces.TryGetValue(elemTag, out var list))
                result.AddRange(list.Select(x => x.Scale(factor)));
        return _pieces[(key, elemTag)] = result;
    }

    public IReadOnlyDictionary<string, Dof6> NodeLoads(FragmentMaskKey key)
    {
        if (_nodeLoads.TryGetValue(key, out var cached)) return cached;
        var result = new Dictionary<string, Dof6>(StringComparer.Ordinal);
        foreach (var (lc, factor) in key.Terms())
            if (_parentLoadCase(lc) is int p && _loads.TryGetValue(p, out var parts))
                foreach (var (n, v) in parts.Nodes) Add(result, n, v, factor);
        return _nodeLoads[key] = result;
    }

    public IReadOnlyDictionary<string, Dof6> Reactions(FragmentMaskKey key)
    {
        if (_reactions.TryGetValue(key, out var cached)) return cached;
        var result = new Dictionary<string, Dof6>(StringComparer.Ordinal);
        if (_steps.TryGetValue(key, out var f))
            for (int i = 0; i < f.ReactionNodeIds.Length; i++)
                Add(result, Tag(f.ReactionNodeIds[i]), f.Reactions.AsSpan(RcSecantStepFields.NodeComponents * i, 6).ToArray(), 1);
        return _reactions[key] = result;
    }

    static void Add(Dictionary<string, Dof6> d, string tag, double[] v, double factor)
    {
        var x = new Dof6(v[0], v[1], v[2], v[3], v[4], v[5]).Scale(factor);
        d[tag] = d.TryGetValue(tag, out var s) ? s + x : x;
    }

    static string Tag(int id) => id.ToString(CultureInfo.InvariantCulture);
}
