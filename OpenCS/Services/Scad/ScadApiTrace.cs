using System.Diagnostics;
using System.IO;
using System.Text;

namespace OpenCS.Services.Scad;

/// <summary>
/// Журнал вызовов SCADAPIX.dll в файл <c>%LOCALAPPDATA%\OpenCS\scad-api.log</c> — для разбора аварийного
/// завершения процесса в нативном коде (исключения .NET при этом не возникает, журнал приложения теряется).
/// Каждая строка сразу пишется на диск. Ошибки записи журнала игнорируются.
/// </summary>
internal static class ScadApiTrace
{
    static readonly object Sync = new();

    /// <summary>Путь к файлу журнала.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCS", "scad-api.log");

    /// <summary>Записать строку: время, поток, текст.</summary>
    public static void Write(string text)
    {
        try
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId,3}] {text}{Environment.NewLine}";
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                using var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite,
                    4096, FileOptions.WriteThrough);
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Отметка начала этапа; Dispose результата — отметка конца с длительностью.</summary>
    public static Scope Step(string name) => new(name);

    /// <summary>Этап журнала.</summary>
    public readonly struct Scope : IDisposable
    {
        readonly string _name;
        readonly long _start;

        public Scope(string name)
        {
            _name = name;
            _start = Stopwatch.GetTimestamp();
            Write("→ " + name);
        }

        public void Dispose() =>
            Write($"← {_name} ({Stopwatch.GetElapsedTime(_start).TotalMilliseconds:0} мс)");
    }
}
