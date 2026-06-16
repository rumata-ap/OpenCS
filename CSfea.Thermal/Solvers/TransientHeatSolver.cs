using CSfea.Sparse;
using CSfea.Thermal.Bc;
using CSfea.Thermal.Materials;

namespace CSfea.Thermal.Solvers;

/// <summary>
/// Нестационарный решатель теплопроводности:
/// θ-схема по времени + Пикар-итерации для учёта температурной нелинейности.
/// </summary>
public static class TransientHeatSolver
{
    /// <summary>
    /// Выполнить нестационарный тепловой расчёт.
    /// </summary>
    public static TransientHeatResult Solve(
        HeatMesh mesh,
        IHeatMaterial material,
        TransientHeatOptions options,
        IReadOnlyList<HeatBoundaryEdge> boundaryEdges,
        Func<double, double>? fireCurve = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(boundaryEdges);

        ValidateOptions(options);

        int n = mesh.NNodes;
        IList<HeatBoundaryEdge> robinEdges = boundaryEdges as IList<HeatBoundaryEdge> ?? [.. boundaryEdges];
        var T = new double[n];
        Array.Fill(T, options.TInitCelsius);

        var times = new List<double> { 0.0 };
        var snapshots = new List<double[]> { (double[])T.Clone() };
        var convergenceLog = new List<PicardRecord>();

        double duration_s = options.Duration_s;
        double snapshotStep_s = options.SnapshotStep_s;
        double nextSnapshot_s = 0.0;

        double t_s = 0.0;
        double dt = options.TimeStep_s;
        bool subStepping = options.AdaptiveFirstMinute;
        double dtCurrent = subStepping ? 0.5 : dt;
        int subStepsDone = 0;

        while (t_s < duration_s - 1e-9)
        {
            if (subStepping && t_s < 60.0)
                dtCurrent = Math.Min(dtCurrent, duration_s - t_s);
            else
                dtCurrent = Math.Min(dt, duration_s - t_s);

            var TNext = (double[])T.Clone();
            var TOldIter = (double[])T.Clone();
            int nIter = 0;
            double maxResid = 0.0;

            var lu = new SparseLuSolver();
            for (int k = 0; k < options.PicardMaxIter; k++)
            {
                double[] midpointT = ComputeMidpointNodalTemperature(T, TNext);
                CooMatrix K = mesh.AssembleConductivity(material, midpointT);
                CooMatrix C = mesh.AssembleCapacity(material, midpointT);

                var F = new double[n];
                RobinBoundaryModel.ApplyRobin(mesh, robinEdges, t_s + dtCurrent, TNext, K, F, fireCurve);

                CscMatrix KCsc = K.ToCsc();
                CscMatrix CCsc = C.ToCsc();
                CscMatrix A = CombineScaled(CCsc, 1.0 / dtCurrent, KCsc, options.Theta).ToCsc();

                double[] cTimesT = CCsc.Multiply(T);
                double[] kTimesT = KCsc.Multiply(T);
                var rhs = new double[n];
                double kFactor = 1.0 - options.Theta;
                for (int i = 0; i < n; i++)
                    rhs[i] = cTimesT[i] / dtCurrent - kFactor * kTimesT[i] + F[i];

                lu.Factorize(A);
                double[] TNew = lu.Solve(rhs);

                maxResid = MaxAbsDiff(TNew, TOldIter);
                TNext = TNew;
                TOldIter = (double[])TNew.Clone();
                nIter = k + 1;
                if (maxResid < options.PicardTolCelsius)
                    break;
            }

            convergenceLog.Add(new PicardRecord
            {
                Time_s = t_s + dtCurrent,
                NPicardIter = nIter,
                MaxResidualCelsius = maxResid
            });

            t_s += dtCurrent;
            T = TNext;

            if (subStepping && t_s < 60.0)
            {
                subStepsDone++;
                if (subStepsDone % 10 == 0)
                    dtCurrent = Math.Min(dtCurrent * 2.0, dt);
            }

            if (t_s >= nextSnapshot_s + snapshotStep_s - 1e-9 || t_s >= duration_s - 1e-9)
            {
                times.Add(t_s);
                snapshots.Add((double[])T.Clone());
                nextSnapshot_s = t_s;
            }
        }

        return new TransientHeatResult
        {
            Times_s = [.. times],
            Snapshots = [.. snapshots],
            ConvergenceLog = convergenceLog
        };
    }

    private static void ValidateOptions(TransientHeatOptions options)
    {
        if (options.Duration_s <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(options.Duration_s), "Duration_s должен быть > 0.");
        if (options.TimeStep_s <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(options.TimeStep_s), "TimeStep_s должен быть > 0.");
        if (options.SnapshotStep_s <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(options.SnapshotStep_s), "SnapshotStep_s должен быть > 0.");
        if (options.Theta < 0.0 || options.Theta > 1.0)
            throw new ArgumentOutOfRangeException(nameof(options.Theta), "Theta должен быть в диапазоне [0, 1].");
        if (options.PicardMaxIter <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.PicardMaxIter), "PicardMaxIter должен быть > 0.");
        if (options.PicardTolCelsius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(options.PicardTolCelsius), "PicardTolCelsius должен быть > 0.");
    }

    private static CooMatrix CombineScaled(CscMatrix a, double aScale, CscMatrix b, double bScale)
    {
        if (a.Rows != b.Rows || a.Cols != b.Cols)
            throw new ArgumentException("Размеры матриц не совпадают.");

        var outCoo = new CooMatrix(a.Rows, a.Cols, a.Nnz + b.Nnz);

        if (aScale != 0.0)
        {
            for (int c = 0; c < a.Cols; c++)
            {
                for (int p = a.ColPtr[c]; p < a.ColPtr[c + 1]; p++)
                    outCoo.Add(a.RowIdx[p], c, aScale * a.Values[p]);
            }
        }

        if (bScale != 0.0)
        {
            for (int c = 0; c < b.Cols; c++)
            {
                for (int p = b.ColPtr[c]; p < b.ColPtr[c + 1]; p++)
                    outCoo.Add(b.RowIdx[p], c, bScale * b.Values[p]);
            }
        }

        return outCoo;
    }

    private static double[] ComputeMidpointNodalTemperature(double[] left, double[] right)
    {
        var result = new double[left.Length];
        for (int i = 0; i < left.Length; i++)
            result[i] = 0.5 * (left[i] + right[i]);
        return result;
    }

    private static double MaxAbsDiff(double[] a, double[] b)
    {
        double max = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            double d = Math.Abs(a[i] - b[i]);
            if (d > max)
                max = d;
        }
        return max;
    }
}
