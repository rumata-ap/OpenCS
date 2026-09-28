using System.Globalization;
using CScore;
using CScore.Sp63.CrackWidth;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Модуль деформаций бетона при продолжительном действии нагрузки (NL):
/// справочник DataSource и миграция v63 старых проектов. Eb,τ = Eb/(1+φb,cr),
/// п. 6.1.15, 6.1.16, табл. 6.12 СП 63.13330.
/// </summary>
public sealed class ConcreteLongTermModulusTests
{
    static readonly string[] Prefixes = ["Бетон_тяжелый", "Мелкозернистый группы А", "Мелкозернистый группы Б"];

    public static TheoryData<string, int> NlCatalogs()
    {
        var data = new TheoryData<string, int>();
        foreach (var p in Prefixes)
            for (int k = 1; k <= 3; k++)
                data.Add(p, k);
        return data;
    }

    [Theory]
    [MemberData(nameof(NlCatalogs))]
    public void NlCatalog_ModulusFollowsCreepCoefficient(string prefix, int dampness)
    {
        var n = ReadCsv(prefix + "_N.csv").ToDictionary(r => r["Tag"]);
        var nl = ReadCsv($"{prefix}_NL_{dampness}.csv");
        Assert.NotEmpty(nl);

        foreach (var r in nl)
        {
            string tag = r["Tag"];
            Assert.Equal(dampness, (int)Num(r, "Dampness"));
            var humidity = ConcreteLongTermModulusRepair.ToHumidity((Dampness)dampness)!.Value;
            double phi = Sp63Curvature.PhiBCr(Num(r, "Class"), humidity)!.Value;
            double e = Num(r, "E");

            AssertRel(Num(n[tag], "E") / (1 + phi), e, $"{prefix} NL_{dampness} {tag}: E");
            AssertRel(0.6 * Num(r, "Fc") / e, Num(r, "Ec1"), $"{prefix} NL_{dampness} {tag}: Ec1");
            AssertRel(0.6 * Num(r, "Ft") / e, Num(r, "Et1"), $"{prefix} NL_{dampness} {tag}: Et1");
        }
    }

    [Fact]
    public void MigrationV63_RepairsSavedHeavyConcreteNl()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-nl-v63-{Guid.NewGuid():N}.db");
        try
        {
            int id;
            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                // Материал из обращения 27.09.2026: E и εb1 испорчены сменой влажности.
                var m = new Material(0) { Type = MatType.Concrete, Tag = "B30", Description = "Бетон тяжелый по СП 63.13330" };
                m.N = new MaterialChars { Type = MatType.Concrete, TypeCalc = CalcType.N, Class = 30, Fc = -22000, Ft = 1750, E = 32.5e6 };
                m.NL = new MaterialChars
                {
                    Type = MatType.Concrete, TypeCalc = CalcType.NL, Class = 30, Dampness = Dampness.от40_до70,
                    Fc = -22000, Ft = 1750, E = 16414141.41, Ec1 = -0.0008041846155875174, Et1 = 6.39692307853707E-05
                };
                db.SaveMaterial(m);
                id = m.Id;
            }
            SetSchemaVersion(path, 62);

            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                var nl = db.Materials.Single(m => m.Id == id).NL!;
                Assert.Equal(32.5e6 / 3.3, nl.E, 1);
                Assert.Equal(0.6 * -22000 / (32.5e6 / 3.3), nl.Ec1, 9);
                Assert.Equal(0.6 * 1750 / (32.5e6 / 3.3), nl.Et1, 9);
            }
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    static void AssertRel(double expected, double actual, string what) =>
        Assert.True(Math.Abs(actual - expected) <= 1e-4 * Math.Abs(expected),
            $"{what}: ожидалось {expected:G9}, в справочнике {actual:G9}");

    static double Num(Dictionary<string, string> row, string col) =>
        double.Parse(row[col], NumberStyles.Float, CultureInfo.InvariantCulture);

    static List<Dictionary<string, string>> ReadCsv(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        var lines = File.ReadAllLines(Path.Combine(dir!.FullName, "OpenCS", "DataSource", name))
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var header = lines[0].Split(';');
        return lines.Skip(1)
            .Select(l => header.Zip(l.Split(';')).ToDictionary(p => p.First, p => p.Second))
            .ToList();
    }

    static void SetSchemaVersion(string path, int version)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE settings SET value_json = $v WHERE key = 'schema_version'";
        cmd.Parameters.AddWithValue("$v", version.ToString());
        cmd.ExecuteNonQuery();
    }
}
