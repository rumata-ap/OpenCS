using Xunit.Sdk;

namespace OpenCS.OpenSees.Tests.Fixtures;

internal static class OpenSeesTestExecutable
{
    public static string ResolveOrSkip()
    {
        const string path = @"C:\Tools\OpenSees\bin\OpenSees.exe";
        if (!File.Exists(path))
            throw SkipException.ForSkip($"OpenSees executable не найден по фиксированному пути: {path}");

        return Path.GetFullPath(path);
    }
}
