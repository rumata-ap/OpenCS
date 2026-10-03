using CScore.Sp16;

namespace CScore.Import;

/// <summary>
/// Стальной профиль по строке сортамента SCAD (PRF): вид таблицы и колонки размеров → <see cref="ImportedSteelShape"/>.
/// Соответствие видов таблиц и колонок — спека <c>2026-10-02-imported-steel-sections-design.md</c>, §9.
/// </summary>
public static class ScadSteelProfiles
{
    /// <summary>Профиль строки <paramref name="rowNumber"/> (с 1) таблицы <paramref name="tableCode"/> либо причина.</summary>
    public static (ImportedSteelShape? Shape, string? Reason) Resolve(ScadPrfFile file, string tableCode, int rowNumber)
    {
        var found = file.Find(tableCode, rowNumber);
        if (found is not var (table, row))
            return (null, file.Table(tableCode) == null
                ? $"таблицы «{tableCode}» нет в сортаменте SCAD «{file.Title}»"
                : $"в таблице «{tableCode}» сортамента SCAD нет профиля № {rowNumber}");

        // Зеркальные уголки (виды 17, 18): только имена строк, размеры — в предыдущей таблице файла.
        if (table.Kind is 17 or 18)
        {
            var source = file.Previous(table);
            if (source is not { Kind: 4 or 5 } || source.Rows.Count != table.Rows.Count)
                return (null, $"таблица сортамента SCAD «{table.Title}» не поддерживается");
            var (mirrored, reason) = FromRow(source, source.Rows[rowNumber - 1]);
            return mirrored == null ? (null, reason) : (mirrored with { Name = row.Name, Flipped = true }, null);
        }
        return FromRow(table, row);
    }

    /// <summary>Профиль по строке таблицы либо причина («таблица … не поддерживается»).</summary>
    public static (ImportedSteelShape? Shape, string? Reason) FromRow(ScadPrfTable table, ScadPrfRow row)
    {
        double V(string name) => table.ValueSi(row, name) ?? 0;
        double? Ref(string name) => table.ColumnIndex(name) >= 0 && table.Columns[table.ColumnIndex(name)].Unit == "cm4"
            ? table.Value(row, name) : null;
        bool bent = table.ColumnIndex("n1") >= 0 || table.ColumnIndex("n") >= 0;
        string unsupported = $"таблица сортамента SCAD «{table.Title}» не поддерживается";

        ImportedSteelShape Make(SteelProfileKind kind, SteelFabrication fabrication,
            double h, double b, double tw, double tf, double r1, double r2 = 0, double slope = 0) =>
            new(kind, fabrication, h, b, tw, tf, r1, r2, slope, table.Standard, row.Name,
                table.Value(row, "A"), Ref("Iy"), Ref("Iz"));

        ImportedSteelShape? shape = table.Kind switch
        {
            1 => Make(SteelProfileKind.IBeam, SteelFabrication.Rolled,
                V("h"), V("b"), V("s"), V("t"), V("r1"), V("r2"), Slope(table, row)),
            // Тавр в ядре — только сварной: прокатный (разрезанный двутавр) строится без скругления у стенки.
            2 => Make(SteelProfileKind.Tee, SteelFabrication.Welded, V("h"), V("b"), V("s"), V("t"), 0),
            3 when bent => Make(SteelProfileKind.Channel, SteelFabrication.Bent,
                V("h"), V("b"), V("s"), V("s"), V("r1")),
            3 => Make(SteelProfileKind.Channel, SteelFabrication.Rolled,
                V("h"), V("b"), V("s"), V("t"), V("r1"), V("r2"), Slope(table, row)),
            4 when bent => Make(SteelProfileKind.Angle, SteelFabrication.Bent, V("b"), V("b"), V("t"), V("t"), V("r1")),
            4 => Make(SteelProfileKind.Angle, SteelFabrication.Rolled, V("b"), V("b"), V("t"), V("t"), V("r1"), V("r2")),
            5 when bent => Make(SteelProfileKind.Angle, SteelFabrication.Bent, V("h"), V("b"), V("t"), V("t"), V("r1")),
            5 => Make(SteelProfileKind.Angle, SteelFabrication.Rolled, V("h"), V("b"), V("t"), V("t"), V("r1"), V("r2")),
            13 => Make(SteelProfileKind.Rect, SteelFabrication.Rolled, V("b"), V("b"), 0, 0, 0),
            15 => Make(SteelProfileKind.Rect, SteelFabrication.Rolled, V("s"), V("b"), 0, 0, 0),
            16 => Make(SteelProfileKind.Pipe,
                table.Standard.Contains("8732") ? SteelFabrication.Rolled : SteelFabrication.Welded,
                V("D"), 0, V("s"), 0, 0),
            19 => Make(SteelProfileKind.Box, SteelFabrication.Bent, V("b"), V("b"), V("s"), V("s"), InnerRadius(table, row)),
            20 => Make(SteelProfileKind.Box, SteelFabrication.Bent, V("h"), V("b"), V("s"), V("s"), InnerRadius(table, row)),
            25 => Make(SteelProfileKind.Round, SteelFabrication.Rolled, V("d"), 0, 0, 0, 0),
            _ => null,
        };
        if (shape == null) return (null, unsupported);
        if (!(shape.H > 0)) return (null, $"профиль «{row.Name}» сортамента SCAD: размеры не заданы");
        return (shape, null);
    }

    /// <summary>
    /// Уклон внутренних граней полок: колонка Gamma (доля, либо проценты при значении ≥ 1); ГОСТ 8239 без колонки —
    /// 12 %, как в <c>ProfileDB.SteelSubtypes</c>.
    /// </summary>
    static double Slope(ScadPrfTable table, ScadPrfRow row)
    {
        if (table.Value(row, "Gamma") is double g && g > 0) return g >= 1 ? g / 100 : g;
        return table.Standard.Contains("8239") ? 0.12 : 0;
    }

    /// <summary>
    /// Внутренний радиус гиба замкнутого профиля: R/r1 сортамента — наружный (ГОСТ 30245, 32931, Р 54157, 12336,
    /// 25577: R ≈ 2s), внутренний = R − s; без колонки радиуса — s (наружный 2s).
    /// </summary>
    static double InnerRadius(ScadPrfTable table, ScadPrfRow row)
    {
        double s = table.ValueSi(row, "s") ?? 0;
        double outer = table.ValueSi(row, "R") ?? table.ValueSi(row, "r1") ?? 2 * s;
        return Math.Max(0, outer - s);
    }
}
