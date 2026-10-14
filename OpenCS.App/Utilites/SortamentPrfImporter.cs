using System.Globalization;
using System.IO;
using CScore.Import;
using CScore.ParametricSteel;
using CScore.Sp16;
using CSfea.Torsion;
using Microsoft.Data.Sqlite;
using OpenCS.Services;

namespace OpenCS.Utilites;

/// <summary>
/// Пополнение сортамента OpenCS (<c>Sortamenty.db3</c>) таблицами сортамента SCAD (PRF), которых в нём нет:
/// двутавры ТУ и ГОСТ Р 57837 (доп. серии, изм. 1), гнутые замкнутые профили ГОСТ 30245-2012, 32931, 12336,
/// 25577, 8639, 8645, трубы ГОСТ 32931, Р 58064 и др. Подтипы помечаются <c>Source = 'SCAD PRF'</c>; повторный
/// запуск заменяет их. Вид, изготовление и уклон полок — в колонках <c>Kind</c>, <c>Fabrication</c>, <c>Slope</c>
/// таблицы <c>ShapeSubTypes</c> (их читает <see cref="ProfileDB"/>). It двутавров и швеллеров — МКЭ (как
/// <see cref="SteelTorsionConstantService"/>), замкнутых профилей — по Бредту, труб — точно. Строки, у которых
/// площадь контура расходится с сортаментом SCAD больше чем на 5 % (опечатки сортамента), не переносятся.
/// </summary>
public static class SortamentPrfImporter
{
    /// <summary>Метка подтипов, перенесённых из PRF.</summary>
    public const string SourceTag = "SCAD PRF";

    /// <summary>Таблица PRF → группа OpenCS. Код таблицы, база.</summary>
    public static readonly IReadOnlyList<(string Base, string Code)> Tables =
    [
        ("ASCHM", "d4"), ("ASCHM", "d5"),
        ("RUSSIAN", "p_add_d"), ("RUSSIAN", "P_19425"),
        ("RUSSIAN", "DTV016SH"), ("RUSSIAN", "DTV036SH"), ("RUSSIAN", "DTVR016K"), ("RUSSIAN", "DTVR016N"),
        ("RUSSIAN", "DTVR016S"), ("RUSSIAN", "DTVR016U"), ("RUSSIAN", "DTVR036K"), ("RUSSIAN", "DTVR036N"),
        ("RUSSIAN", "DTVR036S"),
        ("RUSSIAN", "TU016_U"), ("RUSSIAN", "TU016_N"), ("RUSSIAN", "TU016_Sr"), ("RUSSIAN", "TU016_Sh"),
        ("RUSSIAN", "TU016_K"), ("RUSSIAN", "TU036_N"), ("RUSSIAN", "TU036_K"), ("RUSSIAN", "TU036_Sh"),
        ("RUSSIAN", "TU036_Sv"),
        ("RUSSIAN", "G57837DB"), ("RUSSIAN", "G57837DK"),
        ("RUSSIAN", "C57837DB"), ("RUSSIAN", "C57837DK"), ("RUSSIAN", "C57837Sh"),
        ("RUSSIAN", "PU_19425"),
        ("RUSSIAN", "C_32931"), ("RUSSIAN", "C_58064"),
        ("RUSSIAN", "okv2012"), ("RUSSIAN", "orct2012"), ("RUSSIAN", "S_32931"), ("RUSSIAN", "R_32931"),
        ("RUSSIAN", "OKV30245"), ("RUSSIAN", "REC30245"), ("RUSSIAN", "OKV66"), ("RUSSIAN", "ORECT66"),
        ("RUSSIAN", "OKV77"), ("RUSSIAN", "ORECT77"), ("RUSSIAN", "kv_truba"), ("RUSSIAN", "pr_truba"),
        ("RUSSIAN", "kv82_tru"), ("RUSSIAN", "OKV"), ("RUSSIAN", "ORECT"),
    ];

    /// <summary>Результат: подтипы и строки, пропущенные строки с причиной.</summary>
    public sealed record Report(List<string> Subtypes, int Rows, List<string> Skipped);

