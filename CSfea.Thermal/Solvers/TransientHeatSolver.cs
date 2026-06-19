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

        // Прямые решатели: один экземпляр на весь расчёт. Холецкий (SPD) с однократным
        // символическим анализом; LU — fallback при неположительном пивоте.
        var lu = new SparseLuSolver();
        SparseCholeskySolver? chol = null;
        bool useDirectFallback = false;

        // Постоянный CSC-паттерн сборки: один раз на расчёт, буферы значений переиспользуются.
        var assembly = HeatAssembly.Build(mesh, robinEdges);
        int nnz = assembly.ColPtr[n];
        var valuesK = new double[nnz];
        var valuesC = new double[nnz];
        var valuesA = new double[nnz];

        while (t_s < duration_s - 1e-9)
        {
            if (subStepping && t_s < 60.0)
                dtCurrent = Math.Min(dtCurrent, duration_s - t_s);
            else
                dtCurrent = Math.Min(dt, duration_s - t_s);

            var TNext = (double[])T.Clone();
            int nIter = 0;
            double maxResid = 0.0;
            double prevDelta = double.PositiveInfinity;
            bool factoredThisStep = false;

            for (int k = 0; k < options.PicardMaxIter; k++)
            {
                double[] midpointT = ComputeMidpointNodalTemperature(T, TNext);
                assembly.AssembleK(material, midpointT, valuesK);
                assembly.AssembleC(material, midpointT, valuesC);

                var F = new double[n];
                RobinBoundaryModel.ApplyRobin(mesh, robinEdges, t_s + dtCurrent, TNext,
                    assembly.SinkFor(valuesK), F, fireCurve);

                CscMatrix KCsc = assembly.ToCsc(valuesK);
                CscMatrix CCsc = assembly.ToCsc(valuesC);
                for (int i = 0; i < nnz; i++)
                    valuesA[i] = valuesC[i] / dtCurrent + options.Theta * valuesK[i];
                CscMatrix A = assembly.ToCsc(valuesA);

                double[] cTimesT = CCsc.Multiply(T);
                double[] kTimesT = KCsc.Multiply(T);
                double[] aTimesTNext = A.Multiply(TNext);
                double kFactor = 1.0 - options.Theta;

                // Остаток: r = rhs - A·TNext, где rhs = C·T/dt - (1-θ)K·T + F.
                var r = new double[n];
                for (int i = 0; i < n; i++)
                    r[i] = (cTimesT[i] / dtCurrent - kFactor * kTimesT[i] + F[i]) - aTimesTNext[i];

                bool needRefactor = !factoredThisStep
                                    || (k % options.RefactorEveryNIter == 0)
                                    || (maxResid > 0.5 * prevDelta); // стагнация

                if (!useDirectFallback)
                {
                    chol ??= AnalyzeOnce(A, out useDirectFallback);
                }

                double[] delta;
                if (useDirectFallback)
                {
                    lu.Factorize(A);
                    delta = lu.Solve(r);
                }
                else
                {
                    if (needRefactor)
                    {
                        chol!.Factorize(A);
                        if (!chol.LastFactorizationSpd)
                        {
                            useDirectFallback = true;
                            lu.Factorize(A);
                            delta = lu.Solve(r);
                        }
                        else
                        {
                            delta = chol.Solve(r);
                        }
                    }
                    else
                    {
                        delta = chol!.Solve(r);
                    }
                }
                factoredThisStep = true;

                for (int i = 0; i < n; i++) TNext[i] += delta[i];

                prevDelta = maxResid;
                maxResid = MaxAbs(delta);
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

    private static SparseCholeskySolver AnalyzeOnce(CscMatrix patternA, out bool fallback)
    {
        var chol = new SparseCholeskySolver();
        try
        {
            chol.AnalyzePattern(patternA);
            fallback = false;
            return chol;
        }
        catch
        {
            fallback = true;
            return chol;
        }
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

    private static double MaxAbs(double[] a)
    {
        double m = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            double v = Math.Abs(a[i]);
            if (v > m) m = v;
        }
        return m;
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
