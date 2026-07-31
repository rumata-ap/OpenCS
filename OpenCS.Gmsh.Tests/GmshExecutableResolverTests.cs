using OpenCS.Gmsh.Runtime;

namespace OpenCS.Gmsh.Tests;

public sealed class GmshExecutableResolverTests
{
    [Fact]
    public void Resolve_PrefersExistingExplicitPath()
    {
        const string executable = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";

        var resolved = new GmshExecutableResolver().Resolve(executable);

        Assert.Equal(Path.GetFullPath(executable), resolved.Path);
        Assert.Equal("explicit", resolved.Source);
    }
}
