using CScore;
using CScore.Fem;
using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.CScoreBridge.Structural;

/// <summary>
/// Поля принятого шага секущего расчёта (СИ). Номера узлов и КЭ — номера модели (теги сетки схемы). Массивы — подряд
/// по объектам: перемещения — <see cref="NodeComponents"/> на узел (ux, uy, uz, м; rx, ry, rz, рад); усилия пластин —
/// <see cref="ShellForceComponents"/> на КЭ в осях выдачи (Nx, Ny, Nxy, Н/м; Mx, My, Mxy, Н·м/м; Qx, Qy, Н/м) по закону
/// сечения в центре КЭ; состояние пластин — <see cref="ShellStateComponents"/> (<see cref="PlateFieldState"/>; NaN — КЭ
/// упругий); усилия стержней — <see cref="BeamForceComponents"/> на КЭ: местные силы концов i, j (N, Qy, Qz, Mx, My,
/// Mz), действующие на КЭ, — как localForce OpenSees (при коротационной геометрической нелинейности — в осях
/// деформированного КЭ, <see cref="BeamCorotational.Beam3dLocalEndForces"/>); реакции опор — <see cref="NodeComponents"/> на узел из
/// <see cref="ReactionNodeIds"/> (глобальные оси, <see cref="RcReactionSet"/>).
/// </summary>
public sealed class RcSecantStepFields
{
    public const int NodeComponents = 6, ShellForceComponents = 8, ShellStateComponents = 5, BeamForceComponents = 12;

    /// <summary>Флаги состояния КЭ.</summary>
    public const byte Cracked = 1, Yielded = 2, Failed = 4;

    public int Stage { get; init; }
    public int Step { get; init; }
    public double LoadFactor { get; init; }
    public bool IsRefinement { get; init; }

    public required int[] NodeIds { get; init; }
    public required double[] Displacements { get; init; }
    public required int[] ShellIds { get; init; }
    public required double[] ShellForces { get; init; }
    public required double[] ShellStates { get; init; }
    public required byte[] ShellFlags { get; init; }
    public required int[] BeamIds { get; init; }
    public required double[] BeamForces { get; init; }
    public required byte[] BeamFlags { get; init; }

    /// <summary>Узлы с опорами (<see cref="RcReactionSet.NodeIds"/>); пусто — реакций в записи нет (старый формат).</summary>
    public int[] ReactionNodeIds { get; init; } = [];

    /// <summary>Реакции: Rx, Ry, Rz (Н), Rmx, Rmy, Rmz (Н·м) на узел из <see cref="ReactionNodeIds"/>.</summary>
    public double[] Reactions { get; init; } = [];

    /// <summary>Типы опор узла (<see cref="RcReactionSet.Support"/> …).</summary>
    public byte[] ReactionKinds { get; init; } = [];

    /// <summary>ΣRx, ΣRy, ΣRz (Н) по типам опор (<see cref="RcReactionSet.Sums"/>); пусто — нет данных.</summary>
    public double[] ReactionSums { get; init; } = [];

    /// <summary>ΣFx, ΣFy, ΣFz (Н) узловой нагрузки шага; пусто — нет данных.</summary>
    public double[] LoadSum { get; init; } = [];

    /// <summary>
    /// Компоненты блоков жёсткостей, деформаций сечений и полных матриц на КЭ:
    /// <see cref="ShellStiffness"/>, <see cref="BeamStiffness"/>, <see cref="ShellStrains"/>, <see cref="BeamStrains"/>,
    /// <see cref="ShellMatrices"/>, <see cref="BeamMatrices"/>.
    /// </summary>
    public const int ShellStiffnessComponents = 17, BeamStiffnessComponents = 21, ShellStrainComponents = 6,
        BeamStrainComponents = 9, ShellMatrixComponents = 31, BeamMatrixComponents = 33;

    /// <summary>Есть блок жёсткостей (записи до 17.10.2026 — нет).</summary>
    public bool HasStiffness { get; init; }

    /// <summary>
    /// Жёсткости пластин в осях выдачи усилий (<see cref="ShellStiffnessComponents"/> на КЭ): секущие A11, A22, A12, A66
    /// (Н/м), D11, D22, D12, D66 (Н·м) по центру КЭ, затем те же начальные и толщина сечения h (м; NaN — не задана).
    /// Упругий КЭ — секущие = начальные.
    /// </summary>
    public double[] ShellStiffness { get; init; } = [];

