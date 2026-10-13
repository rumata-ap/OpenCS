using CScore;
using CScore.Fem;
using CScore.PlateStrip;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Срез 8d: нелинейный прогон полосы эквивалентного сечения из проекта — собственный Ньютон CScore
/// по «живым» источникам <see cref="PlateSectionLiveResponse"/> (ConstitutiveIntegration) и, по запросу,
/// сверка фибровым стержнем OpenSees. Результат сохраняется в SQLite и привязывается к отпечатку
/// входов эквивалентного сечения.
/// </summary>
public sealed class StripNonlinearRunService
{
    readonly DatabaseService _database;
    readonly IReadOnlyDictionary<int, Material> _materials;

    public StripNonlinearRunService(DatabaseService database, IReadOnlyDictionary<int, Material> materials)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    }

    /// <summary>Выполнить прогон. Сохраняется только результат, у которого решена балка полосы
    /// (даже при остановке до полной нагрузки — это информативно); отказы постановки возвращаются
    /// несохранённым результатом с диагностиками.</summary>
    public async Task<StripNonlinearRunResult> RunAsync(
        EquivalentSection equivalent,
        StripNonlinearRunInput input,
        CalcType calc,
        string? openSeesExecutablePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(equivalent);
        ArgumentNullException.ThrowIfNull(input);

        var result = new StripNonlinearRunResult
        {
            EquivalentSectionId = equivalent.Id,
            CreatedUtc = DateTime.UtcNow,
            Input = input,
            EquivalentInputFingerprint = equivalent.InputFingerprint,
            Formulation = equivalent.BeamFormulation,
            LengthM = equivalent.Strip.Geometry.LengthM
        };
        var diagnostics = new List<FemValidationDiagnostic>();

        string? invalid = input.Validate();
        if (invalid != null)
            return Fail(result, diagnostics, "strip_run_invalid_input", invalid);
        if (equivalent.Id == 0)
            return Fail(result, diagnostics, "strip_run_unsaved_section",
                "Эквивалентное сечение не сохранено в проекте.");
        if (!(equivalent.Strip.Geometry.LengthM > 0.0))
            return Fail(result, diagnostics, "strip_run_invalid_length", "Длина полосы должна быть положительной.");

        var stations = Enumerable.Range(0, input.ElementCount + 1)
            .Select(i => (double)i / input.ElementCount).ToArray();
        result.StationFractions = stations;

        var projectService = new EquivalentSectionProjectService(_database, _materials);
        var sources = projectService.ResolveLiveSources(equivalent, calc, stations);
        diagnostics.AddRange(sources.Diagnostics);
        if (!sources.IsCalculable || sources.Grid == null)
        {
            result.Diagnostics = diagnostics;
            return result;
        }

        var loads = input.Loads.Select((l, i) => l.ToStripLoad(i)).ToList();
        foreach (var load in loads)
        {
            try { load.Validate(); }
            catch (ArgumentException ex)
            {
                return Fail(result, diagnostics, "strip_run_invalid_load", ex.Message);
            }
        }

        var scheme = input.ToScheme();
        double width = equivalent.Strip.ExplicitWidthM;
        double length = equivalent.Strip.Geometry.LengthM;
        var newton = new StripNewtonOptions(LoadSteps: input.LoadSteps, MaxIterations: 60);

        // Ньютон не асинхронен: выносим с UI-потока.
        var solved = await Task.Run(
            () => SolveBeam(equivalent, sources.Grid, width, length, stations, scheme, loads, newton),
            cancellationToken);
        diagnostics.AddRange(solved.Diagnostics);
        // Остановка до полной нагрузки (исчерпана несущая способность) возвращает состояние на последней
        // сошедшейся доле нагрузки — оно информативно и сохраняется с IsConverged = false.
        if (solved.Resultants.Length == 0 || solved.Deflections.Length == 0)
        {
            result.Diagnostics = diagnostics;
            return result;
        }

        result.TotalIterations = solved.Iterations;
        result.AchievedLoadFactor = solved.AchievedLoadFactor;
        result.IsConverged = Math.Abs(solved.AchievedLoadFactor - 1.0) < 1e-6;
        result.BeamResultants = solved.Resultants;
        result.BeamDeflections = solved.Deflections;
        result.MaxDeflectionM = solved.Deflections.Length == 0 ? 0.0 : solved.Deflections.Max(Math.Abs);
        if (!result.IsConverged)
            diagnostics.Add(new("strip_run_partial_load",
                $"Ньютон остановился на коэффициенте нагрузки {solved.AchievedLoadFactor:F3} (< 1): " +
                "полная нагрузка недостижима для принятой диаграммы материала.", false));

        if (input.FiberCheck && !result.IsConverged)
            diagnostics.Add(new("strip_run_fiber_skipped_partial",
                "Фибровая сверка пропущена: полная нагрузка не достигнута, сравнение по частичному " +
                "состоянию некорректно.", false));
        else if (input.FiberCheck)
            await RunFiberCheckAsync(
                equivalent, sources, scheme, loads, input, calc, openSeesExecutablePath, result, diagnostics,
                cancellationToken);

        result.Diagnostics = diagnostics;
        _database.SaveStripNonlinearResult(result);
        return result;
    }

    sealed record BeamSolution(
        bool IsCalculable, IReadOnlyList<FemValidationDiagnostic> Diagnostics, double[][] Resultants,
        double[] Deflections, int Iterations, double AchievedLoadFactor);

    static BeamSolution SolveBeam(
        EquivalentSection equivalent, StripSectionSourceGrid grid, double width, double length,
        IReadOnlyList<double> stations, StripBeamSupportScheme scheme, IReadOnlyList<StripLoad> loads,
        StripNewtonOptions newton)
    {
        if (equivalent.BeamFormulation == StripBeamFormulation.Timoshenko)
        {
            var law = new NonlinearStripTimoshenkoSectionLaw(grid, width);
            var full = StripBeamTimoshenkoModel.Solve(
                law, length, stations, scheme, new StripLoadSet(loads), null, newton, [],
                StripTorsionRestraint.BothEnds);
            int nodes = full.Displacements.Length / StripBeamElementTimoshenko.DofPerNode;
            var w = new double[nodes];
            for (int n = 0; n < nodes; n++)
                w[n] = full.Displacements[n * StripBeamElementTimoshenko.DofPerNode + StripBeamElementTimoshenko.W];
            var resultants = full.StationResultants.Select(r => new[] { r[0], r[1], r[2] }).ToArray();
            return new(full.IsCalculable, full.Diagnostics, resultants, w, full.TotalIterations,
                full.AchievedLoadFactor);
        }

        var beam = StripBeamNonlinearModel.Solve(
            grid, width, length, stations, scheme, new StripLoadSet(loads), null, newton, []);
        int count = beam.Displacements.Length / StripBeamElement.DofPerNode;
        var deflections = new double[count];
        for (int n = 0; n < count; n++)
            deflections[n] = beam.Displacements[n * StripBeamElement.DofPerNode + 2];
        return new(beam.IsCalculable, beam.Diagnostics, beam.StationResultants, deflections,
            beam.TotalIterations, beam.AchievedLoadFactor);
    }

    async Task RunFiberCheckAsync(
        EquivalentSection equivalent, EquivalentSectionProjectService.StripLiveSources sources,
        StripBeamSupportScheme scheme, IReadOnlyList<StripLoad> loads, StripNonlinearRunInput input,
        CalcType calc, string? openSeesExecutablePath, StripNonlinearRunResult result,
        List<FemValidationDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(openSeesExecutablePath))
        {
            diagnostics.Add(new("strip_run_opensees_not_found",
                "Фибровая сверка не выполнена: не найден исполняемый файл OpenSees " +
                "(укажите путь в настройках расчёта или переменной OPENSEES_HOME).", false));
            return;
        }

        if (equivalent.BeamFormulation == StripBeamFormulation.Timoshenko)
            diagnostics.Add(new("strip_run_fiber_timoshenko_bending_only",
                "Фибровая сверка — формулировка Эйлера–Бернулли: сдвиговая податливость сверяемой " +
                "балки в стержне OpenSees не учтена, сравнивается изгибная часть.", false));

        var fiber = await PlateStripFiberBeamRunner.RunAsync(
            sources.Background!, sources.Field!, equivalent.Strip, result.StationFractions,
            equivalent.WidthIntegrationPoints, scheme, loads, null, [], input.LoadSteps,
            new PlateStripFiberCheck { Materials = _materials, Calc = calc },
            openSeesExecutablePath, cancellationToken);

        diagnostics.AddRange(fiber.Diagnostics);
        result.FiberChecked = true;
        if (!fiber.IsConverged) return;

        result.FiberResultants = fiber.StationResultants.ToArray();
        result.FiberDeflections = fiber.StationDeflections.ToArray();
        result.FiberMaxDeflectionM = fiber.MaxDeflectionM;
        result.FiberMomentMismatch = StripResultMismatch.Moment(result.BeamResultants, fiber.StationResultants);
        result.FiberDeflectionMismatch = StripResultMismatch.Relative(result.MaxDeflectionM, fiber.MaxDeflectionM);
    }

    static StripNonlinearRunResult Fail(
        StripNonlinearRunResult result, List<FemValidationDiagnostic> diagnostics, string code, string message)
    {
        diagnostics.Add(new(code, message));
        result.Diagnostics = diagnostics;
        return result;
    }
}
