using System.Diagnostics;
using CScore;
using CScore.Fem;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная оценка доли длительной части (<see cref="FemLongTermShare"/>) на наборах усилий базы проекта, читаемых напрямую
/// (только чтение): OPENCS_LONG_SHARE_DB — путь к базе (без переменной тест сразу выходит). Для каждой пары наборов
/// «… (N)» — «… (NL)» и «… (C)» — «… (CL)»: число строк и КЭ в обоих наборах, число КЭ с разным числом строк, доля.
/// Метрика — при толщине пластин OPENCS_LONG_SHARE_H (по умолчанию 0,2 м) и радиусах инерции стержней
/// OPENCS_LONG_SHARE_I (по умолчанию 0,115 м — квадрат 0,4 м).
/// </summary>
public sealed class FemLongTermShareManualTests(ITestOutputHelper output)
{
    [Fact]
    public void ShareByPairedSets()
    {
        var path = Environment.GetEnvironmentVariable("OPENCS_LONG_SHARE_DB");
        if (string.IsNullOrWhiteSpace(path)) return;
        double h = Number("OPENCS_LONG_SHARE_H", 0.2), radius = Number("OPENCS_LONG_SHARE_I", 0.115);

        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        con.Open();
        var sets = new List<ForceSet>();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "select id, tag, kind from force_sets";
            using var r = cmd.ExecuteReader();
            while (r.Read()) sets.Add(new ForceSet { Id = r.GetInt32(0), Tag = r.GetString(1), Kind = r.GetString(2) });
        }
        foreach (var total in sets)
        {
            if (FemEtaPsi.LongTermSet(total, sets.Where(s => s.Kind == total.Kind)) is not { } lng) continue;
            var clock = Stopwatch.StartNew();
            FemLongTermShareResult? result;
            string stats;
            if (total.Kind == "shell")
            {
                var (a, b) = (Shells(con, total.Id), Shells(con, lng.Id));
                stats = Stats(a.Select(x => (x.SourceElementNum, x.SourceSectionNum)).ToList(), b.Select(x => (x.SourceElementNum, x.SourceSectionNum)).ToList());
                result = FemLongTermShare.Shells([(a, b)], _ => h, defaultThickness: h);
            }
            else
            {
                var (a, b) = (Bars(con, total.Id), Bars(con, lng.Id));
                stats = Stats(a.Select(x => (x.SourceElementNum, x.SourceSectionNum)).ToList(), b.Select(x => (x.SourceElementNum, x.SourceSectionNum)).ToList());
                result = FemLongTermShare.Bars([(a, b)], _ => (radius, radius), defaultRadii: (radius, radius));
            }
            output.WriteLine($"«{total.Tag}» — «{lng.Tag}» ({total.Kind}): {stats}");
            output.WriteLine(result == null ? "  доля не определена"
                : $"  доля {result.Share:0.000}; групп {result.Groups}, строк {result.Rows}, без длительных строк {result.GroupsWithoutLong}, " +
                  $"пропущено {result.GroupsSkipped}; по группам {result.MinGroupShare:0.000}…{result.MaxGroupShare:0.000}; {clock.Elapsed.TotalSeconds:0.00} с");
        }
    }

    static double Number(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;

    static string Stats(List<(int?, int?)> total, List<(int?, int?)> lng)
    {
        var a = total.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        var b = lng.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        int differ = a.Count(kv => b.TryGetValue(kv.Key, out int n) && n != kv.Value);
        return $"строк {total.Count} / {lng.Count}, групп «КЭ — сечение» {a.Count} / {b.Count}, с разным числом строк {differ}, " +
               $"только в полном {a.Keys.Count(k => !b.ContainsKey(k))}";
    }

    static List<ShellLoadItem> Shells(SqliteConnection con, int setId)
    {
        var rows = new List<ShellLoadItem>();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "select label, nx, ny, nxy, mx, my, mxy, source_elem_num, source_section_num from force_shell_items where set_id = $id";
        cmd.Parameters.AddWithValue("$id", setId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new ShellLoadItem
            {
                Label = r.IsDBNull(0) ? "" : r.GetString(0), Nx = r.GetDouble(1), Ny = r.GetDouble(2), Nxy = r.GetDouble(3),
                Mx = r.GetDouble(4), My = r.GetDouble(5), Mxy = r.GetDouble(6),
                SourceElementNum = r.IsDBNull(7) ? null : r.GetInt32(7), SourceSectionNum = r.IsDBNull(8) ? null : r.GetInt32(8),
            });
        return rows;
    }

    static List<LoadItem> Bars(SqliteConnection con, int setId)
    {
        var rows = new List<LoadItem>();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "select label, n, mx, my, source_elem_num, source_section_num from force_items where set_id = $id";
        cmd.Parameters.AddWithValue("$id", setId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new LoadItem
            {
                Label = r.IsDBNull(0) ? "" : r.GetString(0), N = r.GetDouble(1), Mx = r.GetDouble(2), My = r.GetDouble(3),
                SourceElementNum = r.IsDBNull(4) ? null : r.GetInt32(4), SourceSectionNum = r.IsDBNull(5) ? null : r.GetInt32(5),
            });
        return rows;
    }
}
