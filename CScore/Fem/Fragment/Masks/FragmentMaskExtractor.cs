using System.Globalization;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>Сечение рассечения на входе экстрактора: точка конца стержня или нормаль стены (из КонЭ фрагмента).</summary>
public sealed record FragmentMaskCutInput(FragmentCut Cut, PlanarVector3? EndPoint, PlanarVector3? WallNormal);

/// <summary>Вход снятия масок: сечения фрагмента, сетка родителя полосы, результат родителя.</summary>
public sealed class FragmentMaskInput
{
    public IReadOnlyList<FragmentMaskCutInput> Cuts { get; init; } = [];
    public IReadOnlyDictionary<string, FemMeshNode> ParentNodes { get; init; } = new Dictionary<string, FemMeshNode>();
    public IReadOnlyList<FemElement> ParentElements { get; init; } = [];
    public required IFragmentParentResult Result { get; init; }
    public double ToleranceM { get; init; } = 0.001;
}

/// <summary>Итог снятия: маски по сечениям и ключам, диагностика (только предупреждения).</summary>
public sealed class FragmentMaskExtraction
{
    public List<FragmentCutMask> Masks { get; } = [];
    public List<FemValidationDiagnostic> Diagnostics { get; } = [];
}

/// <summary>Снятие масок по всем сечениям и ключам источника (спека разд. 4.2–4.4).</summary>
public static class FragmentMaskExtractor
{
    /// <summary>Допуск сверки равнодействующей сечения (спека 4.4).</summary>
    public const double CutResultantTolerance = 0.02;

    /// <summary>
    /// Нижняя граница масштаба сверки равнодействующей — доля Σ|F| масок того же ключа по всем сечениям: на почти
    /// ненагруженном сечении (верх стены) относительное расхождение патча с прямой суммой велико, но ничтожно для
    /// равновесия фрагмента. При 0,5 порог — 1 % Σ|F|, половина допуска равновесия масок
    /// (<see cref="FragmentMaskBalance.BalanceTolerance"/> от нагрузки полосы ≈ Σ|F| / 2).
    /// </summary>
    public const double CutResultantKeyShare = 0.5;

    /// <summary>Отложенная сверка кромки: сравнивается, когда известна Σ|F| масок ключа.</summary>
    sealed record ResultantCheck(FragmentMaskCutInput Cut, FragmentMaskKey Key, string Where, double Length,
        PlanarVector3 MaskF, PlanarVector3 MaskM, PlanarVector3 DirectF, PlanarVector3 DirectM);

    public static FragmentMaskExtraction Extract(FragmentMaskInput input)
    {
        var x = new FragmentMaskExtraction();
        var bars = new List<FragmentBarElement>();
        var shells = new List<(string, FemElementGeometry)>();
        foreach (var e in input.ParentElements)
        {
            if (FemElementGeometry.Of(e, input.ParentNodes) is not { } g) continue;
            if (g.IsBar) bars.Add(new FragmentBarElement(e.ElemTag, g.NodeTags[0], g.NodeTags[1], g.Points[0], g.Points[1],
                e.BeamRotationDeg ?? 0));
            else if (g.IsShell) shells.Add((e.ElemTag, g));
        }

        var checks = new List<ResultantCheck>();
        for (int i = 0; i < input.Cuts.Count; i++)
        {
            var c = input.Cuts[i];
            bool below = c.Cut.Side == FragmentCutSide.Bottom;
            string where = $"{c.Cut.MemberTag} ({(below ? "низ" : "верх")}, z = {c.Cut.Z.ToString("0.###", CultureInfo.CurrentCulture)})";
            if (c.Cut.Kind == FragmentCutKind.End)
                ExtractEnd(input, x, i, c, below, where, bars);
            else
                ExtractEdge(input, x, i, c, below, where, shells, checks);
        }
        CheckResultants(input, x, checks);
        return x;
    }

    /// <summary>
    /// Сверка равнодействующей кромки с прямой суммой по КЭ (спека 4.4): масштаб — сила сечения, но не меньше
    /// <see cref="CutResultantKeyShare"/> от Σ|F| масок ключа.
    /// </summary>
    static void CheckResultants(FragmentMaskInput input, FragmentMaskExtraction x, List<ResultantCheck> checks)
    {
        if (checks.Count == 0) return;
        var keyTotal = new Dictionary<FragmentMaskKey, double>();
        foreach (var m in x.Masks)
            if (FragmentMaskBalance.Resultant(input.Cuts[m.CutIndex], m.Samples, PlanarVector3.Zero) is { } r)
                keyTotal[m.Key] = keyTotal.GetValueOrDefault(m.Key) + r.F.Length;
        foreach (var k in checks)
        {
            if (k.DirectF.Length <= 1e-6) continue;
            double floor = CutResultantKeyShare * keyTotal.GetValueOrDefault(k.Key);
            double scaleF = Math.Max(Math.Max(k.DirectF.Length, floor), 1e-9);
            double scaleM = Math.Max(k.DirectM.Length + Math.Max(k.DirectF.Length, floor) * k.Length / 2, 1e-9);
            double dF = (k.MaskF - k.DirectF).Length / scaleF, dM = (k.MaskM - k.DirectM).Length / scaleM;
            if (dF > CutResultantTolerance || dM > CutResultantTolerance)
                Warn(x, "fragment_mask_cut_resultant", string.Create(CultureInfo.CurrentCulture,
                    $"Сечение {k.Where}, {KeyText(k.Key)}: равнодействующая маски расходится с суммой по КЭ на {Math.Max(dF, dM):P1}."), k.Cut);
        }
    }

