using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>Как работать с файлом проекта: копия (по умолчанию) с сохранением в <see cref="OutPath"/> или сам файл.</summary>
sealed record ProjectSessionOptions(bool InPlace = false, string? OutPath = null, bool Force = false);

/// <summary>
/// Открытый консолью проект. Конструктор <see cref="DatabaseService"/> пишет в файл (миграции, настройки) и
/// повреждённый файл удаляет, поэтому по умолчанию открывается копия во временной папке; файл пользователя
/// меняется только с <c>--in-place</c>.
/// </summary>
sealed class ProjectSession : IDisposable
{
    readonly string? _tempDir;
    readonly string? _outPath;
    bool _closed;

    public HeadlessProjectContext Context { get; }

    /// <summary>Файл, указанный пользователем.</summary>
    public string SourcePath { get; }

    /// <summary>Версия схемы БД файла до открытия (миграции копии её поднимают).</summary>
    public int FileSchemaVersion { get; }

    /// <summary>Расчёты пишутся в файл, который останется после работы (--in-place или --out).</summary>
    public bool KeepsResults => _tempDir == null || _outPath != null;

    ProjectSession(HeadlessProjectContext context, string sourcePath, int fileSchemaVersion, string? tempDir, string? outPath)
    {
        Context = context;
        SourcePath = sourcePath;
        FileSchemaVersion = fileSchemaVersion;
        _tempDir = tempDir;
        _outPath = outPath;
    }

    public static ProjectSession Open(string path, ProjectSessionOptions options,
        ILogService log, IUserNotifier notifier, IBusyHost busy)
    {
        string source = Path.GetFullPath(path);
        if (!File.Exists(source))
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrFileNotFound"), path));
        if (!ProjectFile.IsSqliteDatabase(source))
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrNotSqlite"), path));
        int version = ProjectFile.ReadSchemaVersion(source)
            ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrNotProject"), path));

        string? outPath = options.OutPath == null ? null : Path.GetFullPath(options.OutPath);
        if (options.InPlace && outPath != null)
            throw new CliException(CliExitCode.Usage, Loc.S("CliErrInPlaceWithOut"));
        if (outPath != null && SamePath(outPath, source))
            throw new CliException(CliExitCode.Usage, Loc.S("CliErrOutIsInput"));
        if (outPath != null && File.Exists(outPath) && !options.Force)
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrOutExists"), options.OutPath));

        bool newer = version > DatabaseService.SchemaVersion;
        bool inUse = ProjectFile.IsInUse(source);
        if (options.InPlace)
        {
            if (newer)
                throw new CliException(CliExitCode.Usage,
                    string.Format(Loc.S("CliErrNewerSchema"), version, DatabaseService.SchemaVersion));
            if (inUse)
                throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrInUse"), path));
            return new ProjectSession(HeadlessProjectContext.Open(source, log, notifier, busy), source, version, null, null);
        }

        if (newer)
            log.Warning(string.Format(Loc.S("CliWarnNewerSchema"), version, DatabaseService.SchemaVersion));
        if (inUse)
            log.Warning(Loc.S("CliWarnInUse"));

        string tempDir = Path.Combine(Path.GetTempPath(), "opencs-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string work = Path.Combine(tempDir, Path.GetFileName(source));
            // File.Copy не откроет файл, который держит запущенный OpenCS (доступ на запись без FileShare.Write).
            using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var to = new FileStream(work, FileMode.CreateNew, FileAccess.Write))
                from.CopyTo(to);
            var context = HeadlessProjectContext.Open(work, log, notifier, busy);
            return new ProjectSession(context, source, version, tempDir, outPath);
        }
        catch
        {
            DeleteDirectory(tempDir);
            throw;
        }
    }

    /// <summary>
    /// Закрывает БД; копия сохраняется в --out (если задан) и удаляется. Возвращает путь сохранённого
    /// проекта с результатами или null.
    /// </summary>
    public string? Close()
    {
        if (_closed) return null;
        _closed = true;
        Context.Dispose();
        SqliteConnection.ClearAllPools();
        if (_tempDir == null) return SourcePath;
        try
        {
            if (_outPath == null) return null;
            File.Copy(Context.Db.DataSource, _outPath, overwrite: true);
            return _outPath;
        }
        finally
        {
            DeleteDirectory(_tempDir);
        }
    }

    public void Dispose() => Close();

    static bool SamePath(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    static void DeleteDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
