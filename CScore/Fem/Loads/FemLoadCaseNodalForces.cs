namespace CScore.Fem.Loads;

/// <summary>Узловые силы сетки по загружению или линейной комбинации загружений и диагностики разрешения.</summary>
public sealed record FemNodalLoadResult(IReadOnlyList<FemNodalForce> Forces, IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    /// <summary>ΣFx, ΣFy, ΣFz, Н.</summary>
    public (double Fx, double Fy, double Fz) Total =>
        (Forces.Sum(f => f.Fx), Forces.Sum(f => f.Fy), Forces.Sum(f => f.Fz));
}

/// <summary>
/// Нагрузки сеточного уровня загружений → узловые силы сетки: нагрузки на КЭ, узловые на узлы сетки, собственный вес
/// загружения. Общий путь для адаптера CSfea, 3D-показа и сумм. Нагрузки конструктивного уровня (на узлы и стержни
/// редактора) сюда не входят — их переносит адаптер через дискретизацию.
/// </summary>
public static class FemLoadCaseNodalForces
{
    /// <summary>
    /// Сумма загружений с коэффициентами (<paramref name="terms"/>: Id загружения → коэффициент). <paramref name="sink"/>
    /// — отдельный приёмник сил КЭ (см. <see cref="FemElementLoadNodalizer.Accumulate"/>): ушедшее в него в результат не
    /// входит.
    /// </summary>
    public static FemNodalLoadResult Resolve(
        IReadOnlyList<(FemLoadCase LoadCase, double Factor)> terms,
        IReadOnlyList<FemElementLoad> elementLoads,
        IReadOnlyList<FemMeshNodeLoad> meshNodeLoads,
        FemLoadMeshContext mesh,
        Func<FemElement, Dictionary<string, double[]>?>? sink = null)
    {
        var forces = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var diagnostics = new List<FemValidationDiagnostic>();
        foreach (var (lc, factor) in terms)
        {
            if (factor == 0) continue;
            if (lc.SelfWeightFactor is { } k && k != 0)
                FemElementLoadNodalizer.AccumulateSelfWeight(k * factor, mesh.Elements, mesh, forces, diagnostics, sink);
            foreach (var load in elementLoads.Where(l => l.LoadCaseId == lc.Id))
                FemElementLoadNodalizer.Accumulate(load, mesh, factor, forces, diagnostics, sink);
            var missing = new List<string>();
            foreach (var load in meshNodeLoads.Where(l => l.LoadCaseId == lc.Id))
            {
                if (!mesh.NodesByTag.ContainsKey(load.MeshNodeTag)) { missing.Add(load.MeshNodeTag); continue; }
                if (!forces.TryGetValue(load.MeshNodeTag, out var v)) forces[load.MeshNodeTag] = v = new double[6];
                v[0] += factor * load.Fx; v[1] += factor * load.Fy; v[2] += factor * load.Fz;
                v[3] += factor * load.Mx; v[4] += factor * load.My; v[5] += factor * load.Mz;
            }
            if (missing.Count > 0)
                diagnostics.Add(new("mesh_node_load_node_missing",
                    $"Загружение «{lc.Tag}»: узловые нагрузки на отсутствующие узлы сетки {FemLoadTargets.Sample(missing)}.",
                    true, missing.ToArray()));
        }
        var list = forces
            .OrderBy(kv => int.TryParse(kv.Key, out int n) ? n : int.MaxValue).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new FemNodalForce(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3], kv.Value[4], kv.Value[5]))
            .ToArray();
        return new FemNodalLoadResult(list, diagnostics);
    }

    /// <summary>Одно загружение.</summary>
    public static FemNodalLoadResult Resolve(FemLoadCase loadCase, IReadOnlyList<FemElementLoad> elementLoads,
        IReadOnlyList<FemMeshNodeLoad> meshNodeLoads, FemLoadMeshContext mesh)
        => Resolve([(loadCase, 1.0)], elementLoads, meshNodeLoads, mesh);
}
