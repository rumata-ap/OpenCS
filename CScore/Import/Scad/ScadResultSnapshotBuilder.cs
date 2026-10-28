using CScore.Fem.Fragment.Masks;

namespace CScore.Import;

/// <summary>Итог сборки снимка SCAD: снимок и сведения для отчёта.</summary>
/// <param name="Snapshot">Снимок результатов.</param>
/// <param name="ShortBars">Стержни с одним сечением — сил концов нет.</param>
/// <param name="ShellsWithoutThickness">Пластины без толщины в схеме — Nx, Ny, Txy не перевести в погонные, пластина пропущена.</param>
/// <param name="LoadMismatch">КЭ, у которых число загружений усилий не совпадает ни с числом загружений, ни с числом строк перемещений.</param>
public sealed record ScadResultSnapshotBuild(ImportedResultSnapshot Snapshot, int ShortBars, int ShellsWithoutThickness,
    int LoadMismatch);

/// <summary>
/// Снимок результатов SCAD для масок фрагмента (<see cref="ImportedResultSnapshot"/>) из перемещений
/// (<see cref="ScadDisplacementSet"/>, м и рад) и усилий загружений КЭ (<see cref="ScadElementForces"/>, т и м). Знаки и
/// оси — SCAD; единицы — СИ; напряжения пластин NX, NY, TXY умножены на толщину КЭ. Нелинейное загружение — последняя
/// строка (шаг) перемещений. Усилия КЭ — по индексу загружения ApiGetEffors: если их столько же, сколько загружений, —
/// номер загружения; если столько же, сколько строк перемещений, — строка последнего шага; иначе усилия КЭ не берутся.
/// Первое сечение стержня — у узла 1, последнее — у узла 2.
/// </summary>
public static class ScadResultSnapshotBuilder
{
    public static ScadResultSnapshotBuild Build(ScadDisplacementSet displacements, IReadOnlyList<ScadElementForces> forces,
        ScadResultCatalog catalog, IReadOnlyDictionary<int, double> shellThickness, double tonToKn, string sourceName,
        string takenAt)
    {
        double tf = tonToKn * 1e3;
        // Загружения: по строкам перемещений (последняя строка — последний шаг); без перемещений — по усилиям.
        var rows = displacements.Rows.OrderBy(r => r.Load).ThenBy(r => r.Row).ToList();
        var loadNums = rows.Select(r => r.Load).Distinct().ToList();
        if (loadNums.Count == 0)
            loadNums = Enumerable.Range(1, forces.Count > 0 ? forces.Max(f => f.LoadCount) : 0).ToList();
        var lastRow = rows.GroupBy(r => r.Load).ToDictionary(g => g.Key, g => g.Last());
        var flatIndex = new Dictionary<int, int>();
        for (int i = 0; i < rows.Count; i++) flatIndex[rows[i].Load] = i;

        int shortBars = 0, noThickness = 0, mismatch = 0;
        var loads = new List<ImportedResultLoad>(loadNums.Count);
        var bars = new List<(int Num, List<double> Values)>[loadNums.Count];
        var shells = new List<(int Num, List<double> Values)>[loadNums.Count];
        for (int i = 0; i < loadNums.Count; i++) { bars[i] = []; shells[i] = []; }

        foreach (var e in forces.OrderBy(f => f.ElemId))
        {
            int[]? index = IndexOf(e.LoadCount);
            if (index == null) { mismatch++; continue; }
            if (e.Kind == ScadElementKind.Shell)
            {
                if (!shellThickness.TryGetValue(e.ElemId, out double h) || !(h > 0)) { noThickness++; continue; }
                for (int i = 0; i < loadNums.Count; i++)
                {
                    var v = e.LoadCase(0, index[i]);
                    shells[i].Add((e.ElemId,
                    [
                        Get(e.Types, v, ScadApiForceMapper.ShellNx) * h * tf, Get(e.Types, v, ScadApiForceMapper.ShellNy) * h * tf,
                        Get(e.Types, v, ScadApiForceMapper.ShellTxy) * h * tf,
                        Get(e.Types, v, ScadApiForceMapper.ShellMx) * tf, Get(e.Types, v, ScadApiForceMapper.ShellMy) * tf,
                        Get(e.Types, v, ScadApiForceMapper.ShellMxy) * tf,
                        Get(e.Types, v, ScadApiForceMapper.ShellQx) * tf, Get(e.Types, v, ScadApiForceMapper.ShellQy) * tf,
                    ]));
                }
            }
            else
            {
                if (e.Points < 2) { shortBars++; continue; }
                for (int i = 0; i < loadNums.Count; i++)
                {
                    var values = new List<double>(ImportedResultLoad.BarComponents);
                    foreach (int p in new[] { 0, e.Points - 1 })
                    {
                        var v = e.LoadCase(p, index[i]);
                        values.AddRange(
                        [
                            Get(e.Types, v, ScadApiForceMapper.BarN) * tf, Get(e.Types, v, ScadApiForceMapper.BarQy) * tf,
                            Get(e.Types, v, ScadApiForceMapper.BarQz) * tf, Get(e.Types, v, ScadApiForceMapper.BarMk) * tf,
                            Get(e.Types, v, ScadApiForceMapper.BarMy) * tf, Get(e.Types, v, ScadApiForceMapper.BarMz) * tf,
                        ]);
                    }
                    bars[i].Add((e.ElemId, values));
                }
            }
        }

        for (int i = 0; i < loadNums.Count; i++)
        {
            int num = loadNums[i];
            var row = lastRow.GetValueOrDefault(num);
            string name = num - 1 < catalog.LoadNames.Count && !string.IsNullOrEmpty(catalog.LoadNames[num - 1])
                ? catalog.LoadNames[num - 1] : row?.Name ?? "";
            loads.Add(new ImportedResultLoad
            {
                Num = num, Name = name, Step = row is { Row: > 1 } ? row.Step : 0,
                NodeNums = row?.Nodes ?? [], Displacements = row?.Values ?? [],
                BarNums = [.. bars[i].Select(b => b.Num)], BarForces = [.. bars[i].SelectMany(b => b.Values)],
                ShellNums = [.. shells[i].Select(s => s.Num)], ShellForces = [.. shells[i].SelectMany(s => s.Values)],
            });
        }
        var snapshot = new ImportedResultSnapshot
        {
            Program = ImportedResultSnapshot.ProgramScad, SourceName = sourceName, TakenAt = takenAt, Loads = loads,
        };
        return new ScadResultSnapshotBuild(snapshot, shortBars, noThickness, mismatch);

        // Индекс загружения ApiGetEffors для каждого загружения снимка; null — число загружений КЭ не сопоставить.
        int[]? IndexOf(int loadCount)
        {
            if (loadCount == loadNums.Count && loadNums.Select((n, i) => n == i + 1).All(b => b))
                return [.. loadNums.Select(n => n - 1)];
            if (rows.Count > 0 && loadCount == rows.Count) return [.. loadNums.Select(n => flatIndex[n])];
            return null;
        }
    }

    static double Get(byte[] types, ReadOnlySpan<double> values, byte code)
    {
        int i = Array.IndexOf(types, code);
        return i >= 0 && i < values.Length ? values[i] : 0;
    }
}