    /// <summary>
    /// Жёсткости стержней в осях КЭ (<see cref="BeamStiffnessComponents"/> на КЭ): в точках ξ = 0, ½, 1 секущие EA (Н),
    /// EIy, EIz (Н·м²) — диагональ секущей матрицы сечения точки, GAy, GAz (Н; КЭ Тимошенко — одни на КЭ, +∞ — без
    /// сдвига); затем начальные EA₀, EIy₀, EIz₀, GAy₀, GAz₀ и упругая GJ (Н·м²).
    /// </summary>
    public double[] BeamStiffness { get; init; } = [];

    /// <summary>Есть блок деформаций сечений (<see cref="RcSecantOptions.RecordSectionStrains"/>).</summary>
    public bool HasStrains { get; init; }

    /// <summary>Деформации центра пластины в осях выдачи усилий: εx, εy, γxy, κx, κy, κxy (1/м).</summary>
    public double[] ShellStrains { get; init; } = [];

    /// <summary>
    /// Деформации стержня в точках ξ = 0, ½, 1 (оси КЭ): ε₀, κy, κz (1/м). Пишутся всегда (записи с 18.10.2026), без
    /// галки — <see cref="HasBeamStrains"/>; деформации пластин — только по галке.
    /// </summary>
    public double[] BeamStrains { get; init; } = [];

    /// <summary>Есть деформации всех стержней (записи с 18.10.2026 или с галкой деформаций сечений).</summary>
    public bool HasBeamStrains => BeamStrains.Length == BeamIds.Length * BeamStrainComponents;

    /// <summary>Есть полные секущие матрицы (конечное состояние прогона).</summary>
    public bool HasMatrices { get; init; }

    /// <summary>
    /// Полная секущая матрица пластины в осях сечения (армирования; у повёрнутого сечения сетки — оси внутреннего):
    /// A, B, D (3 × 3 по строкам) и As (2 × 2) — <see cref="ShellMatrixComponents"/> на КЭ, единицы CSfea.
    /// </summary>
    public double[] ShellMatrices { get; init; } = [];

    /// <summary>
    /// Полные секущие стержня в точках ξ = 0, ½, 1 (оси КЭ): матрица сечения 3 × 3 по строкам и GAy, GAz —
    /// <see cref="BeamMatrixComponents"/> на КЭ.
    /// </summary>
    public double[] BeamMatrices { get; init; } = [];

    public static byte Flags(SecantSectionStatus s) =>
        (byte)((s.Cracked ? Cracked : 0) | (s.Yielded ? Yielded : 0) | (s.Failed ? Failed : 0));
}

/// <summary>
/// Снятие полей шага секущего расчёта (<see cref="RcSecantStepFields"/>) по перемещениям шага и зафиксированным
/// состояниям КЭ — вызывать из <see cref="SecantPicardOptions.OnStepAccepted"/>, пока состояния отвечают шагу.
/// Пролётные нагрузки стержней — по стадиям модели (накопленные, как в <see cref="RcSecantAnalysis"/>): усилия концов
/// = K·d − согласованные силы нагрузки шага. При коротационной геометрической нелинейности концы — в осях
/// деформированного КЭ (CR-базис): локальные силы КЭ минус согласованные силы, повёрнутые в эти оси (решатель
/// прикладывал их в глобальных осях по недеформированной геометрии — так концы согласованы с решённой моделью).
/// </summary>
public sealed class RcSecantFieldExtractor
{
    private readonly RcStructuralMeshBuild _build;
    private readonly ISecantShellState?[] _shells;
    private readonly ISecantBeamState?[] _beams;
    private readonly bool _geometric;
    private readonly bool _shellCr;   // оболочки — CR (геомнелин с P-Δ в плоскости), иначе линейно / фон Карман
    private readonly bool _strains;
    private readonly IReadOnlyDictionary<int, double> _forceAngles;
    private readonly List<Dictionary<int, double[]>> _stageBeamLoads = new();
    private readonly List<double[]> _stageLoadSums = new();   // ΣF конца стадии (накопленные)
    private readonly Dictionary<int, double> _thickness;      // номер оболочки → толщина сечения, м

