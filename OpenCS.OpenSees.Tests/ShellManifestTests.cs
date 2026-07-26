using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellManifestTests
{
    [Fact]
    public void Manifest_PreservesShellMappingMetadata()
    {
        var manifest = new OpenSeesManifest
        {
            Shell = new ShellManifestData
            {
                SectionFingerprints = ["section-fingerprint"],
                Frames = [ShellFrame.Identity],
                LayerRecords = [new RCShellLayer(0, ShellLayerKind.Concrete, 0, 0.2, 1, 0, "concrete")],
                IntegrationPolicies = [ShellIntegrationPolicy.Full],
                RecorderMappings = [new ShellRecorderMapping("section_forces.out", 10, 1, "section-fingerprint")],
                MappingMode = ShellMappingMode.NativeWithExplicitApproximation,
                ApproximationFlags = ["smeared-rebar"],
                UnitContract = "m,N,Pa",
                FaceConvention = "z>0 = +Normal",
                SignContract = "Mx = integral(sigmax*z dz)",
                Diagnostics = ["fixture"]
            }
        };

        Assert.NotNull(manifest.Shell);
        Assert.Equal("section-fingerprint", Assert.Single(manifest.Shell!.SectionFingerprints));
        Assert.Equal(ShellMappingMode.NativeWithExplicitApproximation, manifest.Shell.MappingMode);
        Assert.Equal("Mx = integral(sigmax*z dz)", manifest.Shell.SignContract);
        Assert.Equal("section_forces.out", Assert.Single(manifest.Shell.RecorderMappings).FileName);
        Assert.Equal("m,N,Pa", manifest.Shell.UnitContract);
        Assert.Equal("fixture", Assert.Single(manifest.Shell.Diagnostics));
    }
}
