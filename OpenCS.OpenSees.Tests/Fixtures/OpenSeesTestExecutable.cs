using Xunit.Sdk;

namespace OpenCS.OpenSees.Tests.Fixtures;

internal static class OpenSeesTestExecutable
{
    public static string ResolveOrSkip()
    {
        string? path = Environment.GetEnvironmentVariable("OPENSEES_EXE");
        if (string.IsNullOrWhiteSpace(path))
            throw SkipException.ForSkip("Задайте OPENSEES_EXE для opt-in OpenSees integration tests.");
        if (!File.Exists(path))
            throw SkipException.ForSkip($"OpenSees executable не найден по OPENSEES_EXE: {path}");

        return Path.GetFullPath(path);
    }
}
