using CScore.Fire.Entities;
using CSfea.Thermal.Bc;
using CSfea.Thermal.Solvers;

namespace CScore.Fire;

/// <summary>
/// Сервис запуска огневого нестационарного теплового расчёта.
/// </summary>
public static class FireThermalService
{
    private const double DefaultMoistureFraction = 0.025;

    /// <summary>
    /// Выполнить полный огневой тепловой расчёт для заданного сечения.
    /// </summary>
    /// <param name="def">Параметры огневого расчёта.</param>
    /// <param name="section">Геометрия поперечного сечения.</param>
    /// <param name="aggregateType">Тип заполнителя бетона: silicate, carbonate, lightweight.</param>
    public static FireThermalResult Run(FireSectionDef def, CrossSection section, string aggregateType = "silicate")
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(section);

        var meshResult = FireMeshBuilder.Build(
            section,
            def.MeshStepM,
            def.Algorithm,
            def.SmoothIterTri,
            useQuadratic: IsQuadraticMesh(def));
        var boundaryEdges = FireBoundaryMapper.MapEdges(def, meshResult, def.FireCurve);
        var material = new Sp468ConcreteHeatMaterial(aggregateType, DefaultMoistureFraction);

        var options = new TransientHeatOptions
        {
            Duration_s = def.FireDurationMin * 60.0,
            TimeStep_s = def.TimeStepS,
            SnapshotStep_s = def.SnapshotStepMin * 60.0,
            Theta = def.Theta,
            PicardTolCelsius = def.PicardTolCelsius,
            PicardMaxIter = def.PicardMaxIter,
            TInitCelsius = 20.0,
            AdaptiveFirstMinute = true
        };

        Func<double, double> fireCurve = FireCurves.Get(def.FireCurve);
        TransientHeatResult thermal = TransientHeatSolver.Solve(
            meshResult.Mesh,
            material,
            options,
            boundaryEdges,
            fireCurve);

        var rebarHistory = BuildRebarHistory(meshResult, thermal.Snapshots);
        var rebarMax = rebarHistory.ToDictionary(kv => kv.Key, kv => kv.Value.Length == 0 ? 20.0 : kv.Value.Max());

        var coldFaceNodes = CollectColdFaceNodes(boundaryEdges);
        double[][]? coldFaceHistory = BuildColdFaceHistory(coldFaceNodes, thermal.Snapshots);
        double coldInit = coldFaceHistory is { Length: > 0 } && coldFaceHistory[0].Length > 0
            ? coldFaceHistory[0].Average()
            : options.TInitCelsius;

        return new FireThermalResult
        {
            MeshInfo = meshResult,
            TimesMin = thermal.Times_s.Select(t => t / 60.0).ToArray(),
            Snapshots = thermal.Snapshots,
            RebarMaxTemperatures = rebarMax,
            RebarTemperatureHistory = rebarHistory,
            ColdFaceNodeIds = coldFaceNodes,
            ColdFaceTemperatureHistory = coldFaceHistory,
            ColdFaceInitialTemperature = coldInit,
            ConvergenceLog = [.. thermal.ConvergenceLog],
            AggregateType = aggregateType,
            MoistureFraction = DefaultMoistureFraction,
            FireCurve = def.FireCurve,
            FireDurationMin = def.FireDurationMin
        };
    }

    static bool IsQuadraticMesh(FireSectionDef def) => false;

    private static Dictionary<int, double[]> BuildRebarHistory(FireMeshBuildResult meshResult, IReadOnlyList<double[]> snapshots)
    {
        var history = new Dictionary<int, double[]>(meshResult.Rebars.Count);
        foreach (var rebar in meshResult.Rebars)
        {
            int[] el = meshResult.Mesh.Elements[rebar.ElementIndex];
            var one = new double[snapshots.Count];
            for (int s = 0; s < snapshots.Count; s++)
            {
                double[] t = snapshots[s];
                if (rebar.ShapeWeights is { Length: > 0 } w)
                {
                    double val = 0;
                    for (int i = 0; i < w.Length && i < el.Length; i++)
                        val += w[i] * t[el[i]];
                    one[s] = val;
                }
                else
                    one[s] = rebar.Xi1 * t[el[0]] + rebar.Xi2 * t[el[1]] + rebar.Xi3 * t[el[2]];
            }

            history[rebar.Id] = one;
        }

        return history;
    }

    private static List<int> CollectColdFaceNodes(IReadOnlyList<HeatBoundaryEdge> boundaryEdges)
    {
        var set = new HashSet<int>();
        foreach (var edge in boundaryEdges)
        {
            if (edge.BcType != HeatBoundaryBcType.Ambient)
                continue;
            set.Add(edge.NodeA);
            set.Add(edge.NodeB);
        }

        return [.. set.OrderBy(i => i)];
    }

    private static double[][]? BuildColdFaceHistory(IReadOnlyList<int> coldFaceNodes, IReadOnlyList<double[]> snapshots)
    {
        if (coldFaceNodes.Count == 0)
            return null;

        var result = new double[snapshots.Count][];
        for (int s = 0; s < snapshots.Count; s++)
        {
            var row = new double[coldFaceNodes.Count];
            for (int i = 0; i < coldFaceNodes.Count; i++)
                row[i] = snapshots[s][coldFaceNodes[i]];
            result[s] = row;
        }

        return result;
    }
}