    /// <param name="beams">Состояния стержней (null — все упругие).</param>
    /// <param name="recordStrains">Писать деформации сечений (<see cref="RcSecantStepFields.HasStrains"/>).</param>
    public RcSecantFieldExtractor(RcStructuralModel model, RcStructuralMeshBuild build, ISecantShellState?[] shells,
        bool geometric, IReadOnlyDictionary<int, double>? shellForceAngles = null, bool shellInPlanePDelta = true,
        ISecantBeamState?[]? beams = null, bool recordStrains = false)
    {
        _build = build;
        _shells = shells;
        _beams = beams ?? new ISecantBeamState?[build.Mesh.Beams.Count];
        _strains = recordStrains;
        _thickness = new Dictionary<int, double>();
        foreach (var s in model.Shells) _thickness[s.Id] = s.Section.Elastic?.H ?? s.Section.Plate?.H ?? double.NaN;
        _geometric = geometric;
        _shellCr = geometric && shellInPlanePDelta;
        _forceAngles = shellForceAngles ?? new Dictionary<int, double>();
        var acc = new Dictionary<int, double[]>();
        var sum = new double[3];
        foreach (var st in model.Stages)
        {
            sum = sum.Zip(RcReactions.LoadSum(build.Combination(st.Loads)), (a, b) => a + b).ToArray();
            _stageLoadSums.Add(sum);
            var next = acc.ToDictionary(kv => kv.Key, kv => (double[])kv.Value.Clone());
            foreach (var (e, fe) in build.BeamCombination(st.Loads))
            {
                if (!next.TryGetValue(e, out var a)) next[e] = a = new double[12];
                for (int i = 0; i < 12; i++) a[i] += fe[i];
            }
            _stageBeamLoads.Add(next);
            acc = next;
        }
    }

    /// <param name="matrices">Добавить полные секущие матрицы КЭ (<see cref="RcSecantStepFields.HasMatrices"/>) —
    /// у конечного состояния прогона.</param>
    public RcSecantStepFields Extract(SecantStepResult step, bool matrices = false)
    {
        var loadsEnd = step.Stage < _stageBeamLoads.Count ? _stageBeamLoads[step.Stage] : null;
        var loadsStart = step.Stage > 0 && step.Stage - 1 < _stageBeamLoads.Count ? _stageBeamLoads[step.Stage - 1] : null;
        return Extract(step.U, step.Stage, step.Step, step.LoadFactor, step.IsRefinement, loadsStart, loadsEnd,
            step.Shells, step.Beams, Reactions(step), LoadSum(step), matrices);
    }

    /// <summary>Реакции опор принятого шага; у несошедшегося шага — только пружины.</summary>
    public RcReactionSet Reactions(SecantStepResult step) => RcReactions.Compute(_build, step.U, step.Reactions);

    /// <summary>ΣFx, ΣFy, ΣFz (Н) узловой нагрузки шага (стадии накапливаются).</summary>
    public double[] LoadSum(SecantStepResult step)
    {
        if (step.Stage >= _stageLoadSums.Count) return new double[3];
        var end = _stageLoadSums[step.Stage];
        var start = step.Stage > 0 ? _stageLoadSums[step.Stage - 1] : new double[3];
        return Enumerable.Range(0, 3).Select(i => start[i] + step.LoadFactor * (end[i] - start[i])).ToArray();
    }

    /// <summary>
    /// Поля независимого линейного решения <paramref name="u"/> (<see cref="RcLinearAnalysis"/>): загружение с номером
    /// <paramref name="caseIndex"/> — «стадия» из одного шага, <paramref name="beamLoads"/> — пролётные нагрузки стержней
    /// этого загружения (без накопления), <paramref name="fixedReactions"/> — реакции закреплённых DOF
    /// (<see cref="StructuralMesh.ComputeReactions"/>), <paramref name="load"/> — узловой вектор нагрузки загружения.
    /// </summary>
    public RcSecantStepFields ExtractLinear(double[] u, int caseIndex, IReadOnlyDictionary<int, double[]>? beamLoads,
        double[]? fixedReactions = null, double[]? load = null)
        => Extract(u, caseIndex, 1, 1.0, false, null, beamLoads, [], [],
            fixedReactions != null ? RcReactions.Compute(_build, u, fixedReactions) : null,
            load != null ? RcReactions.LoadSum(load) : null, matrices: false);

