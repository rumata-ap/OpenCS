using System.Diagnostics;
using System.Globalization;
using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>Параметры линейного расчёта схемы.</summary>
public sealed class RcLinearOptions
{
    /// <summary>Число потоков сборки, факторизации, решений по правым частям и снятия полей (−1 — все процессоры).</summary>
    public int MaxDegreeOfParallelism { get; init; } = -1;

    /// <summary>Угол оси x выдачи усилий пластины в осях сечения (<see cref="FemRcModelResult.ShellForceAngles"/>).</summary>
    public IReadOnlyDictionary<int, double>? ShellForceAngles { get; init; }

    /// <summary>Журнал фаз расчёта.</summary>
    public Action<string>? Log { get; init; }
}

/// <summary>Решение одного загружения: перемещения (длина — NDof сетки) и поля в формате шага секущего расчёта.</summary>
public sealed record RcLinearCase(string Name, double[] U, RcSecantStepFields Fields);

/// <summary>Итог линейного расчёта схемы: сетка (номера узлов и КЭ модели) и решения по загружениям модели.</summary>
public sealed record RcLinearRun(RcStructuralMeshBuild Build, IReadOnlyList<RcLinearCase> Cases);

/// <summary>
/// Линейный расчёт <see cref="RcStructuralModel"/>: упругие сечения (<see cref="LinearRcSectionFactory"/>), каждая стадия
/// модели — независимое загружение (сумма загружений с коэффициентами, без накопления и без дробления на шаги). Все
/// загружения решаются одной сборкой и одной факторизацией (<see cref="StructuralMesh.SolveLinear(IReadOnlyList{double[]}, BoundaryConditions)"/>):
/// сборка, суперузловой Холецкий, прямые и обратные ходы по правым частям и снятие полей — многопоточные.
/// </summary>
public static class RcLinearAnalysis
{
    /// <summary>Расчёт модели; отмена — между фазами (<see cref="OperationCanceledException"/>).</summary>
    public static RcLinearRun Run(RcStructuralModel model, RcLinearOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        options ??= new RcLinearOptions();
        var clock = Stopwatch.StartNew();
        var build = RcStructuralMeshBuilder.Build(model, new LinearRcSectionFactory());
        build.Mesh.MaxDegreeOfParallelism = options.MaxDegreeOfParallelism;
        var loads = model.Stages.Select(st => build.Combination(st.Loads)).ToList();
        double tBuild = clock.Elapsed.TotalSeconds;
        ct.ThrowIfCancellationRequested();

        var u = build.Mesh.SolveLinear(loads, build.Bc);
        double tSolve = clock.Elapsed.TotalSeconds - tBuild;
        ct.ThrowIfCancellationRequested();

        var extractor = new RcSecantFieldExtractor(model, build, new ISecantShellState?[build.Mesh.Shells.Count],
            geometric: false, options.ShellForceAngles);
        // Реакции: K·u — одной сборкой K на все загружения.
        var k = loads.Count > 0 ? build.Mesh.AssembleK().ToCsc() : null;
        var cases = new List<RcLinearCase>(loads.Count);
        for (int i = 0; i < loads.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var st = model.Stages[i];
            var reactions = build.Mesh.ComputeReactions(u[i], build.Bc, k!.Multiply(u[i]), loads[i]);
            cases.Add(new RcLinearCase(st.Name, u[i],
                extractor.ExtractLinear(u[i], i, build.BeamCombination(st.Loads), reactions, loads[i])));
        }
        double tFields = clock.Elapsed.TotalSeconds - tBuild - tSolve;
        options.Log?.Invoke(string.Create(CultureInfo.InvariantCulture,
            $"Линейный расчёт: загружений {loads.Count}, неизвестных {build.Mesh.NDof}; сетка и нагрузки {tBuild:0.##} с, " +
            $"сборка, факторизация и решения {tSolve:0.##} с, поля {tFields:0.##} с."));
        return new RcLinearRun(build, cases);
    }
}
