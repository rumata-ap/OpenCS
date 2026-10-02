using CScore.Import;

namespace OpenCS.Services.Scad;

/// <summary>Что читать из результатов SCAD: усилия загружений, комбинаций загружений («РСН») или РСУ.</summary>
internal enum ScadForceReadKind { LoadCases, Combinations, Rsu }

/// <summary>
/// Итог чтения результатов: записи для <see cref="ScadForceSetBuilder"/> и сведения для предупреждений
/// (текст — в потоке UI по ресурсам).
/// </summary>
/// <param name="Forces">Усилия загружений или комбинаций (пусто для РСУ).</param>
/// <param name="Rsu">РСУ (пусто для загружений и комбинаций).</param>
/// <param name="Catalog">Имена загружений и комбинаций.</param>
/// <param name="MissingElements">КЭ цели, которых нет в проекте SCAD (или удалены).</param>
/// <param name="WrongKindElements">КЭ другого вида (стержень в SCAD ↔ пластина в OpenCS и наоборот).</param>
/// <param name="NoResultElements">КЭ без результатов (ApiGetEffors/ApiGetRsu не дали данных или строк РСУ нет).</param>
internal sealed record ScadApiForceReadResult(IReadOnlyList<ScadElementForces> Forces,
    IReadOnlyList<ScadRsuElement> Rsu, ScadResultCatalog Catalog, int MissingElements, int WrongKindElements,
    int NoResultElements);

/// <summary>
/// Чтение усилий, комбинаций и РСУ КЭ через открытую <see cref="ScadApiSession"/> (результаты в «т, м»,
/// <see cref="ScadApiSession.TryInitResult"/>). Только чтение, один поток; память DLL копируется сразу.
/// </summary>
internal static unsafe class ScadApiForceReader
{
    const int CancelStride = 256;

    /// <summary>ApiGetEffors TypeRead: 0 — всё, 1 — без комбинаций, 2 — только комбинации.</summary>
    const byte ReadAll = 0, ReadNoComb = 1;

    /// <summary>
    /// Прочитать результаты КЭ <paramref name="targets"/> (номер КЭ SCAD → вид КЭ в OpenCS).
    /// Результатов в <paramref name="workDirectory"/> нет → <see cref="ScadApiException"/> «ScadApiNoResults»;
    /// больше половины КЭ цели нет в проекте или они другого вида → «ScadApiForcesOtherProject».
    /// </summary>
    public static ScadApiForceReadResult Read(ScadApiSession s, string? workDirectory,
        IReadOnlyDictionary<int, ScadElementKind> targets, ScadForceReadKind kind,
        IProgress<double>? progress, CancellationToken ct)
    {
        var n = s.Native;
        nint h = s.Handle;
        if (workDirectory == null || !s.TryInitResult(workDirectory))
            throw new ScadApiException("ScadApiNoResults", [workDirectory ?? ""], s.Phrases());
        bool has = kind == ScadForceReadKind.Rsu ? n.ApiYesRSU(h) != 0 : n.ApiYesEffors(h) != 0;
        if (!has)
            throw new ScadApiException(kind == ScadForceReadKind.Rsu ? "ScadApiNoRsu" : "ScadApiNoForces", [workDirectory]);

        var catalog = ReadCatalog(s, kind);
        var forces = new List<ScadElementForces>();
        var rsu = new List<ScadRsuElement>();
        int missing = 0, wrongKind = 0, noResult = 0, done = 0;
        uint elemCount = n.ApiGetElemQuantity(h);

        foreach (var (id, expected) in targets.OrderBy(kv => kv.Key))
        {
            if (++done % CancelStride == 0) { ct.ThrowIfCancellationRequested(); progress?.Report((double)done / targets.Count); }
            var actual = Classify(s, id, elemCount);
            if (actual == null) { missing++; continue; }
            if (actual != expected) { wrongKind++; continue; }

            if (kind == ScadForceReadKind.Rsu)
            {
                if (ReadRsu(s, id, actual.Value) is { } r) rsu.Add(r);
                else noResult++;
            }
            else if (ReadForces(s, id, actual.Value, kind) is { } f) forces.Add(f);
            else noResult++;
        }
        ct.ThrowIfCancellationRequested();
        progress?.Report(1);

        if (targets.Count > 0 && 2 * (missing + wrongKind) > targets.Count)
            throw new ScadApiException("ScadApiForcesOtherProject", [missing + wrongKind, targets.Count]);
        return new ScadApiForceReadResult(forces, rsu, catalog, missing, wrongKind, noResult);
    }

    /// <summary>Вид КЭ в проекте SCAD; null — нет такого КЭ или удалён.</summary>
    static ScadElementKind? Classify(ScadApiSession s, int id, uint elemCount)
    {
        var n = s.Native;
        nint h = s.Handle;
        if (id < 1 || id > elemCount || n.ApiIsElemDeleted(h, (uint)id) != 0) return null;
        uint type, rigid, qn;
        uint* list;
        if (n.ApiElemGetData(h, (uint)id, &type, &rigid, &qn, &list) != 0) return null;
        return ScadElementKinds.Classify((int)type, (int)qn);
    }

