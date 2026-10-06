using CScore;
using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>Параметры секущего расчёта ЖБ-схемы (вариант 1 спеки).</summary>
public sealed class RcSecantOptions
{
    /// <summary>Работа бетона на растяжение до трещины: null — пластины как в сечении, стержни — да.</summary>
    public bool? TensionConcrete { get; init; }

    /// <summary>ψs арматуры у трещин (п. 8.2.32 СП 63).</summary>
    public bool Psi { get; init; } = true;

    /// <summary>Правило выключения растянутого бетона пластин трещиной.</summary>
    public PlateCrackRule PlateCrackRule { get; init; } = PlateCrackRule.Layer;

    /// <summary>
    /// Полоса регуляризации секущей бетона без растяжения у нуля деформаций (<see cref="SecantLaminateBuilder.Build"/>):
    /// без неё слои с ε ≈ 0 «мигают» между E₀ и 0, и шаг не сходится. Ошибка напряжения в полосе — не более E₀·δ/4
    /// (при δ = 1e-5 и E₀ = 26 500 МПа — 0,07 МПа).
    /// </summary>
    public double ZeroStrainBand { get; init; } = 1e-5;

    /// <summary>ν бетона до трещины (Дарвин — Пекнольд); null — как в сечении.</summary>
    public double? PoissonUncracked { get; init; }

    /// <summary>Параметры итераций Пикара.</summary>
    public SecantPicardOptions Solver { get; init; } = new();
}

/// <summary>
/// Фабрика сечений секущего расчёта: слоистые ЖБ-пластины и стержни с сечением CScore получают свой
/// <see cref="ISecantShellState"/>/<see cref="ISecantBeamState"/> на каждый КЭ (сечение и диаграммы делятся, состояние —
/// нет), упругие сечения остаются линейными и не пересчитываются.
/// </summary>
public sealed class SecantRcSectionFactory(RcSecantOptions options) : IRcSectionFactory
{
    private readonly Dictionary<string, PlateSection> _plates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IShellSectionResponse> _elasticShells = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IBeamSectionResponse> _elasticBeams = new(StringComparer.Ordinal);
    private readonly Dictionary<object, ISecantShellState> _shellStates = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, ISecantBeamState> _beamStates = new(ReferenceEqualityComparer.Instance);

    public IShellSectionResponse Shell(RcShell shell)
    {
        var s = shell.Section;
        if (s.Plate != null && s.PlateMaterials != null)
        {
            if (!_plates.TryGetValue(s.Key, out var plate))
            {
                plate = s.Plate.CloneForCalc();
                if (plate.PlateModel != "layered")
                    throw new NotSupportedException($"Оболочка {shell.Id}: секущий расчёт — только для слоистой модели, а не «{plate.PlateModel}».");
                if (options.TensionConcrete is { } t) plate.TensionConcrete = t;
                if (options.PoissonUncracked is { } nu) plate.PoissonUncracked = nu;
                _plates[s.Key] = plate;
            }
            var st = new PlateSecantShellState(plate, s.PlateMaterials, options.Psi, options.PlateCrackRule,
                options.ZeroStrainBand);
            _shellStates[st.Response] = st;
            return st.Response;
        }
        if (_elasticShells.TryGetValue(s.Key, out var cached)) return cached;
        return _elasticShells[s.Key] = new LinearRcSectionFactory().Shell(shell);
    }

    public IBeamSectionResponse Beam(RcBeam beam)
    {
        var s = beam.Section;
        if (s.Cross != null)
        {
            var st = new CrossSectionSecantBeamState(s.Cross, s.Calc, s.TorsionGJ, options.TensionConcrete ?? true, options.Psi);
            _beamStates[st.Response] = st;
            return st.Response;
        }
        if (_elasticBeams.TryGetValue(s.Key, out var cached)) return cached;
        return _elasticBeams[s.Key] = new LinearRcSectionFactory().Beam(beam);
    }

    /// <summary>Состояния по КЭ сетки (null — линейный КЭ); повёрнутые сечения разворачиваются до внутреннего.</summary>
    public (ISecantShellState?[] Shells, ISecantBeamState?[] Beams) StatesFor(StructuralMesh mesh)
    {
        var shells = mesh.Shells.Select(s =>
        {
            var inner = s.Section is RotatedShellResponse r ? r.Inner : s.Section;
            return _shellStates.GetValueOrDefault(inner);
        }).ToArray();
        var beams = mesh.Beams.Select(b => _beamStates.GetValueOrDefault(b.Section)).ToArray();
        return (shells, beams);
    }
}

/// <summary>Итог секущего расчёта схемы: сетка (номера узлов и КЭ модели), состояния КЭ, шаги и журнал.</summary>
public sealed record RcSecantRun(RcStructuralMeshBuild Build, ISecantShellState?[] ShellStates,
    ISecantBeamState?[] BeamStates, IReadOnlyList<SecantLoadStage> Stages, SecantPicardResult Result);

/// <summary>
/// Секущий расчёт <see cref="RcStructuralModel"/>: сетка с секущими сечениями, стадии модели (нагрузка накапливается:
/// стадия добавляет свои загружения к нагрузке конца предыдущей), итерации Пикара (<see cref="SecantPicardSolver"/>).
/// </summary>
public static class RcSecantAnalysis
{
    public static RcSecantRun Run(RcStructuralModel model, RcSecantOptions? options = null)
    {
        options ??= new RcSecantOptions();
        var factory = new SecantRcSectionFactory(options);
        var build = RcStructuralMeshBuilder.Build(model, factory);
        var (shells, beams) = factory.StatesFor(build.Mesh);
        var stages = new List<SecantLoadStage>();
        var f = new double[build.Mesh.NDof];
        foreach (var st in model.Stages)
        {
            var df = build.Combination(st.Loads);
            f = f.Zip(df, (a, b) => a + b).ToArray();
            stages.Add(new SecantLoadStage(st.Name, f, st.Steps));
        }
        var result = new SecantPicardSolver(build.Mesh, build.Bc, shells, beams, options.Solver).Run(stages);
        return new RcSecantRun(build, shells, beams, stages, result);
    }
}
