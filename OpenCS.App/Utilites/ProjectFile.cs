using Microsoft.Data.Sqlite;

namespace OpenCS.Utilites;

/// <summary>
/// Проверки файла проекта до открытия через <see cref="DatabaseService"/>: конструктор сервиса пишет в файл
/// (создание таблиц, миграции), а повреждённый файл удаляет и создаёт заново.
/// </summary>
public static class ProjectFile
{
    /// <summary>Файл начинается с заголовка SQLite («SQLite format 3»).</summary>
    public static bool IsSqliteDatabase(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[16];
            if (fs.Read(buf, 0, 16) < 16) return false;
            var header = System.Text.Encoding.ASCII.GetString(buf, 0, 15);
            return header == "SQLite format 3";
        }
        catch { return false; }
    }

    /// <summary>
    /// Версия схемы БД из таблицы settings (ключ schema_version) без записи в файл; null — таблицы или ключа нет
    /// (файл не проект OpenCS или очень старый).
    /// </summary>
    public static int? ReadSchemaVersion(string path)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString();
        try
        {
            using var conn = new SqliteConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value_json FROM settings WHERE key = 'schema_version'";
            if (cmd.ExecuteScalar() is not string row) return null;
            return int.TryParse(row.Trim('"'), out int v) ? v : null;
        }
        catch (SqliteException) { return null; }
    }

    /// <summary>
    /// Файл открыт другим процессом (например, запущенным OpenCS): монопольное открытие не удаётся.
    /// На Linux блокировки рекомендательные — там всегда false.
    /// </summary>
    public static bool IsInUse(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }
}
