using System.Globalization;
using CScore.Fem;
using CScore.Import;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная сверка импорта из текстового файла ЛИРЫ с импортом из API: OPENCS_LIRA_IMPORT_TXT (пути через «;») — сводка
/// конвертации каждого файла; с OPENCS_LIRA_IMPORT_DB (БД OpenCS, открывается только на чтение) и
/// OPENCS_LIRA_IMPORT_SCHEMA (тег схемы, импортированной из API) — сверка КЭ, жёсткостей (свойства КЭ по
/// <see cref="LiraElementStiffnessSource"/>), C1 и осей пластин. Без переменной — выход.
/// </summary>
public class LiraTextImportManualTests(ITestOutputHelper output)
{
    const double TonToKn = 9.80665;

    [Fact]
    public void ConvertAndCompareWithApi()
    {
        string? list = Environment.GetEnvironmentVariable("OPENCS_LIRA_IMPORT_TXT");
        if (string.IsNullOrWhiteSpace(list)) return;
        string? dbPath = Environment.GetEnvironmentVariable("OPENCS_LIRA_IMPORT_DB");
        string? schemaTag = Environment.GetEnvironmentVariable("OPENCS_LIRA_IMPORT_SCHEMA");

        foreach (string path in list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            output.WriteLine($"=== {Path.GetFileName(path)}");
            var txt = LiraTextReader.Read(path);
            string? aldPath = LiraAldFile.PathFor(path);
            var ald = aldPath != null ? LiraAldFile.Read(aldPath) : null;
            var report = new List<string>();
            var data = LiraTextSchemaConverter.ToSchemaData(txt, ald, TonToKn, report);
            output.WriteLine($"  .ald: {(aldPath != null ? "есть" : "нет")}; узлов {data.Nodes.Count}, КЭ {data.Elements.Count} " +
                $"(стержней {data.Elements.Count(e => e.NodeIds.Length == 2)}), жёсткостей {data.Stiffnesses.Count}, " +
                $"блоков {data.ConstructiveBlocks.Count}, C1 {data.PlateFoundationC1.Count} (C2 и др. {data.PlateFoundationBeyondC1}), " +
                $"осей пластин {data.PlateAxisAngles.Count}");
            foreach (var r in report) output.WriteLine("  " + r);
            foreach (var s in data.Stiffnesses.Take(12)) output.WriteLine($"  {s.Id} [{s.KindCode}] «{s.Name}»: {s.Params}");

            if (dbPath != null && schemaTag != null
                && string.Equals(Path.GetFileNameWithoutExtension(path), schemaTag, StringComparison.OrdinalIgnoreCase))
                CompareWithApi(data, dbPath, schemaTag);
        }
    }

    void CompareWithApi(LiraSchemaData data, string dbPath, string schemaTag)
    {
        using var c = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        c.Open();
        int schemaId = Scalar<long>(c, "SELECT id FROM fem_schemas WHERE tag=@t", ("@t", schemaTag)) is var id and > 0 ? (int)id
            : throw new InvalidOperationException($"Схемы «{schemaTag}» нет в БД.");

        var api = new Dictionary<int, LiraStiffnessRecord>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT num, kind_code, name, params, section_unit_m FROM fem_schema_stiffnesses WHERE schema_id=@s";
            cmd.Parameters.AddWithValue("@s", schemaId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) api[r.GetInt32(0)] = new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetDouble(4));
        }
        var apiElems = new Dictionary<string, (string Type, int? Stiff, double? C1, double? Angle)>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                SELECT elem_tag, elem_type, stiffness_num, foundation_c1, local_axis_angle_deg FROM fem_elements
                WHERE schema_id=@s AND origin='imported'
                """;
            cmd.Parameters.AddWithValue("@s", schemaId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                apiElems[r.GetString(0)] = (r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2),
                    r.IsDBNull(3) ? null : r.GetDouble(3), r.IsDBNull(4) ? null : r.GetDouble(4));
        }

        var txtElems = data.Elements.ToDictionary(e => e.Id.ToString(CultureInfo.InvariantCulture));
        int missing = apiElems.Keys.Count(k => !txtElems.ContainsKey(k)), extra = txtElems.Keys.Count(k => !apiElems.ContainsKey(k));
        int stiffDiff = apiElems.Count(kv => txtElems.TryGetValue(kv.Key, out var e) && kv.Value.Stiff is { } s && s != e.StiffnessId);
        output.WriteLine($"  API: КЭ {apiElems.Count}; нет в тексте {missing}, лишних в тексте {extra}, другой номер жёсткости {stiffDiff}");

        int c1Diff = 0, c1Count = 0, angleDiff = 0;
        foreach (var (tag, a) in apiElems)
        {
            int num = int.Parse(tag, CultureInfo.InvariantCulture);
            double tc1 = data.PlateFoundationC1.GetValueOrDefault(num);
            // В тексте — 6 значащих цифр.
            if (a.C1 is > 0 || tc1 > 0) { c1Count++; if (Math.Abs((a.C1 ?? 0) - tc1) > 1e-5 * Math.Max(1, a.C1 ?? tc1)) c1Diff++; }
            double ta = data.PlateAxisAngles.GetValueOrDefault(num);
            if (a.Type == "shell" && Math.Abs((a.Angle ?? 0) - ta) > 1e-4) angleDiff++;  // в тексте — 6 значащих цифр
        }
        output.WriteLine($"  C1: КЭ с основанием {c1Count}, расхождений {c1Diff}; оси пластин: расхождений {angleDiff}");

        var units = data.Units!;
        var srcApi = new LiraElementStiffnessSource(api, units);
        var srcTxt = new LiraElementStiffnessSource(data.Stiffnesses.ToDictionary(s => s.Id), units);
        foreach (int num in api.Keys.Union(data.Stiffnesses.Select(s => s.Id)).Order())
        {
            string type = apiElems.Values.FirstOrDefault(v => v.Stiff == num).Type ?? (txtElems.Values.FirstOrDefault(e => e.StiffnessId == num)?.NodeIds.Length == 2 ? "beam" : "shell");
            var probe = new FemElement { ElemTag = "1", ElemType = type, StiffnessNum = num };
            string a = Describe(srcApi, probe), t = Describe(srcTxt, probe);
            output.WriteLine($"  жёсткость {num} ({type}): {(a == t ? "совпадает" : "РАЗНАЯ")} — API {a}" + (a == t ? "" : $"; текст {t}"));
        }
    }

    static string Describe(LiraElementStiffnessSource src, FemElement e)
    {
        static string G(double v) => v.ToString("G6", CultureInfo.InvariantCulture);
        string w = src.UnitWeight(e) is { } uw ? G(uw) : "—";
        if (e.ElemType == "shell")
            return src.Shell(e) is { } s ? $"E {G(s.E)} ν {G(s.Nu)} h {G(s.H)} γ {w}" : $"нет свойств, γ {w}";
        if (src.IsWithoutStiffness(e)) return "без жёсткости (аналог)";
        return src.Bar(e) is { } b ? $"E {G(b.E)} A {G(b.A)} Iy {G(b.Iy)} Iz {G(b.Iz)} J {G(b.J)} γ {w}" : $"нет свойств, γ {w}";
    }

    static T Scalar<T>(SqliteConnection c, string sql, params (string, object)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar() is T v2 ? v2 : default!;
    }
}
