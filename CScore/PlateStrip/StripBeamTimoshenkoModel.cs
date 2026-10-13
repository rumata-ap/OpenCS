using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Закрепление кручения полосы.</summary>
public enum StripTorsionRestraint
{
    /// <summary>θx закреплён на обоих концах (вилочные опоры) — по умолчанию.</summary>
    BothEnds,
    /// <summary>θx закреплён только в начале: свободный конец несёт заданный крутящий момент.</summary>
    StartOnly
}

/// <summary>Результат прогона производной балки полосы Тимошенко (Срез 8b).</summary>
/// <param name="Displacements">Узловые перемещения, 6 DOF в порядке u, v, w, θx, θy, θz.</param>
/// <param name="StationResultants">Внутренние усилия на станциях [N, My, Mz, T, Vy, Vz].</param>
public sealed record StripBeamTimoshenkoSolveResult(
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    double[] Displacements,
    double[][] StationResultants,
    int TotalIterations,
    double AchievedLoadFactor);

/// <summary>
/// Производная балка полосы Тимошенко с кручением, 6 DOF (Срез 8b): линейная и нелинейная
/// задачи одним решателем — Ньютон–Рафсон с пошаговым нагружением поверх закона сечения
/// <see cref="IStripTimoshenkoSectionLaw"/>. Линейный закон сходится за одну итерацию.
///
/// Нумерация DOF в <c>prescribed</c> наследует 5-DOF соглашение
/// <see cref="StripPrescribedDisplacement"/> (0 u, 1 v, 2 w, 3 θy, 4 θz) плюс
/// <see cref="StripTorsionDof"/> = 5 для θx: так результат <see cref="StripKinematicMapper"/>
/// подаётся без пересчёта. Узловые нагрузки — из <see cref="StripLoadConsistentNodalProjection"/>
/// (эрмитовы согласованные нагрузки; для равномерной нагрузки они совпадают с точными для
/// Тимошенко), крутящий момент точечных нагрузок распределяется линейно по элементу.
///
/// Эпюра снимается по правилу Срезов 6–7: g = f_int − f^consistent; сечение слева берётся с
/// обратным знаком. Внутренний примитив: некорректные входы — исключение.
/// </summary>
public static class StripBeamTimoshenkoModel
{
    /// <summary>Индекс θx в нумерации заданных перемещений (5-DOF соглашение расширено).</summary>
    public const int StripTorsionDof = 5;

    static readonly int[] DofMap = [0, 1, 2, 4, 5, 3];

    public static StripBeamTimoshenkoSolveResult SolveLinear(
        double[,] sectionTangent, double torsionalStiffness, double shearGAy, double shearGAz,
        double lengthM, IReadOnlyList<double> stationFractions, StripBeamSupportScheme scheme,
        StripLoadSet loads, KnownEndActions? endActions = null,
        IReadOnlyList<StripPrescribedDisplacement>? prescribed = null,
        StripTorsionRestraint torsionRestraint = StripTorsionRestraint.BothEnds)
    {
        ArgumentNullException.ThrowIfNull(stationFractions);
        var law = new LinearStripTimoshenkoSectionLaw(
            sectionTangent, torsionalStiffness, shearGAy, shearGAz,
            Math.Max(stationFractions.Count - 1, 1));
        return Solve(law, lengthM, stationFractions, scheme, loads, endActions,
            StripNewtonOptions.Default, prescribed, torsionRestraint);
    }