    /// <summary>Пополняет базу сортамента таблицами <see cref="Tables"/> из PRF каталога <paramref name="prfDirectory"/>.</summary>
    public static Report Import(string dbPath, string prfDirectory, Action<string>? progress = null)
    {
        var files = new Dictionary<string, ScadPrfFile>(StringComparer.OrdinalIgnoreCase);
        ScadPrfFile File(string name) =>
            files.TryGetValue(name, out var f) ? f : files[name] = ScadPrfReader.Read(Path.Combine(prfDirectory, name + ".PRF"));

        var report = new Report([], 0, []);
        int rows = 0;
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        EnsureColumns(conn);
        using var tx = conn.BeginTransaction();
        RemovePrevious(conn, tx);

        int nextId = Scalar(conn, tx, "SELECT COALESCE(MAX(ID), 0) FROM ShapeSubTypes") + 1;
        foreach (var (baseName, code) in Tables)
        {
            var table = File(baseName).Table(code);
            if (table == null) { report.Skipped.Add($"{baseName} {code}: таблицы нет"); continue; }
            string? group = table.Kind switch { 1 => "Двутавры", 3 => "Швеллеры", 16 => "Трубы", 19 or 20 => "Прямоугольные трубы", _ => null };
            if (group == null) { report.Skipped.Add($"{code}: вид {table.Kind} не переносится"); continue; }

            int subtypeId = nextId++;
            string subtype = table.Title.Replace('p', 'р');   // латинская «p» в названиях SCAD
            var entries = new List<(ImportedSteelShape Shape, ScadPrfRow Row)>();
            for (int i = 1; i <= table.Rows.Count; i++)
            {
                var (shape, reason) = ScadSteelProfiles.Resolve(File(baseName), code, i);
                if (shape == null) { report.Skipped.Add($"{code} {table.Rows[i - 1].Name}: {reason}"); continue; }
                double? area = ContourAreaCm2(shape);
                double? reference = table.Value(table.Rows[i - 1], "A");
                if (area is not double a) { report.Skipped.Add($"{code} {shape.Name}: контур не строится"); continue; }
                if (reference is double r && r > 0 && Math.Abs(a - r) > 0.05 * r)
                {
                    report.Skipped.Add($"{code} {shape.Name}: A {a:0.##} против {r:0.##} см² в сортаменте SCAD");
                    continue;
                }
                entries.Add((shape, table.Rows[i - 1]));
            }
            if (entries.Count == 0) { nextId--; continue; }

            var first = entries[0].Shape;
            Exec(conn, tx, "INSERT INTO ShapeSubTypes (ID, SubType, Type, TypeID, Kind, Fabrication, Slope, Source) " +
                           "VALUES (@id, @st, @t, (SELECT ID FROM ShapeTypes WHERE Type = @t), @k, @f, @s, @src)",
                ("@id", subtypeId), ("@st", subtype), ("@t", group), ("@k", first.Kind.ToString()),
                ("@f", first.Fabrication.ToString()), ("@s", first.FlangeSlope), ("@src", SourceTag));
            foreach (var (shape, row) in entries)
            {
                InsertRow(conn, tx, group, subtypeId, subtype, table, row, shape);
                rows++;
            }
            report.Subtypes.Add($"{subtypeId}: {subtype} ({entries.Count})");
            progress?.Invoke($"{code}: {entries.Count} из {table.Rows.Count}");
        }
        tx.Commit();
        return report with { Rows = rows };
    }

