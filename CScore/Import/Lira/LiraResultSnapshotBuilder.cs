using CScore.Fem.Fragment.Masks;

namespace CScore.Import;

/// <summary>
/// Результаты одного загружения ЛИРЫ, как их отдаёт API результатов, — в единицах документа ЛИРЫ.
/// Перемещения: на узел X Y Z (длина) и UX UY UZ (рад); стержень: N Qy Qz Mx My Mz первого, затем последнего сечения;
/// пластина: Nx Ny Txy (напряжения, сила/длина²), Mx My Mxy, Qx Qy первого сечения.
/// </summary>
public sealed record LiraRawLoad(int Num, string Name, int[] NodeNums, double[] Displacements, int[] BarNums, double[] BarValues,
    int[] ShellNums, double[] ShellValues);

/// <summary>Итог сборки снимка ЛИРЫ: снимок и сведения для отчёта.</summary>
/// <param name="Snapshot">Снимок результатов.</param>
/// <param name="ShellsWithoutThickness">Пластины без толщины в схеме — Nx, Ny, Txy не перевести в погонные, пластина пропущена.</param>
public sealed record LiraResultSnapshotBuild(ImportedResultSnapshot Snapshot, int ShellsWithoutThickness);

/// <summary>
/// Снимок результатов ЛИРЫ для масок фрагмента (<see cref="ImportedResultSnapshot"/>) из значений API результатов
/// (<see cref="LiraRawLoad"/>): единицы документа → СИ (Н, Н·м, Н/м, м, рад), напряжения пластин Nx, Ny, Txy умножены
/// на толщину КЭ. Знаки и оси — ЛИРЫ (в конвенцию OpenCS переводит <see cref="ImportedParentResult"/>). Единицы усилий —
/// <see cref="LiraApiUnits"/> документа; единицы перемещений узлов — <c>displacementToM</c>.
/// </summary>
public static class LiraResultSnapshotBuilder
{
    public static LiraResultSnapshotBuild Build(IReadOnlyList<LiraRawLoad> loads, LiraApiUnits units, double displacementToM,
        IReadOnlyDictionary<int, double> shellThickness, string sourceName, string takenAt)
    {
        const double ToSi = 1e3;
        int noThickness = 0;
        var result = new List<ImportedResultLoad>(loads.Count);
        foreach (var l in loads.OrderBy(l => l.Num))
        {
            var disp = new double[l.Displacements.Length];
            for (int i = 0; i < disp.Length; i++)
                disp[i] = i % ImportedResultLoad.NodeComponents < 3 ? l.Displacements[i] * displacementToM : l.Displacements[i];

            var bars = new double[l.BarValues.Length];
            for (int i = 0; i < bars.Length; i++)
                bars[i] = (i % 6) switch
                {
                    0 or 1 or 2 => units.Force(l.BarValues[i]) * ToSi,
                    _ => units.Moment(l.BarValues[i]) * ToSi,
                };

            var shellNums = new List<int>(l.ShellNums.Length);
            var shells = new List<double>(l.ShellValues.Length);
            for (int k = 0; k < l.ShellNums.Length; k++)
            {
                if (!shellThickness.TryGetValue(l.ShellNums[k], out double h) || !(h > 0)) { noThickness++; continue; }
                var v = l.ShellValues.AsSpan(k * ImportedResultLoad.ShellComponents, ImportedResultLoad.ShellComponents);
                shellNums.Add(l.ShellNums[k]);
                shells.Add(units.Stress(v[0]) * h * ToSi);
                shells.Add(units.Stress(v[1]) * h * ToSi);
                shells.Add(units.Stress(v[2]) * h * ToSi);
                shells.Add(units.MomentPerLength(v[3]) * ToSi);
                shells.Add(units.MomentPerLength(v[4]) * ToSi);
                shells.Add(units.MomentPerLength(v[5]) * ToSi);
                shells.Add(units.PerLength(v[6]) * ToSi);
                shells.Add(units.PerLength(v[7]) * ToSi);
            }

            result.Add(new ImportedResultLoad
            {
                Num = l.Num, Name = l.Name, NodeNums = l.NodeNums, Displacements = disp,
                BarNums = l.BarNums, BarForces = bars, ShellNums = [.. shellNums], ShellForces = [.. shells],
            });
        }
        var snapshot = new ImportedResultSnapshot
        {
            Program = ImportedResultSnapshot.ProgramLira, SourceName = sourceName, TakenAt = takenAt, Loads = result,
        };
        return new LiraResultSnapshotBuild(snapshot, noThickness);
    }
}
