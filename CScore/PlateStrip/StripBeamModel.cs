using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Результат прогона производной балки полосы.</summary>
/// <param name="IsCalculable">Ложь, если система вырождена или нагрузка не спроецировалась.</param>
/// <param name="Diagnostics">Диагностики прогона, включая диагностики проекции нагрузки.</param>
/// <param name="Displacements">Узловые перемещения, 5 DOF на узел в порядке u, v, w, θy, θz.</param>
/// <param name="StationResultants">Внутренние [N, My, Mz] на каждой станции.</param>
public sealed record StripBeamSolveResult(
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    double[] Displacements,
    double[][] StationResultants);

/// <summary>Линейная одномерная задача производной балки полосы: сборка жёсткости из
/// эквивалентного сечения, наложение опорной схемы, решение и восстановление внутренних усилий
/// на станциях. См.
/// docs/superpowers/specs/2026-09-06-plate-strip-equivalent-beam-load-recovery-design.md,
/// раздел «Балочная задача».
///
/// Вектор узловой нагрузки не считается заново: используется
/// StripLoadConsistentNodalProjection.Project Среза 4 — вторая реализация той же формулы не
/// заводится, а её знаковая конвенция уже приведена к θy = −w′.
///
/// Внутренний расчётный примитив: некорректные входы (нечисловая матрица сечения, невалидная
/// длина или список станций) приводят к исключению; диагностикой возвращается только
/// вырожденность системы и проблемы проекции нагрузки.</summary>
public static class StripBeamModel
{
    public static StripBeamSolveResult Solve(
        double[,] sectionTangent,
        double lengthM,
        IReadOnlyList<double> stationFractions,
        StripBeamSupportScheme scheme,
        StripLoadSet loads,
        KnownEndActions? endActions = null)
    {
        ArgumentNullException.ThrowIfNull(sectionTangent);
        ArgumentNullException.ThrowIfNull(stationFractions);
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(loads);

        if (sectionTangent.GetLength(0) != 3 || sectionTangent.GetLength(1) != 3)
            throw new ArgumentException("Матрица сечения должна быть 3×3.", nameof(sectionTangent));
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            if (!double.IsFinite(sectionTangent[i, j]))
                throw new ArgumentException("Матрица сечения должна быть конечной.", nameof(sectionTangent));
        if (!(lengthM > 0.0) || !double.IsFinite(lengthM))
            throw new ArgumentOutOfRangeException(nameof(lengthM),
                "Длина полосы должна быть конечной и положительной.");
        if (stationFractions.Count < 2)
            throw new ArgumentException("Нужно не менее двух станций.", nameof(stationFractions));

        int nodeCount = stationFractions.Count;
        int elementCount = nodeCount - 1;
        int dofCount = nodeCount * StripBeamElement.DofPerNode;

        var diagnostics = new List<FemValidationDiagnostic>();
        var projection = StripLoadConsistentNodalProjection.Project(loads, lengthM, stationFractions);
        diagnostics.AddRange(projection.Diagnostics);
        if (!projection.IsCalculable)
            return Failed(diagnostics, dofCount, nodeCount);

        var k = new double[dofCount, dofCount];
        var f = new double[dofCount];
        var elementStiffness = new double[elementCount][,];

        for (int e = 0; e < elementCount; e++)
        {
            double le = (stationFractions[e + 1] - stationFractions[e]) * lengthM;
            elementStiffness[e] = StripBeamElement.Stiffness(sectionTangent, le);
            var fe = StripBeamElement.LoadVector(projection.Elements[e]);
            int offset = e * StripBeamElement.DofPerNode;

            for (int i = 0; i < StripBeamElement.DofPerElement; i++)
            {
                f[offset + i] += fe[i];
                for (int j = 0; j < StripBeamElement.DofPerElement; j++)
                    k[offset + i, offset + j] += elementStiffness[e][i, j];
            }
        }

        if (endActions != null)
        {
            if (!endActions.IsFinite)
                throw new ArgumentException("Концевые усилия должны быть конечными.", nameof(endActions));

            // KnownEndActions заданы как значения эпюры на концах. Внутренний резултант в
            // станции 0 равен −g₁ (нормаль сечения противоположна), поэтому узловое действие
            // на первом узле берётся с обратным знаком, а на последнем — с прямым.
            int last = (nodeCount - 1) * StripBeamElement.DofPerNode;
            f[0] += -endActions.StartN;
            f[3] += -endActions.StartMy;
            f[4] += -endActions.StartMz;
            f[last] += endActions.EndN;
            f[last + 3] += endActions.EndMy;
            f[last + 4] += endActions.EndMz;
        }

        var isFixed = BuildConstraintMask(scheme, nodeCount);
        var freeDofs = new List<int>(dofCount);
        for (int i = 0; i < dofCount; i++)
            if (!isFixed[i])
                freeDofs.Add(i);

        var reduced = new double[freeDofs.Count, freeDofs.Count];
        var rhs = new double[freeDofs.Count];
        for (int i = 0; i < freeDofs.Count; i++)
        {
            rhs[i] = f[freeDofs[i]];
            for (int j = 0; j < freeDofs.Count; j++)
                reduced[i, j] = k[freeDofs[i], freeDofs[j]];
        }

        var displacements = new double[dofCount];
        if (freeDofs.Count > 0)
        {
            if (!DensePivotSolver.Solve(reduced, rhs, out double[] solution))
            {
                diagnostics.Add(new("plate_strip_load_recovery_singular_system",
                    "Система производной балки вырождена: проверьте опорную схему и матрицу сечения."));
                return Failed(diagnostics, dofCount, nodeCount);
            }
            for (int i = 0; i < freeDofs.Count; i++)
                displacements[freeDofs[i]] = solution[i];
        }

        var stationResultants = ExtractStationResultants(
            elementStiffness, projection.Elements, displacements, elementCount);

        return new(true, diagnostics, displacements, stationResultants);
    }

