using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Результат нелинейного прогона производной балки полосы.</summary>
/// <param name="IsCalculable">Ложь, если прогон не дошёл до полной нагрузки.</param>
/// <param name="Diagnostics">Диагностики прогона, включая диагностики проекции нагрузки.</param>
/// <param name="Displacements">Узловые перемещения, 5 DOF на узел (u, v, w, θy, θz).</param>
/// <param name="StationResultants">Внутренние [N, My, Mz] на каждой станции.</param>
/// <param name="TotalIterations">Суммарное число решённых систем.</param>
/// <param name="AchievedLoadFactor">Достигнутая доля нагрузки, 1.0 при полном прогоне.</param>
public sealed record StripBeamNonlinearSolveResult(
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    double[] Displacements,
    double[][] StationResultants,
    int TotalIterations,
    double AchievedLoadFactor);

/// <summary>
/// Нелинейная одномерная задача производной балки полосы: Ньютон–Рафсон с пошаговым
/// нагружением поверх той же сборки, что <see cref="StripBeamModel"/> (Срез 7).
///
/// Отличия от линейной задачи ровно два: внутренние силы элемента считаются как
/// <c>∫Bᵀ(xi)·Q(ε(xi)) dx</c> через <see cref="NonlinearStripSection"/>, а касательная
/// собирается по точкам квадратуры. Опорная схема, маски DOF, вектор внешней нагрузки
/// (<c>StripLoadConsistentNodalProjection</c>) и знаковая конвенция берутся у Среза 6 без
/// изменений — <see cref="StripBeamElement"/> остаётся единственной точкой правды конвенции.
///
/// <b>Правило sampling эпюры сохранено.</b> Внутренние усилия на станциях извлекаются из
/// концевых усилий элемента <c>g = f_int − f^consistent</c> — нелинейное обобщение правила
/// Среза 6 (<c>K·u − f</c>), с которым оно тождественно совпадает на линейном отклике. Снимать
/// эпюру с точек квадратуры (то есть <c>D·B·u</c>) нельзя: для эрмитова элемента при
/// равномерной нагрузке κ постоянна, и такая «эпюра» даёт fixed-end moment qL²/12 на шарнирной
/// опоре вместо нуля.
/// </summary>
public static class StripBeamNonlinearModel
{
    public static StripBeamNonlinearSolveResult Solve(
        StripSectionSourceGrid sources,
        double widthM,
        double lengthM,
        IReadOnlyList<double> stationFractions,
        StripBeamSupportScheme scheme,
        StripLoadSet loads,
        KnownEndActions? endActions = null,
        StripNewtonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(stationFractions);
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(loads);

        options ??= StripNewtonOptions.Default;
        options.Validate();

        if (!(widthM > 0.0) || !double.IsFinite(widthM))
            throw new ArgumentOutOfRangeException(nameof(widthM),
                "Ширина полосы должна быть конечной и положительной.");
        if (!(lengthM > 0.0) || !double.IsFinite(lengthM))
            throw new ArgumentOutOfRangeException(nameof(lengthM),
                "Длина полосы должна быть конечной и положительной.");
        if (stationFractions.Count < 2)
            throw new ArgumentException("Нужно не менее двух станций.", nameof(stationFractions));

        int nodeCount = stationFractions.Count;
        int elementCount = nodeCount - 1;
        int dofCount = nodeCount * StripBeamElement.DofPerNode;

        var diagnostics = new List<FemValidationDiagnostic>();
        diagnostics.AddRange(sources.Validate());
        diagnostics.AddRange(sources.ValidateAgainstStations(nodeCount));
        if (diagnostics.Any(d => d.IsError))
            return Failed(diagnostics, dofCount, nodeCount);

        var projection = StripLoadConsistentNodalProjection.Project(loads, lengthM, stationFractions);
        diagnostics.AddRange(projection.Diagnostics);
        if (!projection.IsCalculable)
            return Failed(diagnostics, dofCount, nodeCount);

        var elementLengths = new double[elementCount];
        var elementLoads = new double[elementCount][];
        for (int e = 0; e < elementCount; e++)
        {
            elementLengths[e] = (stationFractions[e + 1] - stationFractions[e]) * lengthM;
            elementLoads[e] = StripBeamElement.LoadVector(projection.Elements[e]);
        }

        var fullExternal = BuildExternalVector(elementLoads, endActions, nodeCount, dofCount);
        var isFixed = StripBeamModel.BuildConstraintMask(scheme, nodeCount);
        var freeDofs = new List<int>(dofCount);
        for (int i = 0; i < dofCount; i++)
            if (!isFixed[i]) freeDofs.Add(i);

        var displacements = new double[dofCount];
        var assembly = new Assembly(sources, widthM, elementLengths, elementLoads, dofCount, elementCount);

        // f_int(0) != 0 означает, что нулевое состояние несёт начальные усилия. Оно остаётся
        // состоянием отсчёта (вычитание исказило бы физику), но факт объявляется явно.
        AssemblyResult initial;
        try
        {
            initial = assembly.Evaluate(displacements, tangent: false);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            diagnostics.Add(new("plate_strip_nonlinear_state_out_of_bounds", ex.Message));
            return Failed(diagnostics, dofCount, nodeCount);
        }
        if (freeDofs.Any(dof => Math.Abs(initial.InternalForces[dof]) > 0.0))
            diagnostics.Add(new("plate_strip_nonlinear_nonzero_initial_forces",
                "Источник даёт ненулевые усилия в нулевом состоянии: нулевое состояние принято " +
                "состоянием отсчёта, начальные усилия входят в невязку как есть.", false));

        double achieved = 0.0;
        double step = 1.0 / options.LoadSteps;
        int halvings = 0;
        int totalIterations = 0;

        while (achieved < 1.0 - 1e-12)
        {
            double target = Math.Min(1.0, achieved + step);
            var attempt = RunNewton(
                assembly, fullExternal, freeDofs, displacements, target, options, lengthM);
            totalIterations += attempt.Iterations;

            if (attempt.Converged)
            {
                displacements = attempt.Displacements;
                achieved = target;
                continue;
            }

            diagnostics.AddRange(attempt.Diagnostics);
            if (attempt.Fatal || halvings >= options.MaxStepHalvings)
            {
                diagnostics.Add(new("plate_strip_nonlinear_not_converged",
                    $"Нелинейный прогон не достиг полной нагрузки: остановлен на доле " +
                    $"{achieved:F3} после {halvings} делений шага."));
                var partial = ExtractStationResultants(assembly, displacements, elementCount);
                return new(false, diagnostics, displacements, partial, totalIterations, achieved);
            }

            step /= 2.0;
            halvings++;
        }

        var resultants = ExtractStationResultants(assembly, displacements, elementCount);
        return new(!diagnostics.Any(d => d.IsError), diagnostics, displacements, resultants,
            totalIterations, achieved);
    }

