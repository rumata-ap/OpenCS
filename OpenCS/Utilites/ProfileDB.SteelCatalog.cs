using CScore.ParametricSteel;
using CScore.Sp16;

namespace OpenCS.Utilites;

/// <summary>Подтип сортамента (стандарт) для параметрического МК-сечения.</summary>
public sealed record SteelCatalogSubtype(int Id, string Group, string Name)
{
    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// Строка сортамента в списке выбора со справочными характеристиками для сортировки (оси сортамента:
/// x — ось наибольшей жёсткости): h (мм), A (см²), Ix, Iy (см⁴), Wx, Wy (см³); null — нет в таблице.
/// </summary>
public sealed record SteelCatalogProfileItem(int Id, string Name,
    double? HMm = null, double? ACm2 = null, double? IxCm4 = null, double? IyCm4 = null,
    double? WxCm3 = null, double? WyCm3 = null)
{
    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// Размеры профиля из сортамента в едином виде (м), без привязки к классам <c>*Profile</c>.
/// Справочные A (см²), Ix, Iy (см⁴) — для сравнения со сформированным контуром; It (см⁴) — момент
/// инерции при свободном кручении, передаётся в расчёт по СП 16.
/// </summary>
public sealed record SteelCatalogEntry(
    string Group, string Standard, int SubTypeId, int Id, string Name,
    SteelProfileKind Kind, SteelFabrication Fabrication,
    double H, double B, double Tw, double Tf, double R1, double R2, double FlangeSlope,
    double? ACm2, double? IxCm4, double? IyCm4, double? ItCm4 = null)
{
    /// <summary>Ссылка на строку сортамента для исходного описания (It — в м⁴).</summary>
    public ParametricSteelCatalogRef ToCatalogRef() => new(Group, Standard, Name, ItCm4 is > 0 ? ItCm4.Value * 1e-8 : 0);

    /// <summary>Исходное описание сечения с размерами строки сортамента (материал и метка — по умолчанию).</summary>
    public ParametricSteelSectionDefinition ToDefinition() => new()
    {
        Kind = Kind, Fabrication = Fabrication,
        H = H, Bf1 = B, Tf1 = Tf, Tw = Tw, R1 = R1, R2 = R2, FlangeSlope = FlangeSlope,
        Catalog = ToCatalogRef(),
    };
}

/// <summary>Источник сортамента для диалога параметрического МК-сечения (подменяется в тестах).</summary>
public interface ISteelSortament
{
    /// <summary>Подтипы сортамента, подходящие к виду и способу изготовления.</summary>
    IReadOnlyList<SteelCatalogSubtype> GetSteelCatalogSubtypes(SteelProfileKind kind, SteelFabrication fabrication);
    /// <summary>Профили подтипа.</summary>
    IReadOnlyList<SteelCatalogProfileItem> GetSteelCatalogProfiles(int subtypeId);
    /// <summary>Размеры профиля; null — строка не найдена.</summary>
    SteelCatalogEntry? GetSteelCatalogEntry(int subtypeId, int profileId);
}

public partial class ProfileDB : ISteelSortament
{
    /// <summary>
    /// Соответствие подтипов <c>ShapeSubTypes</c> видам и изготовлению ядра СП 16. Уклон внутренних граней
    /// полок — не колонка <c>i</c> базы: справочные A, I ГОСТ 8239 соответствуют 12 %, ГОСТ 8240 «У» — 10 %.
    /// </summary>
    static readonly Dictionary<int, (SteelProfileKind Kind, SteelFabrication Fabrication, double Slope)> SteelSubtypes = new()
    {
        [1] = (SteelProfileKind.Angle, SteelFabrication.Rolled, 0),
        [2] = (SteelProfileKind.Angle, SteelFabrication.Rolled, 0),
        [3] = (SteelProfileKind.Angle, SteelFabrication.Bent, 0),
        [4] = (SteelProfileKind.Angle, SteelFabrication.Bent, 0),
        [5] = (SteelProfileKind.Angle, SteelFabrication.Bent, 0),
        [6] = (SteelProfileKind.Angle, SteelFabrication.Bent, 0),
        [11] = (SteelProfileKind.IBeam, SteelFabrication.Rolled, 0),
        [12] = (SteelProfileKind.IBeam, SteelFabrication.Rolled, 0),
        [13] = (SteelProfileKind.IBeam, SteelFabrication.Rolled, 0),
        [14] = (SteelProfileKind.IBeam, SteelFabrication.Rolled, 0),
        [15] = (SteelProfileKind.IBeam, SteelFabrication.Rolled, 0),
        [16] = (SteelProfileKind.IBeam, SteelFabrication.Rolled, 0),
        [17] = (SteelProfileKind.IBeam, SteelFabrication.Rolled, 0.12),
        [18] = (SteelProfileKind.IBeam, SteelFabrication.Rolled, 0),
        [19] = (SteelProfileKind.IBeam, SteelFabrication.Rolled, 0),
        [20] = (SteelProfileKind.IBeam, SteelFabrication.Rolled, 0),
        [21] = (SteelProfileKind.IBeam, SteelFabrication.Rolled, 0),
        [24] = (SteelProfileKind.Channel, SteelFabrication.Rolled, 0),
        [25] = (SteelProfileKind.Channel, SteelFabrication.Rolled, 0.10),
        [26] = (SteelProfileKind.Channel, SteelFabrication.Rolled, 0),
        [27] = (SteelProfileKind.Channel, SteelFabrication.Rolled, 0),
        [28] = (SteelProfileKind.Channel, SteelFabrication.Bent, 0),
        [29] = (SteelProfileKind.Channel, SteelFabrication.Bent, 0),
        [35] = (SteelProfileKind.Box, SteelFabrication.Bent, 0),
        [36] = (SteelProfileKind.Box, SteelFabrication.Bent, 0),
        [37] = (SteelProfileKind.Box, SteelFabrication.Bent, 0),
        [38] = (SteelProfileKind.Box, SteelFabrication.Bent, 0),
        [44] = (SteelProfileKind.Pipe, SteelFabrication.Welded, 0),
        [45] = (SteelProfileKind.Pipe, SteelFabrication.Welded, 0),
        [46] = (SteelProfileKind.Pipe, SteelFabrication.Rolled, 0),
        [47] = (SteelProfileKind.Pipe, SteelFabrication.Welded, 0),
    };

    /// <inheritdoc/>
    public IReadOnlyList<SteelCatalogSubtype> GetSteelCatalogSubtypes(SteelProfileKind kind, SteelFabrication fabrication)
    {
        var ids = SteelSubtypes.Where(p => p.Value.Kind == kind && p.Value.Fabrication == fabrication)
            .Select(p => p.Key).ToHashSet();
        if (ids.Count == 0) return [];
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ID, SubType, Type FROM ShapeSubTypes WHERE SubType IS NOT NULL ORDER BY ID";
        using var reader = cmd.ExecuteReader();
        var result = new List<SteelCatalogSubtype>();
        while (reader.Read())
            if (ids.Contains(reader.GetInt32(0)))
                result.Add(new(reader.GetInt32(0), reader.GetString(2), reader.GetString(1)));
        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SteelCatalogProfileItem> GetSteelCatalogProfiles(int subtypeId)
    {
        string table = TableMap[TypeForSubtype(subtypeId)];
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT * FROM ""{table}"" WHERE SubTypeID = @sid";
        cmd.Parameters.AddWithValue("@sid", subtypeId);
        using var reader = cmd.ExecuteReader();
        var result = new List<SteelCatalogProfileItem>();
        // У труб один момент инерции I и сопротивления W на обе оси.
        while (reader.Read())
            result.Add(new(reader.GetInt32(reader.GetOrdinal("ID")), reader.GetString(reader.GetOrdinal("Name")),
                Optional(reader, "H"), Optional(reader, "A"),
                Optional(reader, "Ix") ?? Optional(reader, "I"), Optional(reader, "Iy") ?? Optional(reader, "I"),
                Optional(reader, "Wx") ?? Optional(reader, "W"), Optional(reader, "Wy") ?? Optional(reader, "W")));
        return result;
    }

    /// <summary>Число из колонки; null — колонки нет в таблице группы или значение пусто.</summary>
    static double? Optional(Microsoft.Data.Sqlite.SqliteDataReader reader, string column)
    {
        int i = Enumerable.Range(0, reader.FieldCount).FirstOrDefault(k => reader.GetName(k) == column, -1);
        if (i < 0 || reader.IsDBNull(i)) return null;
        return reader.GetDouble(i);
    }

    /// <inheritdoc/>
    public SteelCatalogEntry? GetSteelCatalogEntry(int subtypeId, int profileId)
    {
        if (!SteelSubtypes.TryGetValue(subtypeId, out var map)) return null;
        string group = TypeForSubtype(subtypeId);
        string standard = SubtypeName(subtypeId);
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT * FROM ""{TableMap[group]}"" WHERE ID = @id AND SubTypeID = @sid";
        cmd.Parameters.AddWithValue("@id", profileId);
        cmd.Parameters.AddWithValue("@sid", subtypeId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        const double mm = 0.001;
        double Get(string column)
        {
            int i = reader.GetOrdinal(column);
            return reader.IsDBNull(i) ? 0 : reader.GetDouble(i);
        }
        double? Opt(string column) => Optional(reader, column);
        string name = reader.GetString(reader.GetOrdinal("Name"));
        SteelCatalogEntry Entry(double h, double b, double tw, double tf, double r1, double r2,
            double? a, double? ix, double? iy) =>
            new(group, standard, subtypeId, profileId, name, map.Kind, map.Fabrication,
                h * mm, b * mm, tw * mm, tf * mm, r1 * mm, r2 * mm, map.Slope, a, ix, iy, Opt("J") ?? Opt("It"));

        return map.Kind switch
        {
            SteelProfileKind.IBeam or SteelProfileKind.Channel =>
                Entry(Get("H"), Get("B"), Get("tw"), Get("tf"), Get("R1"), Get("r2"), Opt("A"), Opt("Ix"), Opt("Iy")),
            SteelProfileKind.Angle =>
                Entry(Get("H"), Get("Bf"), Get("Tw"), Get("Tw"), Get("R"), Get("r_"), Opt("A"), Opt("Ix"), Opt("Iy")),
            // Радиус r прямоугольных труб — внутренний (наружный = r + t), как R1 гнутого короба.
            SteelProfileKind.Box =>
                Entry(Get("H"), Get("B"), Get("tw"), Get("tw"), Get("r"), 0, Opt("A"), Opt("Ix"), Opt("Iy")),
            SteelProfileKind.Pipe =>
                Entry(Get("H"), 0, Get("t"), 0, 0, 0, Opt("A"), Opt("I"), Opt("I")),
            _ => null,
        };
    }

    string SubtypeName(int subtypeId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT SubType FROM ShapeSubTypes WHERE ID = @id";
        cmd.Parameters.AddWithValue("@id", subtypeId);
        return cmd.ExecuteScalar() as string ?? "";
    }
}
