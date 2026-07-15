using System.Text.Json;
using OpenCS.OpenSees.Runtime;

namespace OpenCS.OpenSees.Artifacts;

/// <summary>Создаёт уникальный каталог и сохраняет диагностические артефакты запуска.</summary>
public sealed class OpenSeesArtifactStore
{
    private readonly string _rootDirectory;

    /// <summary>Создаёт хранилище под указанным корневым каталогом.</summary>
    public OpenSeesArtifactStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Не задан корневой каталог артефактов.", nameof(rootDirectory));

        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    /// <summary>Создаёт новый каталог запуска с UTC-временем и случайным суффиксом.</summary>
    public OpenSeesArtifact Create()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string name = $"{DateTime.UtcNow:yyyyMMdd'T'HHmmssfff'Z'}_{Guid.NewGuid():N}";
            string directory = Path.Combine(_rootDirectory, name);
            if (Directory.Exists(directory))
                continue;

            Directory.CreateDirectory(directory);
            return new OpenSeesArtifact(directory);
        }

        throw new IOException("Не удалось создать уникальный каталог артефактов.");
    }

    /// <summary>Сохраняет Tcl-сценарий.</summary>
    public void WriteScript(OpenSeesArtifact artifact, string script) =>
        File.WriteAllText(artifact.ScriptPath, script);

    /// <summary>Сохраняет JSON-манифест.</summary>
    public void WriteManifest(OpenSeesArtifact artifact, OpenSeesManifest manifest) =>
        File.WriteAllText(artifact.ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

    /// <summary>Сохраняет stdout, stderr и сведения о завершении процесса.</summary>
    public void WriteRunResult(OpenSeesArtifact artifact, OpenSeesRunResult result)
    {
        File.WriteAllText(artifact.StdoutPath, result.Stdout);
        File.WriteAllText(artifact.StderrPath, result.Stderr);
        File.WriteAllText(artifact.ExitPath, JsonSerializer.Serialize(result, JsonOptions));
    }

    private static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true };
}

/// <summary>Набор фиксированных путей одного запуска OpenSees.</summary>
public sealed class OpenSeesArtifact
{
    /// <summary>Создаёт описание каталога артефактов.</summary>
    public OpenSeesArtifact(string directoryPath)
    {
        DirectoryPath = Path.GetFullPath(directoryPath);
    }

    /// <summary>Каталог запуска.</summary>
    public string DirectoryPath { get; }

    /// <summary>Tcl-сценарий.</summary>
    public string ScriptPath => Path.Combine(DirectoryPath, "script.tcl");

    /// <summary>Манифест.</summary>
    public string ManifestPath => Path.Combine(DirectoryPath, "manifest.json");

    /// <summary>Перехваченный stdout.</summary>
    public string StdoutPath => Path.Combine(DirectoryPath, "stdout.txt");

    /// <summary>Перехваченный stderr.</summary>
    public string StderrPath => Path.Combine(DirectoryPath, "stderr.txt");

    /// <summary>Сведения о завершении.</summary>
    public string ExitPath => Path.Combine(DirectoryPath, "exit.json");
}
