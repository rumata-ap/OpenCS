using System.Text.Json;
using CScore.Batch;

namespace OpenCS.ViewModels;

/// <summary>
/// Карта ширины раскрытия трещин по КЭ из результата пакета ПС2 слоистой пластины: по номеру КЭ строки набора —
/// наибольшие acrc и Кисп среди сошедшихся строк. Строки без номера КЭ (набор не из МКЭ-схемы) в карту не входят;
/// КЭ, у которого не сошлась ни одна строка, — отдельно (на мозаике — серым, а не «0 мм»).
/// </summary>
public sealed class ShellSlsBatchCrackMap
{
    /// <summary>Наибольшая acrc по КЭ, мм.</summary>
    public IReadOnlyDictionary<int, double> AcrcByElement { get; private init; } = new Dictionary<int, double>();

    /// <summary>Наибольший Кисп = acrc/acrc,ult по КЭ (для результатов без предела — нет).</summary>
    public IReadOnlyDictionary<int, double> UtilByElement { get; private init; } = new Dictionary<int, double>();

    /// <summary>КЭ, у которых не сошлась ни одна строка.</summary>
    public IReadOnlyList<int> NotConvergedElements { get; private init; } = [];

    /// <summary>КЭ, у которых часть строк не сошлась (значение — по сошедшимся).</summary>
    public IReadOnlyList<int> PartlyNotConvergedElements { get; private init; } = [];

    /// <summary>Предел acrc,ult пакета, мм; null — результат до 13.11.2026.</summary>
    public double? AcrcLimMm { get; private init; }

    /// <summary>В пакете есть строки с номером КЭ — карту можно показать на схеме.</summary>
    public bool HasElements => AcrcByElement.Count > 0 || NotConvergedElements.Count > 0;

    /// <summary>Разбирает DataJson пакета shell_layered_sls_batch.</summary>
    public static ShellSlsBatchCrackMap Build(string dataJson)
    {
        using var doc = JsonDocument.Parse(dataJson);
        var root = doc.RootElement;
        double? lim = root.TryGetProperty("acrc_lim_mm", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetDouble() : null;
        var acrc = new Dictionary<int, double>();
        var util = new Dictionary<int, double>();
        var failedRows = new HashSet<int>();
        if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            foreach (var r in rows.EnumerateArray())
            {
                if (!r.TryGetProperty("elem", out var e) || e.ValueKind != JsonValueKind.Number) continue;
                int elem = e.GetInt32();
                var v = BatchRowVerdictReader.Read("shell_layered_sls_batch", r);
                if (v.Status is BatchRowStatus.NotConverged or BatchRowStatus.Error) { failedRows.Add(elem); continue; }
                double a = r.TryGetProperty("acrc_mm", out var am) && am.ValueKind == JsonValueKind.Number ? am.GetDouble() : 0.0;
                acrc[elem] = Math.Max(acrc.GetValueOrDefault(elem, double.NegativeInfinity), a);
                // Строка без сохранённой причины — Кисп по пределу пакета.
                double? u = v.Util ?? (lim is > 1e-12 ? a / lim.Value : null);
                if (u is { } uu) util[elem] = Math.Max(util.GetValueOrDefault(elem, double.NegativeInfinity), uu);
            }
        return new ShellSlsBatchCrackMap
        {
            AcrcByElement = acrc,
            UtilByElement = util,
            NotConvergedElements = failedRows.Where(e => !acrc.ContainsKey(e)).Order().ToList(),
            PartlyNotConvergedElements = failedRows.Where(acrc.ContainsKey).Order().ToList(),
            AcrcLimMm = lim,
        };
    }
}
