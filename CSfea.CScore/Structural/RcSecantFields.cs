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
/// Mz), действующие на КЭ, — как localForce OpenSees.
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

    public static byte Flags(SecantSectionStatus s) =>
        (byte)((s.Cracked ? Cracked : 0) | (s.Yielded ? Yielded : 0) | (s.Failed ? Failed : 0));
}

/// <summary>
/// Снятие полей шага секущего расчёта (<see cref="RcSecantStepFields"/>) по перемещениям шага и зафиксированным
/// состояниям КЭ — вызывать из <see cref="SecantPicardOptions.OnStepAccepted"/>, пока состояния отвечают шагу.
/// Пролётные нагрузки стержней — по стадиям модели (накопленные, как в <see cref="RcSecantAnalysis"/>): усилия концов
/// = K·d − согласованные силы нагрузки шага.
/// </summary>
public sealed class RcSecantFieldExtractor
{
    private readonly RcStructuralMeshBuild _build;
    private readonly ISecantShellState?[] _shells;
    private readonly bool _geometric;
    private readonly bool _shellCr;   // оболочки — CR (геомнелин с P-Δ в плоскости), иначе линейно / фон Карман
    private readonly IReadOnlyDictionary<int, double> _forceAngles;
    private readonly List<Dictionary<int, double[]>> _stageBeamLoads = new();

    public RcSecantFieldExtractor(RcStructuralModel model, RcStructuralMeshBuild build, ISecantShellState?[] shells,
        bool geometric, IReadOnlyDictionary<int, double>? shellForceAngles = null, bool shellInPlanePDelta = true)
    {
        _build = build;
        _shells = shells;
        _geometric = geometric;
        _shellCr = geometric && shellInPlanePDelta;
        _forceAngles = shellForceAngles ?? new Dictionary<int, double>();
        var acc = new Dictionary<int, double[]>();
        foreach (var st in model.Stages)
        {
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

    public RcSecantStepFields Extract(SecantStepResult step)
    {
        var mesh = _build.Mesh;
        var u = step.U;
        int nn = mesh.NNodes;
        var disp = new double[nn * RcSecantStepFields.NodeComponents];
        Array.Copy(u, disp, Math.Min(u.Length, disp.Length));

        int ns = mesh.Shells.Count;
        var shellForces = new double[ns * RcSecantStepFields.ShellForceComponents];
        var shellStates = new double[ns * RcSecantStepFields.ShellStateComponents];
        var shellFlags = new byte[ns];
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
            if (_forceAngles.TryGetValue(_build.ShellIds[e], out double angle) && angle != 0)
                item = ShellForceTransform.Rotate(item, -angle);
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
            if (e < step.Shells.Length) shellFlags[e] = RcSecantStepFields.Flags(step.Shells[e]);
        });

        int nb = mesh.Beams.Count;
        var beamForces = new double[nb * RcSecantStepFields.BeamForceComponents];
        var beamFlags = new byte[nb];
        var loadsEnd = step.Stage < _stageBeamLoads.Count ? _stageBeamLoads[step.Stage] : null;
        var loadsStart = step.Stage > 0 && step.Stage - 1 < _stageBeamLoads.Count ? _stageBeamLoads[step.Stage - 1] : null;
        for (int e = 0; e < nb; e++)
        {
            var b = mesh.Beams[e];
            var coords = mesh.BeamCoords(e);
            var ue = StructuralMesh.NodeDofs(new[] { b.I, b.J }).Select(d => u[d]).ToArray();
            var (t, l) = BeamElements.Beam3dT(coords, b.RefVec);
            var fGlobal = _geometric
                ? BeamCorotational.Beam3dInternalForce(coords, b.Section, ue, b.RefVec, b.Releases)
                : Dense.MatVec(BeamElements.Beam3dKGlobal(coords, b.Section, b.RefVec, b.Releases), ue);
            double[]? fe0 = loadsStart?.GetValueOrDefault(e), fe1 = loadsEnd?.GetValueOrDefault(e);
            if (fe0 != null || fe1 != null)
                for (int i = 0; i < 12; i++)
                {
                    double a = fe0?[i] ?? 0.0, c = fe1?[i] ?? 0.0;
                    fGlobal[i] -= a + step.LoadFactor * (c - a);
                }
            var local = Dense.MatVec(t, fGlobal);
            Array.Copy(local, 0, beamForces, e * RcSecantStepFields.BeamForceComponents, 12);
            if (e < step.Beams.Length) beamFlags[e] = RcSecantStepFields.Flags(step.Beams[e]);
        }

        return new RcSecantStepFields
        {
            Stage = step.Stage, Step = step.Step, LoadFactor = step.LoadFactor, IsRefinement = step.IsRefinement,
            NodeIds = _build.NodeIds, Displacements = disp,
            ShellIds = _build.ShellIds, ShellForces = shellForces, ShellStates = shellStates, ShellFlags = shellFlags,
            BeamIds = _build.BeamIds, BeamForces = beamForces, BeamFlags = beamFlags,
        };
    }
}