    /// <summary>Усилия загружений или комбинаций КЭ (первый слой); null — результатов нет.</summary>
    static ScadElementForces? ReadForces(ScadApiSession s, int id, ScadElementKind kind, ScadForceReadKind what)
    {
        byte* p = null;
        byte mode = what == ScadForceReadKind.Combinations ? ReadAll : ReadNoComb;
        if (s.Native.ApiGetEffors(s.Handle, (uint)id, &p, mode) != 0 || p == null) return null;
        var e = ScadApiLayouts.ParseEffors(new ReadOnlySpan<byte>(p, ScadApiLayouts.EfforsSize));
        if (e.QuantityUs == 0 || e.Points == 0 || e.TypeUs == 0) return null;
        var types = new ReadOnlySpan<byte>((void*)e.TypeUs, e.QuantityUs).ToArray();
        int layers = Math.Max(e.Layers, 1);

        double[] loads = [], combs = [];
        int loadCount = 0, combCount = 0;
        if (what == ScadForceReadKind.LoadCases)
        {
            if (!Fits(e.DataUs, e.Us, e.Points, e.Loads, layers, e.QuantityUs)) return null;
            loads = ScadApiLayouts.FirstLayer(new ReadOnlySpan<double>((void*)e.Us, (int)e.DataUs),
                e.Points, e.Loads, layers, e.QuantityUs);
            loadCount = e.Loads;
        }
        else
        {
            if (!Fits(e.DataUsComb, e.UsComb, e.Points, e.Combinations, layers, e.QuantityUs)) return null;
            combs = ScadApiLayouts.FirstLayer(new ReadOnlySpan<double>((void*)e.UsComb, (int)e.DataUsComb),
                e.Points, e.Combinations, layers, e.QuantityUs);
            combCount = e.Combinations;
        }
        return new ScadElementForces(id, kind, types, e.Points, loads, loadCount, combs, combCount);
    }

    static bool Fits(long data, nint ptr, int points, int rows, int layers, int q) =>
        rows > 0 && ptr != 0 && data >= (long)points * rows * layers * q && data <= int.MaxValue;

    /// <summary>РСУ КЭ; коды усилий — из ApiGetEffors того же КЭ (сверено 01.10). null — РСУ нет.</summary>
    static ScadRsuElement? ReadRsu(ScadApiSession s, int id, ScadElementKind kind)
    {
        var n = s.Native;
        nint h = s.Handle;
        byte* pe = null;
        if (n.ApiGetEffors(h, (uint)id, &pe, ReadNoComb) != 0 || pe == null) return null;
        var e = ScadApiLayouts.ParseEffors(new ReadOnlySpan<byte>(pe, ScadApiLayouts.EfforsSize));
        if (e.QuantityUs == 0 || e.TypeUs == 0) return null;
        var types = new ReadOnlySpan<byte>((void*)e.TypeUs, e.QuantityUs).ToArray();

        Span<byte> buf = stackalloc byte[64];
        buf.Clear();
        fixed (byte* b = buf)
            if (n.ApiGetRsu(h, (uint)id, b) != 0) return null;
        var r = ScadApiLayouts.ParseRsu(buf);
        if (r.Rows == 0 || r.Str == 0 || r.QuantityUs == 0) return null;
        int q = Math.Min(r.QuantityUs, types.Length);

        var rows = new List<ScadRsuRow>(r.Rows);
        for (int i = 0; i < r.Rows; i++)
        {
            var row = ScadApiLayouts.ParseRsuRow(new ReadOnlySpan<byte>((byte*)r.Str + i * ScadApiLayouts.RsuRowSize,
                ScadApiLayouts.RsuRowSize));
            if (row.Us == 0) continue;
            var src = new ReadOnlySpan<float>((void*)row.Us, q);
            var us = new double[q];
            for (int k = 0; k < q; k++) us[k] = src[k];
            rows.Add(new ScadRsuRow(row.Point, row.Group, row.Criterion, us));
        }
        return rows.Count > 0 ? new ScadRsuElement(id, kind, types[..q], rows) : null;
    }

    /// <summary>Имена загружений (ApiGetLoadName) и, для комбинаций, имена комбинаций (ApiGetResultData).</summary>
    static ScadResultCatalog ReadCatalog(ScadApiSession s, ScadForceReadKind kind)
    {
        var n = s.Native;
        nint h = s.Handle;
        var loads = new List<string>();
        uint lc = n.ApiGetQuantityLoad(h);
        for (uint i = 1; i <= lc; i++)
            loads.Add(ScadApiSession.Str(n.ApiGetLoadName(h, i)));

        var combs = new List<string>();
        if (kind == ScadForceReadKind.Combinations)
        {
            uint cc = n.ApiGetQuantityComb(h);
            Span<byte> buf = stackalloc byte[ScadApiLayouts.LoadingDataSize];
            for (uint i = 1; i <= cc; i++)
            {
                buf.Clear();
                string name = "";
                fixed (byte* b = buf)
                    if (n.ApiGetResultData(h, ScadApiLayouts.ResultLoadComb, i, 1, b) == 0)
                        name = ScadApiSession.Str(*(byte**)(b + ScadApiLayouts.LoadingDataName));
                combs.Add(name);
            }
        }
        return new ScadResultCatalog(loads, combs);
    }
}
