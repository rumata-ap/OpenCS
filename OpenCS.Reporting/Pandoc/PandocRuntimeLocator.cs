using System.Security.Cryptography;
using System.Text.Json;

namespace OpenCS.Reporting.Pandoc;

/// <summary>Находит и проверяет только комплект отчётных инструментов рядом с OpenCS.</summary>
public sealed class PandocRuntimeLocator
{
    const string RootName = "Tools/Pandoc";

    /// <summary>Строит абсолютные пути к обязательным файлам комплекта.</summary>
    public PandocRuntimePaths Locate(string? baseDirectory = null)
    {
        string root = Path.GetFullPath(Path.Combine(
            baseDirectory ?? AppContext.BaseDirectory, RootName));
        return new PandocRuntimePaths(
            root,
            Path.Combine(root, "pandoc.exe"),
            Path.Combine(root, "typst.exe"),
            Path.Combine(root, "reference.docx"),
            Path.Combine(root, "report.typ"),
            Path.Combine(root, "report-filter.lua"),
            Path.Combine(root, "fonts"),
            Path.Combine(root, "manifest.json"));
    }

    /// <summary>Проверяет обязательные файлы и SHA-256 из манифеста.</summary>
    public void Validate(PandocRuntimePaths paths, bool requireReferenceDoc = true,
        bool requirePdfTools = true)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var required = new List<string> { paths.PandocExe, paths.LuaFilter, paths.Manifest };
        if (requireReferenceDoc)
            required.Add(paths.ReferenceDocx);
        if (requirePdfTools)
        {
            required.Add(paths.TypstExe);
            required.Add(paths.TypstTemplate);
        }
        foreach (string file in required)
        {
            if (!File.Exists(file))
                throw new PandocExportException(
                    PandocExportFailureReason.BundleMissing,
                    $"Отсутствует обязательный файл комплекта отчётных инструментов: {file}");
        }

        if (requirePdfTools && !Directory.Exists(paths.FontDirectory))
            throw new PandocExportException(
                PandocExportFailureReason.BundleMissing,
                $"Отсутствует каталог шрифтов комплекта отчётных инструментов: {paths.FontDirectory}");

        PandocRuntimeManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PandocRuntimeManifest>(
                File.ReadAllText(paths.Manifest),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new PandocExportException(
                PandocExportFailureReason.BundleIntegrity,
                $"Не удалось прочитать манифест комплекта: {paths.Manifest}",
                inner: ex);
        }

        if (manifest?.Files is null || manifest.Files.Count == 0)
            throw new PandocExportException(
                PandocExportFailureReason.BundleIntegrity,
                $"Манифест комплекта не содержит контрольных сумм: {paths.Manifest}");

        foreach ((string relativePath, string expectedHash) in manifest.Files)
        {
            string fullPath = Path.GetFullPath(Path.Combine(paths.Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if ((!requireReferenceDoc && SamePath(fullPath, paths.ReferenceDocx))
                || (!requirePdfTools && (SamePath(fullPath, paths.TypstExe)
                    || SamePath(fullPath, paths.TypstTemplate)
                    || IsInside(paths.FontDirectory, fullPath))))
                continue;
            if (!IsInside(paths.Root, fullPath) || !File.Exists(fullPath))
                throw new PandocExportException(
                    PandocExportFailureReason.BundleMissing,
                    $"Файл из манифеста отсутствует: {relativePath}");

            string actualHash;
            using (FileStream stream = File.OpenRead(fullPath))
                actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new PandocExportException(
                    PandocExportFailureReason.BundleIntegrity,
                    $"Контрольная сумма файла комплекта не совпадает: {relativePath}");
        }
    }

    static bool IsInside(string root, string path)
    {
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    static bool SamePath(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    sealed class PandocRuntimeManifest
    {
        public Dictionary<string, string> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