    public static StripBeamTimoshenkoSolveResult Solve(
        IStripTimoshenkoSectionLaw law, double lengthM, IReadOnlyList<double> stationFractions,
        StripBeamSupportScheme scheme, StripLoadSet loads, KnownEndActions? endActions = null,
        StripNewtonOptions? options = null,
        IReadOnlyList<StripPrescribedDisplacement>? prescribed = null,
        StripTorsionRestraint torsionRestraint = StripTorsionRestraint.BothEnds)
    {
        ArgumentNullException.ThrowIfNull(law);
        ArgumentNullException.ThrowIfNull(stationFractions);
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(loads);
        options ??= StripNewtonOptions.Default;
        options.Validate();

        if (!(lengthM > 0.0) || !double.IsFinite(lengthM))
            throw new ArgumentOutOfRangeException(nameof(lengthM),
                "Длина полосы должна быть конечной и положительной.");
        if (stationFractions.Count < 2)
            throw new ArgumentException("Нужно не менее двух станций.", nameof(stationFractions));

        int nodeCount = stationFractions.Count;
        int elementCount = nodeCount - 1;
        const int perNode = StripBeamElementTimoshenko.DofPerNode;
        int dofCount = nodeCount * perNode;

        var diagnostics = new List<FemValidationDiagnostic>();
        if (law.ElementCount != elementCount)
        {
            diagnostics.Add(new("plate_strip_source_grid_shape_mismatch",
                $"Закон сечения рассчитан на {law.ElementCount} элементов, станций задано на {elementCount}."));
            return Failed(diagnostics, dofCount, nodeCount);
        }

        // Нагрузки: Mx точечных нагрузок здесь законен — проверка допуска отключена.
        var projection = StripLoadConsistentNodalProjection.Project(
            loads, lengthM, stationFractions, double.PositiveInfinity);
        diagnostics.AddRange(projection.Diagnostics);
        if (!projection.IsCalculable)
            return Failed(diagnostics, dofCount, nodeCount);

        var torque = TorqueShares(loads, lengthM, stationFractions);
        var elementLengths = new double[elementCount];
        var elementLoads = new double[elementCount][];
        var phiY = new double[elementCount];
        var phiZ = new double[elementCount];
        for (int e = 0; e < elementCount; e++)
        {
            elementLengths[e] = (stationFractions[e + 1] - stationFractions[e]) * lengthM;
            elementLoads[e] = StripBeamElementTimoshenko.LoadVector(
                projection.Elements[e], torque[e].T1, torque[e].T2);
            var initial = law.Evaluate(e, 0.0, 0.0, 0.0, 0.0);
            phiY[e] = StripBeamElementTimoshenko.ShearParameter(initial.K[1, 1], initial.GAz, elementLengths[e]);
            phiZ[e] = StripBeamElementTimoshenko.ShearParameter(initial.K[2, 2], initial.GAy, elementLengths[e]);
        }

        var external = new double[dofCount];
        for (int e = 0; e < elementCount; e++)
            for (int i = 0; i < StripBeamElementTimoshenko.DofPerElement; i++)
                external[e * perNode + i] += elementLoads[e][i];
        if (endActions != null)
        {
            if (!endActions.IsFinite)
                throw new ArgumentException("Концевые усилия должны быть конечными.", nameof(endActions));
            int last = (nodeCount - 1) * perNode;
            external[0] += -endActions.StartN;
            external[StripBeamElementTimoshenko.ThetaX] += -endActions.StartT;
            external[StripBeamElementTimoshenko.ThetaY] += -endActions.StartMy;
            external[StripBeamElementTimoshenko.ThetaZ] += -endActions.StartMz;
            external[last] += endActions.EndN;
            external[last + StripBeamElementTimoshenko.ThetaX] += endActions.EndT;
            external[last + StripBeamElementTimoshenko.ThetaY] += endActions.EndMy;
            external[last + StripBeamElementTimoshenko.ThetaZ] += endActions.EndMz;
        }

        var isFixed = BuildConstraintMask(scheme, nodeCount, torsionRestraint);
        var prescribedSix = TranslatePrescribed(prescribed, nodeCount);
        var prescribedDofs = new List<int>();
        foreach (var p in prescribedSix)
        {
            int dof = p.NodeIndex * perNode + p.Dof;
            isFixed[dof] = true;
            prescribedDofs.Add(dof);
        }
        var freeDofs = new List<int>(dofCount);
        for (int i = 0; i < dofCount; i++)
            if (!isFixed[i]) freeDofs.Add(i);

        var assembly = new Assembly(law, elementLengths, elementLoads, phiY, phiZ, dofCount, elementCount);
        var displacements = new double[dofCount];
        double achieved = 0.0;
        double step = 1.0 / options.LoadSteps;
        int halvings = 0;
        int totalIterations = 0;

        while (achieved < 1.0 - 1e-12)
        {
            double target = Math.Min(1.0, achieved + step);
            var attempt = RunNewton(assembly, external, freeDofs, prescribedDofs, prescribedSix,
                displacements, target, options, lengthM);
            totalIterations += attempt.Iterations;
            if (attempt.Converged)
            {
                displacements = attempt.Displacements;
                achieved = target;
                continue;
            }
            diagnostics.AddRange(attempt.Diagnostics);
            if (halvings >= options.MaxStepHalvings)
            {
                diagnostics.Add(new("plate_strip_nonlinear_not_converged",
                    $"Нелинейный прогон не достиг полной нагрузки: остановлен на доле " +
                    $"{achieved:F3} после {halvings} делений шага."));
                return new(false, diagnostics, displacements,
                    ExtractStationResultants(assembly, displacements, elementCount),
                    totalIterations, achieved);
            }
            step /= 2.0;
            halvings++;
        }

        return new(!diagnostics.Any(d => d.IsError), diagnostics, displacements,
            ExtractStationResultants(assembly, displacements, elementCount), totalIterations, achieved);
    }