    static void EnsureColumns(SqliteConnection conn)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(ShapeSubTypes)";
            using var r = cmd.ExecuteReader();
            while (r.Read()) columns.Add(r.GetString(1));
        }
        foreach (var (name, type) in new[] { ("Kind", "TEXT"), ("Fabrication", "TEXT"), ("Slope", "REAL"), ("Source", "TEXT") })
            if (!columns.Contains(name))
                Exec(conn, null, $"ALTER TABLE ShapeSubTypes ADD COLUMN {name} {type}");
    }

    static void RemovePrevious(SqliteConnection conn, SqliteTransaction tx)
    {
        foreach (string table in new[] { "Двутавры", "Швеллеры", "Трубы", "Прямоугольные трубы" })
            Exec(conn, tx, $@"DELETE FROM ""{table}"" WHERE SubTypeID IN (SELECT ID FROM ShapeSubTypes WHERE Source = @src)",
                ("@src", SourceTag));
        Exec(conn, tx, "DELETE FROM ShapeSubTypes WHERE Source = @src", ("@src", SourceTag));
    }

    static void InsertRow(SqliteConnection conn, SqliteTransaction tx, string group, int subtypeId, string subtype,
        ScadPrfTable table, ScadPrfRow row, ImportedSteelShape s)
    {
        double? V(string name) => table.Value(row, name);
        const double mm = 1000;
        double a = V("A") ?? ContourAreaCm2(s) ?? 0;
        double? ix = V("Iy"), iy = V("Iz");
        double? Radius(double? i) => i is double v && a > 0 ? Math.Sqrt(v / a) : null;
        double it = TorsionCm4(s);
        var values = new List<(string, object?)>
        {
            ("Name", row.Name), ("A", a), ("P", V("P")), ("J", it),
            ("SubTypeID", subtypeId), ("SubType", subtype), ("ГОСТ", table.Standard),
        };
        switch (group)
        {
            case "Двутавры":
            case "Швеллеры":
                values.AddRange([
                    ("H", s.H * mm), ("B", s.B * mm), ("tw", s.Tw * mm), ("tf", s.Tf * mm), ("R1", s.R1 * mm), ("r2", s.R2 * mm),
                    ("Ix", ix), ("Iy", iy), ("i_x", V("iy") ?? Radius(ix)), ("i_y", V("iz") ?? Radius(iy)),
                    ("Wx", V("Wy")), ("Wy", V("Wz")), ("S", V("Sy")), ("i", s.FlangeSlope * 100),
                ]);
                if (group == "Швеллеры") values.Add(("X0", V("yo") ?? 0));
                break;
            case "Трубы":
            {
                double d = s.H * mm, t = s.Tw * mm;
                double i = V("Iy") ?? Math.PI / 64 * (Math.Pow(d, 4) - Math.Pow(d - 2 * t, 4)) / 1e4;
                values.AddRange([
                    ("H", d), ("t", t), ("I", i), ("i_", Radius(i)), ("W", V("Wy") ?? i / (d / 20)),
                ]);
                break;
            }
            default:
                values.AddRange([
                    ("H", s.H * mm), ("B", s.B * mm), ("tw", s.Tw * mm), ("tf", s.Tf * mm), ("r", s.R1 * mm),
                    ("Ix", ix), ("Iy", iy), ("i_x", V("iy") ?? Radius(ix)), ("i_y", V("iz") ?? Radius(iy)),
                    ("Wx", V("Wy")), ("Wy", V("Wz")),
                ]);
                break;
        }
        string columns = string.Join(", ", values.Select(v => $"\"{v.Item1}\""));
        string parameters = string.Join(", ", values.Select((_, k) => "@p" + k));
        Exec(conn, tx, $@"INSERT INTO ""{group}"" ({columns}) VALUES ({parameters})",
            [.. values.Select((v, k) => ("@p" + k, v.Item2))]);
    }

    static ParametricSteelSectionDefinition Definition(ImportedSteelShape s) => new()
    {
        Kind = s.Kind, Fabrication = s.Fabrication, H = s.H, Bf1 = s.B, Tf1 = s.Tf, Tw = s.Tw,
        R1 = s.R1, R2 = s.R2, FlangeSlope = s.FlangeSlope,
    };

    /// <summary>Площадь контура профиля, см²; null — контур не строится.</summary>
    static double? ContourAreaCm2(ImportedSteelShape s)
    {
        if (ParametricSteelSectionGenerator.BuildCanonicalContour(Definition(s), 8) is not var (outer, holes)) return null;
        return (Math.Abs(Area(outer)) - holes.Sum(h => Math.Abs(Area(h)))) * 1e4;
    }

    /// <summary>
    /// It, см⁴: двутавр и швеллер — МКЭ по контуру со скруглениями; труба — π(D⁴ − d⁴)/32; замкнутый профиль —
    /// формула Бредта по средней линии 4·Am²·t / s с учётом скруглений.
    /// </summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<ImportedSteelShape, double> TorsionCache = new();

    static double TorsionCm4(ImportedSteelShape s) =>
        TorsionCache.GetOrAdd(s with { Name = "", Standard = "", ACm2 = null, IyCm4 = null, IzCm4 = null }, ComputeTorsionCm4);

    static double ComputeTorsionCm4(ImportedSteelShape s)
    {
        switch (s.Kind)
        {
            case SteelProfileKind.Pipe:
            {
                double d = s.H, di = s.H - 2 * s.Tw;
                return Math.PI / 32 * (Math.Pow(d, 4) - Math.Pow(di, 4)) * 1e8;
            }
            case SteelProfileKind.Box:
            {
                double t = s.Tw, rm = s.R1 + t / 2;
                double bm = s.B - t, hm = s.H - t;
                double am = bm * hm - (4 - Math.PI) * rm * rm;
                double perimeter = 2 * (bm + hm) - (8 - 2 * Math.PI) * rm;
                return 4 * am * am * t / perimeter * 1e8;
            }
            default:
            {
                if (ParametricSteelSectionGenerator.BuildCanonicalContour(Definition(s),
                        SteelTorsionConstantService.ArcSegmentsPerQuarter) is not var (outer, holes)) return 0;
                var (ox, oy) = Oriented(outer, ccw: true);
                var boundary = new TorsionBoundary(ox, oy, holes.Count > 0 ? [.. holes.Select(h => Oriented(h, ccw: false))] : null);
                double h0 = new[] { s.Tw, s.Tf }.Where(v => v > 0).DefaultIfEmpty(s.H / 30).Min();
                return SteelTorsionConstantService.Solve(boundary, h0).Value * 1e8;
            }
        }
    }

    static (double[] X, double[] Y) Oriented(IReadOnlyList<(double X, double Y)> pts, bool ccw)
    {
        var x = pts.Select(p => p.X).ToArray();
        var y = pts.Select(p => p.Y).ToArray();
        if (Area(pts) > 0 != ccw) { Array.Reverse(x); Array.Reverse(y); }
        return (x, y);
    }

    static double Area(IReadOnlyList<(double X, double Y)> ring)
    {
        double a = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            a += ring[j].X * ring[i].Y - ring[i].X * ring[j].Y;
        return a / 2;
    }

    static int Scalar(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    static void Exec(SqliteConnection conn, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