    sealed record NewtonAttempt(
        bool Converged, bool Fatal, double[] Displacements, int Iterations,
        IReadOnlyList<FemValidationDiagnostic> Diagnostics);

    static NewtonAttempt RunNewton(
        Assembly assembly, double[] fullExternal, List<int> freeDofs, double[] start,
        double loadFactor, StripNewtonOptions options, double lengthM)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        var u = (double[])start.Clone();
        double previousResidual = double.PositiveInfinity;
        int growthStreak = 0;
        double[,]? frozenTangent = null;

        for (int iteration = 0; iteration < options.MaxIterations; iteration++)
        {
            AssemblyResult state;
            try
            {
                bool needTangent = !options.ModifiedNewton || frozenTangent == null;
                state = assembly.Evaluate(u, needTangent);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                diagnostics.Add(new("plate_strip_nonlinear_state_out_of_bounds", ex.Message));
                // Не фатально: меньший шаг нагрузки может удержать состояние в границах
                // источника. Фатален только выход за границы в самом нулевом состоянии,
                // который проверяется до начала нагружения.
                return new(false, false, u, iteration, diagnostics);
            }

            var residual = new double[fullExternal.Length];
            for (int i = 0; i < residual.Length; i++)
                residual[i] = loadFactor * fullExternal[i] - state.InternalForces[i];

            double scale = ReferenceScale(fullExternal, state.InternalForces, freeDofs, lengthM, loadFactor);
            double residualNorm = ScaledNorm(residual, freeDofs, lengthM) / scale;
            if (residualNorm < options.ResidualTolerance)
                return new(true, false, u, iteration, diagnostics);

            if (residualNorm > previousResidual)
            {
                growthStreak++;
                if (growthStreak == 2)
                    diagnostics.Add(new("plate_strip_nonlinear_diverging",
                        "Невязка растёт две итерации подряд — эвристический признак расходимости " +
                        "(например, нисходящая ветвь диаграммы).", false));
            }
            else growthStreak = 0;
            previousResidual = residualNorm;

            var tangent = state.Tangent ?? frozenTangent!;
            if (options.ModifiedNewton) frozenTangent ??= tangent;

            int n = freeDofs.Count;
            var reduced = new double[n, n];
            var rhs = new double[n];
            for (int i = 0; i < n; i++)
            {
                rhs[i] = residual[freeDofs[i]];
                for (int j = 0; j < n; j++)
                    reduced[i, j] = tangent[freeDofs[i], freeDofs[j]];
            }

            if (n == 0)
                return new(true, false, u, iteration, diagnostics);

            if (!DensePivotSolver.Solve(reduced, rhs, out double[] increment))
            {
                diagnostics.Add(new("plate_strip_nonlinear_singular_tangent",
                    "Касательная матрица производной балки вырождена: проверьте опорную схему " +
                    "и отклик сечения.", false));
                // Не фатально: вырождение чаще всего означает, что пробный шаг увёл сечение
                // в полную пластификацию — меньший шаг остаётся законной попыткой.
                return new(false, false, u, iteration + 1, diagnostics);
            }

            var next = (double[])u.Clone();
            var delta = new double[u.Length];
            for (int i = 0; i < n; i++)
            {
                delta[freeDofs[i]] = increment[i];
                next[freeDofs[i]] += increment[i];
            }
            u = next;

            double displacementScale = Math.Max(DisplacementNorm(u, freeDofs, lengthM), 1e-30);
            if (DisplacementNorm(delta, freeDofs, lengthM) / displacementScale < options.IncrementTolerance)
                return new(true, false, u, iteration + 1, diagnostics);
        }

