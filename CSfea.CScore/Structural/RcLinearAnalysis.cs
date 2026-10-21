using System.Diagnostics;
using System.Globalization;
using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.CScoreBridge.Structural;

/// <summary>Параметры линейного расчёта схемы.</summary>
public sealed class RcLinearOptions
{
    /// <summary>Число потоков сборки, факторизации, решений по правым частям и снятия полей (−1 — все процессоры).</summary>
    public int MaxDegreeOfParallelism { get; init; } = -1;

    /// <summary>Угол оси x выдачи усилий пластины в осях сечения (<see cref="FemRcModelResult.ShellForceAngles"/>).</summary>
    public IReadOnlyDictionary<int, double>? ShellForceAngles { get; init; }

    /// <summary>Писать в поля деформации сечений (<see cref="RcSecantStepFields.HasStrains"/>).</summary>
    public bool RecordSectionStrains { get; init; }

    /// <summary>Журнал фаз расчёта.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>
    /// Сечения КЭ; null — упругие (<see cref="LinearRcSectionFactory"/>). Фабрика с <see cref="IRcInitialStiffness"/>
    /// (<see cref="RecordedRcSectionFactory"/>) даёт полям начальные жёсткости — секущие при этом её матрицы.
    /// </summary>
    public IRcSectionFactory? Sections { get; init; }

    /// <summary>
    /// Замороженное состояние (запись секущего прогона с геометрической нелинейностью): все загружения решаются на
    /// K + K_σ по его перемещениям (<see cref="RcLinearAnalysis.StateVector"/>) — P-Δ определяющего нагружения источника
    /// инженерной нелинейности; null — расчёт первого порядка.
    /// </summary>
    public RcSecantStepFields? GeometricFields { get; init; }

    /// <summary>При <see cref="GeometricFields"/>: K_σ оболочек и в своей плоскости (как <c>ShellInPlanePDelta</c> источника).</summary>
    public bool ShellInPlanePDelta { get; init; } = true;
}

/// <summary>Решение одного загружения: перемещения (длина — NDof сетки) и поля в формате шага секущего расчёта.</summary>
public sealed record RcLinearCase(string Name, double[] U, RcSecantStepFields Fields);

/// <summary>
/// Итог линейного расчёта схемы: сетка (номера узлов и КЭ модели) и решения по загружениям модели;
/// <paramref name="GeometricBeams"/>, <paramref name="GeometricShells"/> — КЭ с K_σ замороженного состояния (без него — 0).
/// </summary>
public sealed record RcLinearRun(RcStructuralMeshBuild Build, IReadOnlyList<RcLinearCase> Cases,
    int GeometricBeams = 0, int GeometricShells = 0);

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
        var build = RcStructuralMeshBuilder.Build(model, options.Sections ?? new LinearRcSectionFactory());
        build.Mesh.MaxDegreeOfParallelism = options.MaxDegreeOfParallelism;
        var loads = model.Stages.Select(st => build.Combination(st.Loads)).ToList();
        double tBuild = clock.Elapsed.TotalSeconds;
        ct.ThrowIfCancellationRequested();

        var mesh = build.Mesh;
        var uDet = options.GeometricFields is { } gf ? StateVector(build, gf) : null;
        bool inPlane = options.ShellInPlanePDelta;
        Func<int, double[,]>? elementK = uDet == null ? null : e =>
        {
            var k = mesh.ElementK(e);
            Dense.AddScaledInPlace(k, mesh.ElementKGeometric(e, uDet, inPlane), 1.0);
            return k;
        };
        var u = mesh.SolveLinear(loads, build.Bc, elementK);
        double tSolve = clock.Elapsed.TotalSeconds - tBuild;
        ct.ThrowIfCancellationRequested();

        var extractor = new RcSecantFieldExtractor(model, build, new ISecantShellState?[build.Mesh.Shells.Count],
            geometric: false, options.ShellForceAngles, recordStrains: options.RecordSectionStrains,
            initial: options.Sections as IRcInitialStiffness);
        // Реакции: K·u (с замороженным состоянием — (K + K_σ)·u) — одной сборкой на все загружения.
        var k = loads.Count > 0 ? mesh.AssembleK().ToCsc() : null;
        var kg = loads.Count > 0 && uDet != null ? mesh.AssembleKGeometric(uDet, inPlane).ToCsc() : null;
        var cases = new List<RcLinearCase>(loads.Count);
        for (int i = 0; i < loads.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var st = model.Stages[i];
            var fInt = k!.Multiply(u[i]);
            if (kg != null)
            {
                var fg = kg.Multiply(u[i]);
                for (int j = 0; j < fInt.Length; j++) fInt[j] += fg[j];
            }
            var reactions = mesh.ComputeReactions(u[i], build.Bc, fInt, loads[i]);
            cases.Add(new RcLinearCase(st.Name, u[i],
                extractor.ExtractLinear(u[i], i, build.BeamCombination(st.Loads), reactions, loads[i])));
        }
        double tFields = clock.Elapsed.TotalSeconds - tBuild - tSolve;
        int geoBeams = uDet == null ? 0 : Enumerable.Range(0, mesh.Beams.Count).Count(b => mesh.BeamAxialForce(b, uDet) != 0.0);
        int geoShells = uDet == null ? 0 : mesh.Shells.Count;
        string geo = uDet == null ? "" : $", K_σ состояния: стержней {geoBeams}, оболочек {geoShells}";
        options.Log?.Invoke(string.Create(CultureInfo.InvariantCulture,
            $"Линейный расчёт: загружений {loads.Count}, неизвестных {mesh.NDof}{geo}; сетка и нагрузки {tBuild:0.##} с, " +
            $"сборка, факторизация и решения {tSolve:0.##} с, поля {tFields:0.##} с."));
        return new RcLinearRun(build, cases, geoBeams, geoShells);
    }

    /// <summary>
    /// Перемещения записи <paramref name="f"/> (по <see cref="RcSecantStepFields.NodeIds"/>) в векторе DOF сетки
    /// <paramref name="build"/>; узлы сетки, которых нет в записи, — нули.
    /// </summary>
    public static double[] StateVector(RcStructuralMeshBuild build, RcSecantStepFields f)
    {
        var u = new double[build.Mesh.NDof];
        for (int s = 0; s < f.NodeIds.Length; s++)
        {
            if (!build.NodeIndex.ContainsKey(f.NodeIds[s])) continue;
            for (int c = 0; c < 6; c++)
                u[build.Dof(f.NodeIds[s], c)] = f.Displacements[RcSecantStepFields.NodeComponents * s + c];
        }
        return u;
    }
}