    private RcSecantStepFields Extract(double[] u, int stage, int stepNumber, double loadFactor, bool isRefinement,
        IReadOnlyDictionary<int, double[]>? loadsStart, IReadOnlyDictionary<int, double[]>? loadsEnd,
        SecantSectionStatus[] shellStatus, SecantSectionStatus[] beamStatus, RcReactionSet? reactions, double[]? loadSum,
        bool matrices)
    {
        var mesh = _build.Mesh;
        int nn = mesh.NNodes;
        var disp = new double[nn * RcSecantStepFields.NodeComponents];
        Array.Copy(u, disp, Math.Min(u.Length, disp.Length));

        int ns = mesh.Shells.Count;
        var shellForces = new double[ns * RcSecantStepFields.ShellForceComponents];
        var shellStates = new double[ns * RcSecantStepFields.ShellStateComponents];
        var shellFlags = new byte[ns];
        var shellStiffness = new double[ns * RcSecantStepFields.ShellStiffnessComponents];
        var shellStrains = _strains ? new double[ns * RcSecantStepFields.ShellStrainComponents] : [];
        var shellMatrices = matrices ? new double[ns * RcSecantStepFields.ShellMatrixComponents] : [];
        Parallel.For(0, ns, e =>
        {
            var sh = mesh.Shells[e];
            var dofs = StructuralMesh.NodeDofs(sh.Nodes);
            var ue = dofs.Select(d => u[d]).ToArray();
            var (eps, kappa, gamma) = _shellCr
                ? ShellCorotational.CenterStrainsCR(mesh.ShellCoords(e), ue)
                : ShellElementForces.CenterStrainsGlobal(mesh.ShellCoords(e), ue, vonKarman: _geometric);
            IShellSectionResponse sec = sh.Section;
            if (sec is RotatedShellResponse rot)
            {
                (eps, kappa, gamma) = rot.ToSection(eps, kappa, gamma);
                sec = rot.Inner;
            }
            var f = _shells[e] is { } st ? st.TrueForces(eps, kappa, gamma) : sec.Forces(eps, kappa, gamma);
            var item = new ShellLoadItem
            {
                Nx = f.N[0], Ny = f.N[1], Nxy = f.N[2], Mx = f.M[0], My = f.M[1], Mxy = f.M[2], Qx = f.Q[0], Qy = f.Q[1],
            };
            _forceAngles.TryGetValue(_build.ShellIds[e], out double angle);
            if (angle != 0)
                item = ShellForceTransform.Rotate(item, -angle);

            // Жёсткости: секущая — матрица сечения в сетке (после сходимости — последняя оценка), начальная — Initial;
            // у упругого КЭ обе — его матрица. Из осей сечения в оси выдачи — тензорным поворотом.
            var secant = _shells[e] is { } s0 ? s0.Response.Matrix : sec.Tangent(new double[3], new double[3], new double[2]);
            var initial = _shells[e] is { } s1 ? s1.Initial : secant;
            int ko = e * RcSecantStepFields.ShellStiffnessComponents;
            ShellStiffnessTerms(secant, angle, shellStiffness, ko);
            ShellStiffnessTerms(initial, angle, shellStiffness, ko + 8);
            shellStiffness[ko + 16] = _thickness.GetValueOrDefault(_build.ShellIds[e], double.NaN);
            if (_strains)
            {
                var (er, kr) = (RotateStrains(eps, angle), RotateStrains(kappa, angle));
                int so6 = e * RcSecantStepFields.ShellStrainComponents;
                for (int i = 0; i < 3; i++) { shellStrains[so6 + i] = er[i]; shellStrains[so6 + 3 + i] = kr[i]; }
            }
            if (matrices)
            {
                int mo = e * RcSecantStepFields.ShellMatrixComponents;
                foreach (var blk in new[] { secant.A, secant.B, secant.D, secant.As })
                    foreach (double v in blk) shellMatrices[mo++] = v;
            }
            int o = e * RcSecantStepFields.ShellForceComponents;
            shellForces[o] = item.Nx; shellForces[o + 1] = item.Ny; shellForces[o + 2] = item.Nxy;
            shellForces[o + 3] = item.Mx; shellForces[o + 4] = item.My; shellForces[o + 5] = item.Mxy;
            shellForces[o + 6] = item.Qx; shellForces[o + 7] = item.Qy;

            int so = e * RcSecantStepFields.ShellStateComponents;
            if (_shells[e] is PlateSecantShellState plate)
            {
                var fs = plate.FieldState(eps, kappa);
                shellStates[so] = fs.CrackBottom; shellStates[so + 1] = fs.CrackTop; shellStates[so + 2] = fs.PsiMin;
                shellStates[so + 3] = fs.SigmaRatio; shellStates[so + 4] = fs.EpsRatio;
            }
            else
                for (int i = 0; i < RcSecantStepFields.ShellStateComponents; i++) shellStates[so + i] = double.NaN;
            if (e < shellStatus.Length) shellFlags[e] = RcSecantStepFields.Flags(shellStatus[e]);
        });

        int nb = mesh.Beams.Count;
        var beamForces = new double[nb * RcSecantStepFields.BeamForceComponents];
        var beamFlags = new byte[nb];
        var beamStiffness = new double[nb * RcSecantStepFields.BeamStiffnessComponents];
        // Деформации стержней — всегда (по ним ширина раскрытия трещин), пластин — по галке.
        var beamStrains = new double[nb * RcSecantStepFields.BeamStrainComponents];
        var beamMatrices = matrices ? new double[nb * RcSecantStepFields.BeamMatrixComponents] : [];
        // Матрица и внутренние силы КЭ — по текущей (зафиксированной) жёсткости сечения, только чтение.
        Parallel.For(0, nb, e =>
        {
            var b = mesh.Beams[e];
            var coords = mesh.BeamCoords(e);
            var ue = StructuralMesh.NodeDofs(new[] { b.I, b.J }).Select(d => u[d]).ToArray();
            double[] local;
            double[,] rot;   // глобальные → оси КЭ, по тройкам
            if (_geometric)
                (local, rot) = BeamCorotational.Beam3dLocalEndForces(coords, b.Section, ue, b.RefVec, b.Releases);
            else
            {
                var (t, _) = BeamElements.Beam3dT(coords, b.RefVec);
                local = Dense.MatVec(t, Dense.MatVec(BeamElements.Beam3dKGlobal(coords, b.Section, b.RefVec, b.Releases), ue));
                rot = new double[3, 3];
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) rot[i, j] = t[i, j];
            }
            double[]? fe0 = loadsStart?.GetValueOrDefault(e), fe1 = loadsEnd?.GetValueOrDefault(e);
            if (fe0 != null || fe1 != null)
                for (int blk = 0; blk < 4; blk++)
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 3; j++)
                        {
                            int g = 3 * blk + j;
                            double a = fe0?[g] ?? 0.0, c = fe1?[g] ?? 0.0;
                            local[3 * blk + i] -= rot[i, j] * (a + loadFactor * (c - a));
                        }
            Array.Copy(local, 0, beamForces, e * RcSecantStepFields.BeamForceComponents, 12);
            if (e < beamStatus.Length) beamFlags[e] = RcSecantStepFields.Flags(beamStatus[e]);

            // Жёсткости: секущие сечений точек последней оценки (у упругого КЭ — его матрица), сдвиг и GJ — КЭ.
            var (s, gj, shear) = b.Section is SecantBeamResponse r
                ? (r.Matrix, r.GJ, r.Shear)
                : (b.Section.Tangent(0, 0, 0), b.Section.TorsionalStiffness(), BeamShearStiffness.Rigid);
            var state = _beams[e];
            IReadOnlyList<double[,]> points = state is CrossSectionSecantBeamState cs ? cs.PointSecants : [s, s, s];
            var s0 = state?.Initial ?? s;
            var shear0 = state?.InitialShear ?? shear;
            int ko = e * RcSecantStepFields.BeamStiffnessComponents;
            for (int p = 0; p < 3; p++)
            {
                beamStiffness[ko + 5 * p] = points[p][0, 0];
                beamStiffness[ko + 5 * p + 1] = points[p][1, 1];
                beamStiffness[ko + 5 * p + 2] = points[p][2, 2];
                beamStiffness[ko + 5 * p + 3] = shear.GAvY;
                beamStiffness[ko + 5 * p + 4] = shear.GAvZ;
            }
            beamStiffness[ko + 15] = s0[0, 0]; beamStiffness[ko + 16] = s0[1, 1]; beamStiffness[ko + 17] = s0[2, 2];
            beamStiffness[ko + 18] = shear0.GAvY; beamStiffness[ko + 19] = shear0.GAvZ; beamStiffness[ko + 20] = gj;
            {
                var (pts, _, _) = SecantPicardSolver.BeamPointStrains(mesh, e, u, s, gj, shear, _geometric);
                int so = e * RcSecantStepFields.BeamStrainComponents;
                for (int p = 0; p < 3; p++)
                {
                    beamStrains[so + 3 * p] = pts[p].Eps0;
                    beamStrains[so + 3 * p + 1] = pts[p].KappaY;
                    beamStrains[so + 3 * p + 2] = pts[p].KappaZ;
                }
            }
            if (matrices)
            {
                int mo = e * RcSecantStepFields.BeamMatrixComponents;
                for (int p = 0; p < 3; p++)
                {
                    foreach (double v in points[p]) beamMatrices[mo++] = v;
                    beamMatrices[mo++] = shear.GAvY;
                    beamMatrices[mo++] = shear.GAvZ;
                }
            }
        });

        return new RcSecantStepFields
        {
            Stage = stage, Step = stepNumber, LoadFactor = loadFactor, IsRefinement = isRefinement,
            NodeIds = _build.NodeIds, Displacements = disp,
            ShellIds = _build.ShellIds, ShellForces = shellForces, ShellStates = shellStates, ShellFlags = shellFlags,
            BeamIds = _build.BeamIds, BeamForces = beamForces, BeamFlags = beamFlags,
            ReactionNodeIds = reactions?.NodeIds ?? [], Reactions = reactions?.Values ?? [],
            ReactionKinds = reactions?.Kinds ?? [], ReactionSums = reactions?.Sums ?? [], LoadSum = loadSum ?? [],
            HasStiffness = true, ShellStiffness = shellStiffness, BeamStiffness = beamStiffness,
            HasStrains = _strains, ShellStrains = shellStrains, BeamStrains = beamStrains,
            HasMatrices = matrices, ShellMatrices = shellMatrices, BeamMatrices = beamMatrices,
        };
    }

    /// <summary>
    /// Члены A11, A22, A12, A66, D11, D22, D12, D66 матрицы сечения <paramref name="t"/> (оси сечения) в осях выдачи,
    /// ось x которых повёрнута на <paramref name="angleDeg"/> от оси x сечения: A′ = T·A·Tᵀ, T — поворот усилий.
    /// </summary>
    internal static void ShellStiffnessTerms(ShellTangent t, double angleDeg, double[] dst, int offset)
    {
        var (a, d) = angleDeg == 0 ? (t.A, t.D) : (RotateMatrix(t.A, angleDeg), RotateMatrix(t.D, angleDeg));
        dst[offset] = a[0, 0]; dst[offset + 1] = a[1, 1]; dst[offset + 2] = a[0, 1]; dst[offset + 3] = a[2, 2];
        dst[offset + 4] = d[0, 0]; dst[offset + 5] = d[1, 1]; dst[offset + 6] = d[0, 1]; dst[offset + 7] = d[2, 2];
    }

    /// <summary>
    /// Поворот усилий (Nx, Ny, Nxy) в оси, повёрнутые на <paramref name="angleDeg"/> против часовой стрелки:
    /// T = [[c², s², 2sc], [s², c², −2sc], [−sc, sc, c² − s²]] (как <see cref="ShellForceTransform.Rotate"/> с −угол).
    /// </summary>
    private static double[,] StressRotation(double angleDeg)
    {
        double a = angleDeg * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
        return new[,] { { c * c, s * s, 2 * s * c }, { s * s, c * c, -2 * s * c }, { -s * c, s * c, c * c - s * s } };
    }

    /// <summary>T·M·Tᵀ: матрица сечения (N по ε, γ — инженерная) в повёрнутых осях.</summary>
    internal static double[,] RotateMatrix(double[,] m, double angleDeg)
    {
        var t = StressRotation(angleDeg);
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double v = 0;
                for (int k = 0; k < 3; k++)
                    for (int l = 0; l < 3; l++)
                        v += t[i, k] * m[k, l] * t[j, l];
                r[i, j] = v;
            }
        return r;
    }

    /// <summary>Деформации (εx, εy, γxy) или кривизны (κx, κy, κxy; сдвиговые — инженерные) в повёрнутых осях.</summary>
    public static double[] RotateStrains(double[] e, double angleDeg)
    {
        if (angleDeg == 0) return [e[0], e[1], e[2]];
        double a = angleDeg * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
        return [c * c * e[0] + s * s * e[1] + s * c * e[2],
                s * s * e[0] + c * c * e[1] - s * c * e[2],
                -2 * s * c * e[0] + 2 * s * c * e[1] + (c * c - s * s) * e[2]];
    }
}
