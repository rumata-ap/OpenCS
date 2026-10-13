using CScore;
using CScore.Fem;
using CScore.PlateRebar;
using CScore.PlateStrip;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.CScore.Fragments;

/// <summary>Настройки фибровой сверки полосы (Срез 8c). Наличие объекта в запросе включает сверку.</summary>
public sealed class PlateStripFiberCheck
{
    /// <summary>Материалы проекта по Id (бетон и арматура плитного сечения).</summary>
    public IReadOnlyDictionary<int, Material> Materials { get; init; } = new Dictionary<int, Material>();

    /// <summary>Вид расчёта диаграмм.</summary>
    public CalcType Calc { get; init; } = CalcType.C;

    /// <summary>Число точек интегрирования элемента forceBeamColumn.</summary>
    public int IntegrationPoints { get; init; } = 5;

    /// <summary>Таймаут запуска OpenSees.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>Результат фибровой сверки полосы.</summary>
public sealed class PlateStripFiberBeamResult
{
    /// <summary>Расчёт дошёл до полной нагрузки.</summary>
    public bool IsConverged { get; init; }

    /// <summary>Усилия [N, My, Mz] на станциях, кН и кН·м (внутренние, как у собственной балки).</summary>
    public IReadOnlyList<double[]> StationResultants { get; init; } = [];

    /// <summary>Прогибы w на станциях, м.</summary>
    public IReadOnlyList<double> StationDeflections { get; init; } = [];

    /// <summary>Максимальный |w|, м.</summary>
    public double MaxDeflectionM { get; init; }

    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Срез 8c: независимый путь нелинейного расчёта полосы — стержень OpenSees с фибровым сечением,
/// построенным из <see cref="PlateSection"/> (<see cref="PlateStripFiberSectionBuilder"/>).
/// Носитель нелинейности полосы по-прежнему собственный Ньютон CScore; этот путь — только сверка.
/// </summary>
public static class PlateStripFiberBeamRunner
{
    /// <summary>Выполнить сверку для уже сформированной постановки полосы.</summary>
    public static Task<PlateStripFiberBeamResult> RunAsync(
        PlateSection section,
        PlateRebarField rebarField,
        PlateStripBeamAnalogy analogy,
        IReadOnlyList<double> stationFractions,
        int widthPointCount,
        StripBeamSupportScheme scheme,
        IReadOnlyList<StripLoad> loads,
        KnownEndActions? endActions,
        IReadOnlyList<StripPrescribedDisplacement> prescribed,
        int loadSteps,
        PlateStripFiberCheck check,
        string openSeesExecutablePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(rebarField);

        // Базовый layout поля — источник истины PlateSection.RebarLayers (как у PlateRebarField.From).
        var field = rebarField.BaseLayout.Count == 0 && rebarField.Zones.Count == 0 && section.RebarLayers.Count > 0
            ? new PlateRebarField(section.RebarLayers, [])
            : rebarField;

        return RunAsync(
            section, new FieldStripRebarSource(field), analogy, stationFractions, widthPointCount, scheme, loads,
            endActions, prescribed, loadSteps, check, openSeesExecutablePath, cancellationToken);
    }

    /// <summary>То же с произвольным источником армирования вдоль полосы (раскладка OpenCS, КЭ импортированной схемы).</summary>
    public static async Task<PlateStripFiberBeamResult> RunAsync(
        PlateSection section,
        IStripRebarSource rebarSource,
        PlateStripBeamAnalogy analogy,
        IReadOnlyList<double> stationFractions,
        int widthPointCount,
        StripBeamSupportScheme scheme,
        IReadOnlyList<StripLoad> loads,
        KnownEndActions? endActions,
        IReadOnlyList<StripPrescribedDisplacement> prescribed,
        int loadSteps,
        PlateStripFiberCheck check,
        string openSeesExecutablePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(rebarSource);
        ArgumentNullException.ThrowIfNull(check);

        var diagnostics = new List<FemValidationDiagnostic>();
        PlateStripFiberBeamResult Fail() => new() { Diagnostics = diagnostics };

        var layouts = PlateStripFiberLayouts.Resolve(rebarSource, analogy, stationFractions, widthPointCount);
        diagnostics.AddRange(layouts.Diagnostics);
        if (!layouts.IsCalculable) return Fail();

        double width = analogy.ExplicitWidthM;
        var sections = new List<OpenSeesSectionModel>(layouts.PerElement.Count);
        var cache = new Dictionary<string, OpenSeesSectionModel>();
        var seenWarnings = new HashSet<string>();
        int nextMaterialTag = 1;
        try
        {
            for (int e = 0; e < layouts.PerElement.Count; e++)
            {
                var element = layouts.PerElement[e];
                // Отверстия региона: доля материала в ячейках ширины элемента (между его станциями).
                var fractions = StripMaterialProfile.ElementFractions(
                    analogy, stationFractions[e], stationFractions[e + 1], element.Count);
                string key = string.Join("|", element.Select(PlateRebarLayoutFingerprint.Compute)) + "#" +
                             string.Join(",", fractions.Select(f => f.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)));
                if (!cache.TryGetValue(key, out var openSees))
                {
                    var built = PlateStripFiberSectionBuilder.Build(
                        section, element, width, check.Materials, check.Calc, fractions);
                    foreach (var d in built.Diagnostics)
                        if (seenWarnings.Add(d.Code + d.Message)) diagnostics.Add(d);
                    if (!built.IsCalculable) return Fail();

                    openSees = CrossSectionToOpenSeesAdapter.Build(
                        built.Section!, check.Calc, check.Materials, customPool: null,
                        new CrossSectionToOpenSeesAdapter.Options
                        {
                            GJ = TorsionStiffnessNm2(section, width, check.Materials),
                            ConsiderConcreteTension = section.TensionConcrete,
                            // Теги материалов OpenSees глобальны: у каждого сечения свой диапазон.
                            FirstMaterialTag = nextMaterialTag
                        });
                    nextMaterialTag += Math.Max(1, openSees.Materials.Count);
                    cache[key] = openSees;
                }
                sections.Add(openSees);
            }
        }
        catch (CScoreMappingException ex)
        {
            diagnostics.Add(new("plate_strip_fiber_section_mapping_failed", ex.Message));
            return Fail();
        }