        diagnostics.Add(new("plate_strip_nonlinear_not_converged",
            $"Ньютон не сошёлся за {options.MaxIterations} итераций при доле нагрузки {loadFactor:F3}.",
            false));
        return new(false, false, u, options.MaxIterations, diagnostics);
    }

    /// <summary>Масштаб для нормировки невязки. Вектор содержит и силы, и моменты, поэтому
    /// моментные компоненты делятся на длину — иначе при L ≫ 1 м они доминируют в норме и
    /// маскируют несходимость осевой задачи (вторая половина ловушки Среза 6). Пол берётся по
    /// масштабу самой задачи, а не абсолютной константой (первая половина).</summary>
    static double ReferenceScale(
        double[] external, double[] internalForces, List<int> freeDofs, double lengthM, double loadFactor)
    {
        double scale = 0.0;
        foreach (int dof in freeDofs)
        {
            double divisor = IsMoment(dof) ? lengthM : 1.0;
            scale = Math.Max(scale, Math.Abs(loadFactor * external[dof]) / divisor);
            scale = Math.Max(scale, Math.Abs(internalForces[dof]) / divisor);
        }
        return scale > 0.0 ? scale : 1.0;
    }

    static double ScaledNorm(double[] vector, List<int> freeDofs, double lengthM)
    {
        double norm = 0.0;
        foreach (int dof in freeDofs)
            norm = Math.Max(norm, Math.Abs(vector[dof]) / (IsMoment(dof) ? lengthM : 1.0));
        return norm;
    }

    /// <summary>Норма перемещений: линейные компоненты приводятся к безразмерному виду делением
    /// на длину полосы, ротации уже безразмерны.</summary>
    static double DisplacementNorm(double[] vector, List<int> freeDofs, double lengthM)
    {
        double norm = 0.0;
        foreach (int dof in freeDofs)
            norm = Math.Max(norm, Math.Abs(vector[dof]) / (IsMoment(dof) ? 1.0 : lengthM));
        return norm;
    }

    /// <summary>DOF в узле идут u, v, w, θy, θz — последние два моментные.</summary>
    static bool IsMoment(int dof) => dof % StripBeamElement.DofPerNode >= 3;

    static double[] BuildExternalVector(
        double[][] elementLoads, KnownEndActions? endActions, int nodeCount, int dofCount)
    {
        var f = new double[dofCount];
        for (int e = 0; e < elementLoads.Length; e++)
        {
            int offset = e * StripBeamElement.DofPerNode;
            for (int i = 0; i < StripBeamElement.DofPerElement; i++)
                f[offset + i] += elementLoads[e][i];
        }

        if (endActions != null)
        {
            if (!endActions.IsFinite)
                throw new ArgumentException("Концевые усилия должны быть конечными.", nameof(endActions));

            // Та же трактовка, что в StripBeamModel: KnownEndActions заданы как значения эпюры
            // на концах, поэтому на первом узле берутся с обратным знаком.
            int last = (nodeCount - 1) * StripBeamElement.DofPerNode;
            f[0] += -endActions.StartN;
            f[3] += -endActions.StartMy;
            f[4] += -endActions.StartMz;
            f[last] += endActions.EndN;
            f[last + 3] += endActions.EndMy;
            f[last + 4] += endActions.EndMz;
        }
        return f;
    }

    /// <summary>Эпюра через концевые усилия элемента g = f_int − f^consistent. Раскладка по
    /// станциям — та же, что в StripBeamModel.ExtractStationResultants: нормаль сечения в левом
    /// узле противоположна, поэтому там берётся −g.</summary>
    static double[][] ExtractStationResultants(Assembly assembly, double[] displacements, int elementCount)
    {
        var result = new double[elementCount + 1][];
        for (int station = 0; station <= elementCount; station++)
            result[station] = new double[3];

        for (int e = 0; e < elementCount; e++)
        {
            double[] g;
            try
            {
                g = assembly.ElementEndActions(e, displacements);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            if (e == 0)
            {
                result[0][0] = -g[0];
                result[0][1] = -g[3];
                result[0][2] = -g[4];
            }
            result[e + 1][0] = g[5];
            result[e + 1][1] = g[8];
            result[e + 1][2] = g[9];
        }
        return result;
    }

    static StripBeamNonlinearSolveResult Failed(
        List<FemValidationDiagnostic> diagnostics, int dofCount, int nodeCount)
    {
        var resultants = new double[nodeCount][];
        for (int i = 0; i < nodeCount; i++)
            resultants[i] = new double[3];
        return new(false, diagnostics, new double[dofCount], resultants, 0, 0.0);
    }

    sealed record AssemblyResult(double[] InternalForces, double[,]? Tangent);

    /// <summary>Сборка внутренних сил и касательной по текущим перемещениям.</summary>
    sealed class Assembly(
        StripSectionSourceGrid sources, double widthM, double[] elementLengths,
        double[][] elementLoads, int dofCount, int elementCount)
    {
        public AssemblyResult Evaluate(double[] displacements, bool tangent)
        {
            var internalForces = new double[dofCount];
            var globalTangent = tangent ? new double[dofCount, dofCount] : null;

            for (int e = 0; e < elementCount; e++)
            {
                var (elementInternal, elementTangent) = Element(e, displacements, tangent);
                int offset = e * StripBeamElement.DofPerNode;
                for (int i = 0; i < StripBeamElement.DofPerElement; i++)
                {
                    internalForces[offset + i] += elementInternal[i];
                    if (globalTangent == null) continue;
                    for (int j = 0; j < StripBeamElement.DofPerElement; j++)
                        globalTangent[offset + i, offset + j] += elementTangent![i, j];
                }
            }
            return new(internalForces, globalTangent);
        }

        /// <summary>Концевые усилия элемента g = f_int − f^consistent.</summary>
        public double[] ElementEndActions(int element, double[] displacements)
        {
            var (elementInternal, _) = Element(element, displacements, tangent: false);
            var g = new double[StripBeamElement.DofPerElement];
            for (int i = 0; i < StripBeamElement.DofPerElement; i++)
                g[i] = elementInternal[i] - elementLoads[element][i];
            return g;
        }

        (double[] Internal, double[,]? Tangent) Element(int element, double[] displacements, bool tangent)
        {
            double length = elementLengths[element];
            var elementDisplacements = new double[StripBeamElement.DofPerElement];
            Array.Copy(displacements, element * StripBeamElement.DofPerNode,
                elementDisplacements, 0, StripBeamElement.DofPerElement);

            int gaussCount = StripBeamElement.GaussXi.Count;
            var resultants = new double[gaussCount][];
            var tangents = tangent ? new double[gaussCount][,] : null;

            for (int g = 0; g < gaussCount; g++)
            {
                var strains = StripBeamElement.Strains(elementDisplacements, length, StripBeamElement.GaussXi[g]);
                var response = NonlinearStripSection.Evaluate(
                    widthM, sources[element], new BeamStrainState(strains[0], strains[1], strains[2]));
                resultants[g] = response.Q;
                if (tangents != null) tangents[g] = response.K;
            }

            var internalForces = StripBeamElement.InternalForces(resultants, length);
            return (internalForces, tangent ? StripBeamElement.Stiffness(tangents!, length) : null);
        }
    }
}