    /// <summary>Маска закреплённых DOF 6-DOF узла.</summary>
    public static bool[] BuildConstraintMask(
        StripBeamSupportScheme scheme, int nodeCount, StripTorsionRestraint torsion)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        if (nodeCount < 2)
            throw new ArgumentOutOfRangeException(nameof(nodeCount), "Нужно не менее двух узлов.");
        const int n = StripBeamElementTimoshenko.DofPerNode;
        var fixedMask = new bool[nodeCount * n];
        int last = (nodeCount - 1) * n;
        foreach (int node in new[] { 0, last })
        {
            fixedMask[node + StripBeamElementTimoshenko.V] = true;
            fixedMask[node + StripBeamElementTimoshenko.W] = true;
        }
        if (scheme.StartCondition == StripBeamEndCondition.Fixed)
        {
            fixedMask[StripBeamElementTimoshenko.ThetaY] = true;
            fixedMask[StripBeamElementTimoshenko.ThetaZ] = true;
        }
        if (scheme.EndCondition == StripBeamEndCondition.Fixed)
        {
            fixedMask[last + StripBeamElementTimoshenko.ThetaY] = true;
            fixedMask[last + StripBeamElementTimoshenko.ThetaZ] = true;
        }
        fixedMask[StripBeamElementTimoshenko.ThetaX] = true;
        if (torsion == StripTorsionRestraint.BothEnds)
            fixedMask[last + StripBeamElementTimoshenko.ThetaX] = true;
        fixedMask[0] = true;
        if (scheme.AxialRestraint == StripAxialRestraint.BothEnds)
            fixedMask[last] = true;
        return fixedMask;
    }

    static List<StripPrescribedDisplacement> TranslatePrescribed(
        IReadOnlyList<StripPrescribedDisplacement>? prescribed, int nodeCount)
    {
        var result = new List<StripPrescribedDisplacement>();
        if (prescribed == null) return result;
        var seen = new HashSet<(int, int)>();
        foreach (var item in prescribed)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(prescribed));
            if (item.NodeIndex < 0 || item.NodeIndex >= nodeCount)
                throw new ArgumentException($"Заданное перемещение: узел {item.NodeIndex} вне диапазона.", nameof(prescribed));
            if (item.Dof < 0 || item.Dof > StripTorsionDof)
                throw new ArgumentException($"Заданное перемещение: DOF {item.Dof} вне диапазона.", nameof(prescribed));
            if (!double.IsFinite(item.Value))
                throw new ArgumentException("Заданное перемещение должно быть конечным.", nameof(prescribed));
            int dof = DofMap[item.Dof];
            if (!seen.Add((item.NodeIndex, dof)))
                throw new ArgumentException(
                    $"Заданное перемещение: DOF {item.Dof} узла {item.NodeIndex} задан дважды.", nameof(prescribed));
            result.Add(new(item.NodeIndex, dof, item.Value));
        }
        return result;
    }

    /// <summary>Крутящие моменты точечных нагрузок, распределённые по узлам элемента линейной
    /// интерполяцией (согласованные нагрузки для линейного θx).</summary>
    static (double T1, double T2)[] TorqueShares(
        StripLoadSet loads, double lengthM, IReadOnlyList<double> stations)
    {
        var shares = new (double T1, double T2)[stations.Count - 1];
        foreach (var load in loads.Loads)
        {
            if (load.IsDistributed || load.MxKnM == 0.0) continue;
            int i = stations.Count - 2;
            for (int k = 0; k < stations.Count - 2; k++)
                if (load.StationFraction < stations[k + 1]) { i = k; break; }
            double le = (stations[i + 1] - stations[i]) * lengthM;
            double a = (load.StationFraction - stations[i]) * lengthM;
            double b = le - a;
            shares[i] = (shares[i].T1 + load.MxKnM * b / le, shares[i].T2 + load.MxKnM * a / le);
        }
        return shares;
    }

    sealed record NewtonAttempt(
        bool Converged, double[] Displacements, int Iterations,
        IReadOnlyList<FemValidationDiagnostic> Diagnostics);

    static NewtonAttempt RunNewton(
        Assembly assembly, double[] external, List<int> freeDofs, List<int> prescribedDofs,
        IReadOnlyList<StripPrescribedDisplacement> prescribed, double[] start, double loadFactor,
        StripNewtonOptions options, double lengthM)
    {
        const int perNode = StripBeamElementTimoshenko.DofPerNode;
        var diagnostics = new List<FemValidationDiagnostic>();
        var u = (double[])start.Clone();
        foreach (var p in prescribed)
            u[p.NodeIndex * perNode + p.Dof] = loadFactor * p.Value;

        double previousResidual = double.PositiveInfinity;
        int growthStreak = 0;
        double[,]? frozen = null;

        for (int iteration = 0; iteration < options.MaxIterations; iteration++)
        {
            AssemblyResult state;
            try
            {
                state = assembly.Evaluate(u, !options.ModifiedNewton || frozen == null);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                diagnostics.Add(new("plate_strip_nonlinear_state_out_of_bounds", ex.Message));
                return new(false, u, iteration, diagnostics);
            }
            catch (InvalidOperationException ex)
            {
                diagnostics.Add(new("plate_strip_nonlinear_singular_tangent", ex.Message, false));
                return new(false, u, iteration, diagnostics);
            }

            var residual = new double[external.Length];
            for (int i = 0; i < residual.Length; i++)
                residual[i] = loadFactor * external[i] - state.InternalForces[i];

            double scale = ReferenceScale(external, state.InternalForces, freeDofs, prescribedDofs,
                lengthM, loadFactor);
            double norm = ScaledNorm(residual, freeDofs, lengthM) / scale;
            if (norm < options.ResidualTolerance)
                return new(true, u, iteration, diagnostics);

            if (norm > previousResidual)
            {
                growthStreak++;
                if (growthStreak == 2)
                    diagnostics.Add(new("plate_strip_nonlinear_diverging",
                        "Невязка растёт две итерации подряд — эвристический признак расходимости.", false));
            }
            else growthStreak = 0;
            previousResidual = norm;

            var tangent = state.Tangent ?? frozen!;
            if (options.ModifiedNewton) frozen ??= tangent;

            int n = freeDofs.Count;
            if (n == 0) return new(true, u, iteration, diagnostics);
            var reduced = new double[n, n];
            var rhs = new double[n];
            for (int i = 0; i < n; i++)
            {
                rhs[i] = residual[freeDofs[i]];
                for (int j = 0; j < n; j++)
                    reduced[i, j] = tangent[freeDofs[i], freeDofs[j]];
            }
            if (!DensePivotSolver.Solve(reduced, rhs, out double[] increment))
            {
                diagnostics.Add(new("plate_strip_load_recovery_singular_system",
                    "Система производной балки Тимошенко вырождена: проверьте опорную схему, " +
                    "закрепление кручения и жёсткости сечения.", false));
                return new(false, u, iteration + 1, diagnostics);
            }

            var delta = new double[u.Length];
            for (int i = 0; i < n; i++)
            {
                delta[freeDofs[i]] = increment[i];
                u[freeDofs[i]] += increment[i];
            }
            double dispScale = Math.Max(DisplacementNorm(u, freeDofs, lengthM), 1e-30);
            if (DisplacementNorm(delta, freeDofs, lengthM) / dispScale < options.IncrementTolerance)
                return new(true, u, iteration + 1, diagnostics);
        }

        diagnostics.Add(new("plate_strip_nonlinear_not_converged",
            $"Ньютон не сошёлся за {options.MaxIterations} итераций при доле нагрузки {loadFactor:F3}.", false));
        return new(false, u, options.MaxIterations, diagnostics);
    }

    static bool IsMoment(int dof) => dof % StripBeamElementTimoshenko.DofPerNode >= 3;

    static double ReferenceScale(
        double[] external, double[] internalForces, List<int> freeDofs, List<int> prescribedDofs,
        double lengthM, double loadFactor)
    {
        double scale = 0.0;
        foreach (int dof in freeDofs)
        {
            double divisor = IsMoment(dof) ? lengthM : 1.0;
            scale = Math.Max(scale, Math.Abs(loadFactor * external[dof]) / divisor);
            scale = Math.Max(scale, Math.Abs(internalForces[dof]) / divisor);
        }
        foreach (int dof in prescribedDofs)
            scale = Math.Max(scale, Math.Abs(internalForces[dof]) / (IsMoment(dof) ? lengthM : 1.0));
        return scale > 0.0 ? scale : 1.0;
    }

    static double ScaledNorm(double[] vector, List<int> freeDofs, double lengthM)
    {
        double norm = 0.0;
        foreach (int dof in freeDofs)
            norm = Math.Max(norm, Math.Abs(vector[dof]) / (IsMoment(dof) ? lengthM : 1.0));
        return norm;
    }

    static double DisplacementNorm(double[] vector, List<int> freeDofs, double lengthM)
    {
        double norm = 0.0;
        foreach (int dof in freeDofs)
            norm = Math.Max(norm, Math.Abs(vector[dof]) / (IsMoment(dof) ? 1.0 : lengthM));
        return norm;
    }

    static double[][] ExtractStationResultants(Assembly assembly, double[] displacements, int elementCount)
    {
        var result = new double[elementCount + 1][];
        for (int s = 0; s <= elementCount; s++)
            result[s] = new double[6];

        const int u = StripBeamElementTimoshenko.U, v = StripBeamElementTimoshenko.V,
            w = StripBeamElementTimoshenko.W, tx = StripBeamElementTimoshenko.ThetaX,
            ty = StripBeamElementTimoshenko.ThetaY, tz = StripBeamElementTimoshenko.ThetaZ;
        const int n = StripBeamElementTimoshenko.DofPerNode;

        for (int e = 0; e < elementCount; e++)
        {
            double[] g;
            try { g = assembly.ElementEndActions(e, displacements); }
            catch (ArgumentOutOfRangeException) { continue; }
            catch (InvalidOperationException) { continue; }

            if (e == 0)
                result[0] = [-g[u], -g[ty], -g[tz], -g[tx], -g[v], -g[w]];
            result[e + 1] = [g[n + u], g[n + ty], g[n + tz], g[n + tx], g[n + v], g[n + w]];
        }
        return result;
    }

    static StripBeamTimoshenkoSolveResult Failed(
        List<FemValidationDiagnostic> diagnostics, int dofCount, int nodeCount)
    {
        var resultants = new double[nodeCount][];
        for (int i = 0; i < nodeCount; i++)
            resultants[i] = new double[6];
        return new(false, diagnostics, new double[dofCount], resultants, 0, 0.0);
    }

    sealed record AssemblyResult(double[] InternalForces, double[,]? Tangent);

    sealed class Assembly(
        IStripTimoshenkoSectionLaw law, double[] lengths, double[][] elementLoads,
        double[] phiY, double[] phiZ, int dofCount, int elementCount)
    {
        public AssemblyResult Evaluate(double[] displacements, bool tangent)
        {
            var internalForces = new double[dofCount];
            var global = tangent ? new double[dofCount, dofCount] : null;
            for (int e = 0; e < elementCount; e++)
            {
                var (fe, ke) = Element(e, displacements, tangent);
                int offset = e * StripBeamElementTimoshenko.DofPerNode;
                for (int i = 0; i < StripBeamElementTimoshenko.DofPerElement; i++)
                {
                    internalForces[offset + i] += fe[i];
                    if (global == null) continue;
                    for (int j = 0; j < StripBeamElementTimoshenko.DofPerElement; j++)
                        global[offset + i, offset + j] += ke![i, j];
                }
            }
            return new(internalForces, global);
        }

        public double[] ElementEndActions(int element, double[] displacements)
        {
            var (fe, _) = Element(element, displacements, tangent: false);
            var g = new double[StripBeamElementTimoshenko.DofPerElement];
            for (int i = 0; i < g.Length; i++)
                g[i] = fe[i] - elementLoads[element][i];
            return g;
        }

        (double[] Internal, double[,]? Tangent) Element(int element, double[] displacements, bool tangent)
        {
            double l = lengths[element];
            var d = new double[StripBeamElementTimoshenko.DofPerElement];
            Array.Copy(displacements, element * StripBeamElementTimoshenko.DofPerNode, d, 0, d.Length);

            var gauss = StripBeamElement.GaussXi;
            var q = new double[gauss.Count][];
            var shear = new double[gauss.Count][];
            var k4 = tangent ? new double[gauss.Count][,] : null;
            var gaY = new double[gauss.Count];
            var gaZ = new double[gauss.Count];
            for (int g = 0; g < gauss.Count; g++)
            {
                var s = StripBeamElementTimoshenko.Strains(d, l, gauss[g], phiY[element], phiZ[element]);
                var r = law.Evaluate(element, s[0], s[1], s[2], s[3]);
                q[g] = r.Q;
                // Бесконечная сдвиговая жёсткость = жёсткий сдвиг: усилие определяется равновесием,
                // в элементе с Φ = 0 γ ≡ 0, поэтому вклад равен нулю.
                shear[g] = [double.IsPositiveInfinity(r.GAy) ? 0.0 : r.GAy * s[4],
                            double.IsPositiveInfinity(r.GAz) ? 0.0 : r.GAz * s[5]];
                gaY[g] = r.GAy;
                gaZ[g] = r.GAz;
                if (k4 != null) k4[g] = r.K;
            }
            var fe = StripBeamElementTimoshenko.InternalForces(q, shear, l, phiY[element], phiZ[element]);
            double[,]? ke = tangent
                ? StripBeamElementTimoshenko.Stiffness(k4!, gaY, gaZ, l, phiY[element], phiZ[element])
                : null;
            return (fe, ke);
        }
    }
}