    static void ExtractEnd(FragmentMaskInput input, FragmentMaskExtraction x, int index, FragmentMaskCutInput c, bool below,
        string where, List<FragmentBarElement> bars)
    {
        if (c.EndPoint is not { } point
            || FragmentBarCutSampler.Locate(point, below, bars, input.ToleranceM) is not { } hit)
        {
            Warn(x, "fragment_mask_cut_unmatched", $"Сечение {where}: нет КЭ стержня родителя в точке рассечения — маски нет.", c);
            return;
        }
        foreach (var key in input.Result.Keys)
        {
            var s = FragmentBarCutSampler.Sample(hit, input.Result, key, out bool loadsUnknown);
            x.Masks.Add(new FragmentCutMask(index, key, [s]));
            if (s.U == null) Warn(x, "fragment_mask_no_displacement", $"Сечение {where}, {KeyText(key)}: нет перемещений узлов КЭ {hit.ElemTag}.", c);
            if (s.F == null) Warn(x, "fragment_mask_no_forces", $"Сечение {where}, {KeyText(key)}: нет усилий КЭ {hit.ElemTag}.", c);
            if (loadsUnknown) Warn(x, "fragment_mask_beam_loads_unknown",
                $"Сечение {where}, {KeyText(key)}: нагрузки КЭ {hit.ElemTag} источнику неизвестны — усилие без нагрузки участка.", c);
        }
    }

    static void ExtractEdge(FragmentMaskInput input, FragmentMaskExtraction x, int index, FragmentMaskCutInput c, bool below,
        string where, List<(string, FemElementGeometry)> shells, List<ResultantCheck> checks)
    {
        if (c.WallNormal is not { } normal || c.Cut.Segments.Count == 0)
        {
            Warn(x, "fragment_mask_cut_unmatched", $"Сечение {where}: нет плоскости стены — маски нет.", c);
            return;
        }
        var geometries = c.Cut.Segments
            .Select(seg => FragmentWallCutSampler.Prepare(seg.P, seg.Q, normal, below, shells, input.ToleranceM)).ToList();
        if (geometries.All(g => g.Plane.Count == 0))
        {
            Warn(x, "fragment_mask_cut_unmatched", $"Сечение {where}: нет КЭ пластин родителя в плоскости стены — маски нет.", c);
            return;
        }
        foreach (var key in input.Result.Keys)
        {
            var samples = new List<FragmentMaskSample>();
            int degenerate = 0;
            bool noU = false, noF = false;
            double offset = 0;
            var direct = (F: PlanarVector3.Zero, M: PlanarVector3.Zero);
            Dof6? Traction(string tag, PlanarVector3 n) =>
                input.Result.TryShellForces(key, tag, out var f) ? FragmentShellForceRecovery.Traction(f, n) : null;
            foreach (var g in geometries)
            {
                foreach (var p in g.Points)
                {
                    var u = FragmentWallCutSampler.Displacement(p,
                        tag => input.Result.TryNodeDisplacement(key, tag, out var d) ? d : null);
                    var f = FragmentShellForceRecovery.Recover(g, p, tag => Traction(tag, g.CutNormal), out bool deg);
                    if (deg) degenerate++;
                    noU |= u == null;
                    noF |= f == null;
                    samples.Add(new FragmentMaskSample(offset + p.S, u, f));
                }
                foreach (var (s0, s1, e) in g.Intervals)
                    if (Traction(e.Tag, g.CutNormal) is { } t)
                    {
                        double len = s1 - s0;
                        var mid = g.P + g.Axis * ((s0 + s1) / 2);
                        direct.F += t.Force * len;
                        direct.M += t.Moment * len + mid.Cross(t.Force * len);
                    }
                offset += g.Length;
            }
            x.Masks.Add(new FragmentCutMask(index, key, samples));
            if (noU) Warn(x, "fragment_mask_no_displacement", $"Сечение {where}, {KeyText(key)}: в части точек нет перемещений источника.", c);
            if (noF) Warn(x, "fragment_mask_no_forces", $"Сечение {where}, {KeyText(key)}: в части точек нет усилий КЭ источника.", c);
            if (degenerate > 0) Warn(x, "fragment_mask_patch_degenerate",
                $"Сечение {where}, {KeyText(key)}: в {degenerate} точках патч КЭ вырожден — усилия по ближайшим центрам.", c);
            if (!noF && FragmentMaskBalance.Resultant(c, samples, PlanarVector3.Zero) is { } res)
                checks.Add(new(c, key, where, geometries.Sum(g => g.Length), res.F, res.M, direct.F, direct.M));
        }
    }

    static string KeyText(FragmentMaskKey key) => key.Kind == FragmentMaskKeyKind.LoadCase
        ? $"загружение #{key.LoadCaseId}" : $"сочетание {key.StageExpressionJson}";

    static void Warn(FragmentMaskExtraction x, string code, string message, FragmentMaskCutInput c) =>
        x.Diagnostics.Add(new FemValidationDiagnostic(code, message, false, [c.Cut.MemberTag]));
}
