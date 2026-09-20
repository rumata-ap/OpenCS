using System.Security.Cryptography;
using System.Text.Json;
using OpenCS.Reporting.Pandoc;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет поиск и целостность автономного комплекта отчётных инструментов.</summary>
public sealed class PandocRuntimeLocatorTests
{
    [Fact]
    public void Locate_uses_only_application_relative_tools_directory()
    {
        string baseDirectory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var paths = new PandocRuntimeLocator().Locate(baseDirectory);
            Assert.Equal(Path.Combine(baseDirectory, "Tools", "Pandoc"), paths.Root);
            Assert.EndsWith(Path.Combine("Tools", "Pandoc", "pandoc.exe"), paths.PandocExe);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void Validate_rejects_missing_required_file()
    {
        string baseDirectory = CreateBundle();
        try
        {
            File.Delete(new PandocRuntimeLocator().Locate(baseDirectory).TypstExe);
            PandocExportException error = Assert.Throws<PandocExportException>(() =>
                new PandocRuntimeLocator().Validate(new PandocRuntimeLocator().Locate(baseDirectory)));
            Assert.Equal(PandocExportFailureReason.BundleMissing, error.Reason);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void Validate_rejects_hash_mismatch()
    {
        string baseDirectory = CreateBundle();
        try
        {
            var paths = new PandocRuntimeLocator().Locate(baseDirectory);
            File.AppendAllText(paths.PandocExe, "changed");
            PandocExportException error = Assert.Throws<PandocExportException>(() =>
                new PandocRuntimeLocator().Validate(paths));
            Assert.Equal(PandocExportFailureReason.BundleIntegrity, error.Reason);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    static string CreateBundle()
    {
        string baseDirectory = Directory.CreateTempSubdirectory().FullName;
        var locator = new PandocRuntimeLocator();
        var paths = locator.Locate(baseDirectory);
        Directory.CreateDirectory(paths.FontDirectory);
        string[] files =
        [
            paths.PandocExe,
            paths.TypstExe,
            paths.ReferenceDocx,
            paths.TypstTemplate,
            paths.LuaFilter
        ];
        foreach (string file in files)
            File.WriteAllText(file, Path.GetFileName(file));
        File.WriteAllText(Path.Combine(paths.FontDirectory, "font.ttf"), "font");

        var hashes = files
            .Concat([Path.Combine(paths.FontDirectory, "font.ttf")])
            .ToDictionary(
                file => Path.GetRelativePath(paths.Root, file).Replace(Path.DirectorySeparatorChar, '/'),
                file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),
                StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(paths.Manifest, JsonSerializer.Serialize(new { files = hashes }));
        return baseDirectory;
    }
}
