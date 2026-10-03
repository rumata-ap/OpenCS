using CScore.Import;

namespace OpenCS.Services.Scad;

/// <summary>
/// Перемещения узлов из результатов SCAD (ApiGetDisplace, единицы «м» после
/// <see cref="ScadApiSession.TryInitResult"/>): все строки всех загружений — у нелинейного загружения строка на
/// сохранённый шаг. Только чтение, один поток; память DLL копируется сразу.
/// </summary>
internal static unsafe class ScadApiDisplacementReader
{
    /// <summary>
    /// Прочитать перемещения узлов <paramref name="nodes"/>. Результатов нет → <see cref="ScadApiException"/>
    /// «ScadApiNoResults»; перемещений нет → «ScadApiNoDisplacements».
    /// </summary>
    public static ScadDisplacementSet Read(ScadApiSession s, string? workDirectory, IReadOnlyList<int> nodes,
        CancellationToken ct)
    {
        var n = s.Native;
        nint h = s.Handle;
        if (workDirectory == null || !s.TryInitResult(workDirectory))
            throw new ScadApiException("ScadApiNoResults", [workDirectory ?? ""], s.Phrases());
        if (n.ApiYesDisplace(h) == 0)
            throw new ScadApiException("ScadApiNoDisplacements", [workDirectory]);

        var set = new ScadDisplacementSet();
        uint loads = n.ApiGetResultQuantityLoad(h);
        Span<byte> buf = stackalloc byte[ScadApiLayouts.LoadingDataSize];
        for (uint l = 1; l <= loads; l++)
        {
            uint rows = n.ApiGetQuantityLoadStr(h, ScadApiLayouts.ResultLoad, l);
            for (uint r = 1; r <= rows; r++)
            {
                ct.ThrowIfCancellationRequested();
                buf.Clear();
                string name = "";
                int step = 0;
                fixed (byte* b = buf)
                    if (n.ApiGetResultData(h, ScadApiLayouts.ResultLoad, l, r, b) == 0)
                    {
                        name = ScadApiSession.Str(*(byte**)(b + ScadApiLayouts.LoadingDataName));
                        step = *(ushort*)(b + ScadApiLayouts.LoadingDataStep);
                    }
                var ids = new List<int>(nodes.Count);
                var values = new List<double>(6 * nodes.Count);
                foreach (int node in nodes)
                {
                    double* p = n.ApiGetDisplace(h, l, r, (uint)node);
                    if (p == null) continue;
                    ids.Add(node);
                    for (int k = 0; k < 6; k++) values.Add(p[k]);
                }
                set.Rows.Add(new ScadDisplacementRow((int)l, (int)r, name, step, [.. ids], [.. values]));
            }
        }
        return set;
    }
}