    /// <summary>Маска закреплённых DOF по опорной схеме (см. таблицу в StripBeamSupportScheme).</summary>
    public static bool[] BuildConstraintMask(StripBeamSupportScheme scheme, int nodeCount)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        if (nodeCount < 2)
            throw new ArgumentOutOfRangeException(nameof(nodeCount), "Нужно не менее двух узлов.");

        var isFixed = new bool[nodeCount * StripBeamElement.DofPerNode];
        int last = (nodeCount - 1) * StripBeamElement.DofPerNode;

        // Поперечные перемещения закреплены на обоих концах при любом условии.
        isFixed[1] = isFixed[2] = true;
        isFixed[last + 1] = isFixed[last + 2] = true;

        if (scheme.StartCondition == StripBeamEndCondition.Fixed)
            isFixed[3] = isFixed[4] = true;
        if (scheme.EndCondition == StripBeamEndCondition.Fixed)
            isFixed[last + 3] = isFixed[last + 4] = true;

        isFixed[0] = true; // продольное закрепление начала — всегда
        if (scheme.AxialRestraint == StripAxialRestraint.BothEnds)
            isFixed[last] = true;

        return isFixed;
    }

    /// <summary>Внутренние усилия на станциях через концевые усилия элемента g = K·u − f.
    /// Для сечения в правом узле элемента внутренний резултант равен приложенному правой частью
    /// действию (+g₂), для левого узла — с обратным знаком (−g₁), потому что нормаль сечения
    /// противоположна. При consistent nodal loads узловые перемещения балки Эйлера–Бернулли
    /// точны, поэтому концевые усилия дают точные значения эпюры (в отличие от интерполяции
    /// D·B·u внутри элемента).</summary>
    static double[][] ExtractStationResultants(
        double[][,] elementStiffness,
        IReadOnlyList<StripElementNodalLoad> elementLoads,
        double[] displacements,
        int elementCount)
    {
        var result = new double[elementCount + 1][];
        for (int station = 0; station <= elementCount; station++)
            result[station] = new double[3];

        for (int e = 0; e < elementCount; e++)
        {
            int offset = e * StripBeamElement.DofPerNode;
            var fe = StripBeamElement.LoadVector(elementLoads[e]);
            var g = new double[StripBeamElement.DofPerElement];
            for (int i = 0; i < StripBeamElement.DofPerElement; i++)
            {
                double sum = -fe[i];
                for (int j = 0; j < StripBeamElement.DofPerElement; j++)
                    sum += elementStiffness[e][i, j] * displacements[offset + j];
                g[i] = sum;
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

    static StripBeamSolveResult Failed(
        List<FemValidationDiagnostic> diagnostics, int dofCount, int nodeCount)
    {
        var resultants = new double[nodeCount][];
        for (int i = 0; i < nodeCount; i++)
            resultants[i] = new double[3];
        return new(false, diagnostics, new double[dofCount], resultants);
    }
}
