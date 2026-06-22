using System.IO;
using CScore;
using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites;

public class ProfileDB
{
    static readonly Dictionary<string, string> TableMap = new()
    {
        ["Двутавры"] = "Двутавры",
        ["Швеллеры"] = "Швеллеры",
        ["Уголки"] = "Уголки",
        ["Прямоугольные трубы"] = "Прямоугольные трубы",
        ["Трубы"] = "Трубы",
    };

    readonly string _dbPath;

    public ProfileDB(string? dbPath = null)
    {
        _dbPath = dbPath ?? GetDefaultDbPath();
    }

    static string GetDefaultDbPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string path = Path.Combine(baseDir, "DataSource", "Sortamenty.db3");
        if (File.Exists(path)) return path;
        path = Path.Combine(baseDir, "..", "..", "..", "..", "OpenCS", "DataSource", "Sortamenty.db3");
        return Path.GetFullPath(path);
    }

    SqliteConnection Connect()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    public List<string> GetTypes() => [.. TableMap.Keys];

    public List<(int Id, string Name)> GetSubtypes(string shapeType)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ID, SubType FROM ShapeSubTypes WHERE Type = @type AND SubType IS NOT NULL";
        cmd.Parameters.AddWithValue("@type", shapeType);
        using var reader = cmd.ExecuteReader();
        var result = new List<(int, string)>();
        while (reader.Read())
            result.Add((reader.GetInt32(0), reader.GetString(1)));
        return result;
    }

    public List<(int Id, string Name)> GetProfiles(int subtypeId)
    {
        string shapeType = TypeForSubtype(subtypeId);
        string table = TableMap[shapeType];
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT ID, Name FROM ""{table}"" WHERE SubTypeID = @sid";
        cmd.Parameters.AddWithValue("@sid", subtypeId);
        using var reader = cmd.ExecuteReader();
        var result = new List<(int, string)>();
        while (reader.Read())
            result.Add((reader.GetInt32(0), reader.GetString(1)));
        return result;
    }

    public object GetProfile(string shapeType, int profileId)
    {
        string table = TableMap[shapeType];
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT * FROM ""{table}"" WHERE ID = @id";
        cmd.Parameters.AddWithValue("@id", profileId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new KeyException($"Профиль {profileId} не найден в {table}");

        double mm = 0.001;
        double cm = 0.01;

        switch (shapeType)
        {
            case "Двутавры":
                return new IBeamProfile(
                    reader.GetString(reader.GetOrdinal("Name")),
                    reader.GetDouble(reader.GetOrdinal("H")) * mm,
                    reader.GetDouble(reader.GetOrdinal("B")) * mm,
                    reader.GetDouble(reader.GetOrdinal("tw")) * mm,
                    reader.GetDouble(reader.GetOrdinal("tf")) * mm,
                    reader.GetDouble(reader.GetOrdinal("R1")) * mm,
                    reader.GetDouble(reader.GetOrdinal("r2")) * mm,
                    reader.GetDouble(reader.GetOrdinal("A"))
                );
            case "Швеллеры":
            {
                int subTypeId = 0;
                try { subTypeId = reader.GetInt32(reader.GetOrdinal("SubTypeID")); } catch { }
                bool isBent = subTypeId is 28 or 29;
                double chSlope = subTypeId == 25 ? 0.10 : 0.0;
                return new ChannelProfile(
                    reader.GetString(reader.GetOrdinal("Name")),
                    reader.GetDouble(reader.GetOrdinal("H")) * mm,
                    reader.GetDouble(reader.GetOrdinal("B")) * mm,
                    reader.GetDouble(reader.GetOrdinal("tw")) * mm,
                    reader.GetDouble(reader.GetOrdinal("tf")) * mm,
                    reader.GetDouble(reader.GetOrdinal("R1")) * mm,
                    reader.GetDouble(reader.GetOrdinal("r2")) * mm,
                    reader.GetDouble(reader.GetOrdinal("A")),
                    reader.GetDouble(reader.GetOrdinal("X0")) * cm,
                    isBent, chSlope
                );
            }
            case "Уголки":
            {
                int subTypeId = 0;
                try { subTypeId = reader.GetInt32(reader.GetOrdinal("SubTypeID")); } catch { }
                bool isBent = subTypeId is >= 3 and <= 6;
                return new AngleProfile(
                    reader.GetString(reader.GetOrdinal("Name")),
                    reader.GetDouble(reader.GetOrdinal("H")) * mm,
                    reader.GetDouble(reader.GetOrdinal("Bf")) * mm,
                    reader.GetDouble(reader.GetOrdinal("Tw")) * mm,
                    reader.GetDouble(reader.GetOrdinal("Tf")) * mm,
                    reader.GetDouble(reader.GetOrdinal("R")) * mm,
                    reader.GetDouble(reader.GetOrdinal("r_")) * mm,
                    reader.GetDouble(reader.GetOrdinal("A")),
                    reader.GetDouble(reader.GetOrdinal("Xo")) * cm,
                    reader.GetDouble(reader.GetOrdinal("Yo")) * cm,
                    isBent
                );
            }
            case "Прямоугольные трубы":
                return new RectTubeProfile(
                    reader.GetString(reader.GetOrdinal("Name")),
                    reader.GetDouble(reader.GetOrdinal("H")) * mm,
                    reader.GetDouble(reader.GetOrdinal("B")) * mm,
                    reader.GetDouble(reader.GetOrdinal("tw")) * mm,
                    reader.GetDouble(reader.GetOrdinal("tf")) * mm,
                    reader.GetDouble(reader.GetOrdinal("r")) * mm,
                    reader.GetDouble(reader.GetOrdinal("A"))
                );
            case "Трубы":
                return new RoundTubeProfile(
                    reader.GetString(reader.GetOrdinal("Name")),
                    reader.GetDouble(reader.GetOrdinal("H")) * mm,
                    reader.GetDouble(reader.GetOrdinal("t")) * mm,
                    reader.GetDouble(reader.GetOrdinal("A"))
                );
            default:
                throw new ArgumentException($"Неизвестный тип профиля: {shapeType}");
        }
    }

    string TypeForSubtype(int subtypeId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Type FROM ShapeSubTypes WHERE ID = @id";
        cmd.Parameters.AddWithValue("@id", subtypeId);
        return (string)cmd.ExecuteScalar()!;
    }
}

public class KeyException(string message) : Exception(message) { }
