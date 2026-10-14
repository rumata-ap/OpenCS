using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>
/// Как работать с файлом проекта: копия (по умолчанию) с сохранением в <see cref="OutPath"/> или сам файл;
/// <see cref="ArtifactsPath"/> — куда класть артефакты gmsh/OpenSees (по умолчанию — во временную папку сессии).
/// </summary>
sealed record ProjectSessionOptions(bool InPlace = false, string? OutPath = null, bool Force = false, string? ArtifactsPath = null);

/// <summary>
/// Открытый консолью проект. Конструктор <see cref="DatabaseService"/> пишет в файл (миграции, настройки) и
/// повреждённый файл удаляет, поэтому по умолчанию открывается копия во временной папке; файл пользователя
/// меняется только с <c>--in-place</c>. Временная папка сессии есть всегда (копия и артефакты) и удаляется
/// при закрытии.
/// </summary>
sealed class ProjectSession : IDisposable
{
    readonly string _tempDir;
    readonly bool _inPlace;
    readonly string? _outPath;
    readonly FileStream? _lock;
    bool _closed;

    public HeadlessProjectContext Context { get; }

    /// <summary>Файл, указанный пользователем.</summary>
    public string SourcePath { get; }

    /// <summary>Версия схемы БД файла до открытия (миграции копии её поднимают).</summary>
    public int FileSchemaVersion { get; }

    /// <summary>Каталог артефактов внешних программ этой сессии.</summary>
    public string ArtifactsPath { get; }

    /// <summary>Расчёты пишутся в файл, который останется после работы (--in-place или --out).</summary>
    public bool KeepsResults => _inPlace || _outPath != null;

    ProjectSession(HeadlessProjectContext context, string sourcePath, int fileSchemaVersion, string tempDir,
        bool inPlace, string? outPath, string artifactsPath, FileStream? fileLock)
    {
        Context = context;
        SourcePath = sourcePath;
        FileSchemaVersion = fileSchemaVersion;
        _tempDir = tempDir;
        _inPlace = inPlace;
        _outPath = outPath;
        ArtifactsPath = artifactsPath;
        _lock = fileLock;
    }

    public static ProjectSession Open(string path, ProjectSessionOptions options,
        ILogService log, IUserNotifier notifier, IBusyHost busy)
    {
        string source = Path.GetFullPath(path);
        if (!File.Exists(source))
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrFileNotFound"), path));
        // Linux: файл держит другой «opencs --in-place» (flock) — проверить до чтения заголовка, иначе чтение
        // не удастся и файл ошибочно окажется «не SQLite».
        if (!OperatingSystem.IsWindows() && ProjectFile.IsInUse(source))
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrInUseByCli"), path));
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
        }
        else
        {
            if (newer)
                log.Warning(string.Format(Loc.S("CliWarnNewerSchema"), version, DatabaseService.SchemaVersion));
            if (inUse)
                log.Warning(Loc.S("CliWarnInUse"));
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "opencs-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        FileStream? fileLock = null;
        try
        {
            string work = source;
            if (options.InPlace)
                fileLock = LockForSession(source, path);
            else
            {
                work = Path.Combine(tempDir, Path.GetFileName(source));
                CopyProject(source, work, path);
            }
            var context = HeadlessProjectContext.Open(work, log, notifier, busy);
            string artifacts = options.ArtifactsPath != null
                ? Path.GetFullPath(options.ArtifactsPath)
                : Path.Combine(tempDir, "artifacts");
            UseArtifacts(context, artifacts);
            return new ProjectSession(context, source, version, tempDir, options.InPlace, outPath, artifacts, fileLock);
        }
        catch
        {
            fileLock?.Dispose();
            DeleteDirectory(tempDir);
            throw;
        }
    }

    /// <summary>
    /// Артефакты gmsh/OpenSees — в каталог сессии, а не рядом с exe (на Linux /opt обычно без права записи) и не
    /// по пути из настроек проекта (он мог прийти с другой машины). Только в памяти: настройки в БД не пишутся.
    /// </summary>
    static void UseArtifacts(HeadlessProjectContext context, string artifacts)
    {
        context.GmshSettings.ArtifactsPath = Path.Combine(artifacts, "gmsh");
        context.CalcSettings.OpenSeesArtifactsPath = Path.Combine(artifacts, "opensees");
    }

    static void CopyProject(string source, string work, string displayPath)
    {
        try
        {
            // File.Copy не откроет файл, который держит запущенный OpenCS (доступ на запись без FileShare.Write).
            using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var to = new FileStream(work, FileMode.CreateNew, FileAccess.Write);
            from.CopyTo(to);
        }
        catch (IOException) when (!OperatingSystem.IsWindows())
        {
            // Linux: .NET читает с flock(LOCK_SH) — конфликт значит, что файл держит другой «opencs --in-place».
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrInUseByCli"), displayPath));
        }
    }

    /// <summary>
    /// Linux: монопольная рекомендательная блокировка (FileShare.None = flock LOCK_EX) на всю сессию --in-place —
    /// второй процесс opencs файл не откроет. С блокировками SQLite (fcntl) не конфликтует. На Windows так нельзя
    /// (SQLite не откроет файл) — там занятость проверяет <see cref="ProjectFile.IsInUse"/>.
    /// </summary>
    static FileStream? LockForSession(string source, string displayPath)
    {
        if (OperatingSystem.IsWindows()) return null;
        try
        {
            return new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrInUseByCli"), displayPath));
        }
    }

    /// <summary>
    /// Закрывает БД; копия сохраняется в --out (если задан); временная папка удаляется. Возвращает путь
    /// сохранённого проекта с результатами или null.
    /// </summary>
    public string? Close()
    {
        if (_closed) return null;
        _closed = true;
        try
        {
            Context.Dispose();
            SqliteConnection.ClearAllPools();
            if (_inPlace) return SourcePath;
            if (_outPath == null) return null;
            File.Copy(Context.Db.DataSource, _outPath, overwrite: true);
            return _outPath;
        }
        finally
        {
            _lock?.Dispose();
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