        var assembled = PlateStripFiberBeamModelBuilder.Build(
            sections, stationFractions, analogy.Geometry.LengthM, scheme, loads, endActions, prescribed,
            loadSteps, check.IntegrationPoints);
        diagnostics.AddRange(assembled.Diagnostics);
        if (!assembled.IsCalculable) return Fail();

        string root = Path.Combine(Path.GetTempPath(), "opencs-strip-fiber", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new FemNonlinearAnalysisService(
                new FemNonlinearTclGenerator(),
                new OpenSeesProcessRunner(),
                new OpenSeesArtifactStore(root),
                new FemNonlinearResultParser());
            var run = await service.RunAsync(
                assembled.Model!,
                new OpenSeesRunRequest
                {
                    ExecutablePath = openSeesExecutablePath,
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = check.Timeout
                },
                cancellationToken);

            foreach (var message in run.Diagnostics)
                diagnostics.Add(new("plate_strip_fiber_opensees", message, false));

            var step = run.Steps.LastOrDefault(s => s.Converged);
            if (step is null)
            {
                diagnostics.Add(new("plate_strip_fiber_no_converged_step",
                    "Фибровый прогон OpenSees не дал ни одного сошедшегося шага."));
                return Fail();
            }

            bool full = Math.Abs(step.LoadFactor - 1.0) < 1e-6;
            if (!full)
                diagnostics.Add(new("plate_strip_fiber_partial_load",
                    $"Фибровый прогон остановлен на коэффициенте нагрузки {step.LoadFactor:F3} (< 1): " +
                    "сверка по частичной нагрузке недостоверна.", false));

            return Extract(step, stationFractions.Count, full, diagnostics);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Усилия и прогибы станций: усилие сечения — действие на положительную грань элемента
    /// (Nj, Myj, Mzj) слева от станции, а в начале — со знаком минус действие на отрицательную грань
    /// (−Ni, −Myi, −Mzi) первого элемента. Силы переводятся из Н в кН.</summary>
    static PlateStripFiberBeamResult Extract(
        FemNonlinearStepResult step, int stationCount, bool full, List<FemValidationDiagnostic> diagnostics)
    {
        const double toKilo = 1.0 / 1000.0;
        var forces = step.ElementForces.ToDictionary(f => f.ElemTag);
        var stations = new List<double[]>(stationCount);
        var deflections = new List<double>(stationCount);
        var displacements = step.Displacements.ToDictionary(d => d.NodeTag);

        for (int i = 0; i < stationCount; i++)
        {
            double[] r;
            if (i == 0)
            {
                var f = forces[1];
                r = [-f.Ni * toKilo, -f.Myi * toKilo, -f.Mzi * toKilo];
            }
            else
            {
                var f = forces[i];
                r = [f.Nj * toKilo, f.Myj * toKilo, f.Mzj * toKilo];
            }
            stations.Add(r);
            deflections.Add(displacements[i + 1].Uz);
        }

        return new PlateStripFiberBeamResult
        {
            IsConverged = full,
            StationResultants = stations,
            StationDeflections = deflections,
            MaxDeflectionM = deflections.Count == 0 ? 0.0 : deflections.Max(Math.Abs),
            Diagnostics = diagnostics
        };
    }

    /// <summary>Крутильная жёсткость фибровой секции, Н·м². Кручение в сверке закреплено, значение
    /// нужно лишь невырожденности матрицы гибкости: G·J прямоугольника b×H, G = E/2,4.</summary>
    static double TorsionStiffnessNm2(
        PlateSection section, double widthM, IReadOnlyDictionary<int, Material> materials)
    {
        double eKpa = materials.TryGetValue(section.ConcreteMaterialId, out var concrete) && concrete.E > 0
            ? concrete.E
            : 30_000_000.0;
        double g = eKpa * 1000.0 / 2.4;
        double j = widthM * Math.Pow(section.H, 3) / 3.0;
        return g * j;
    }
}
