using CScore.PlateStrip;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class ShellReplacementDoubleCountingCheckTests
{
    [Fact]
    public void CheckLoads_ReplaceShellRegion_TagStillOnShell_ReturnsDoubleCountDiagnostic()
    {
        var manifest = new ShellReplacementManifest(
            "strip-1", 10, ShellReplacementPolicy.ReplaceShellRegion, [], ["dead"]);

        var result = ShellReplacementDoubleCountingCheck.CheckLoads(manifest, ["dead"]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_shell_replacement_load_double_count");
    }

    [Fact]
    public void CheckLoads_ReplaceShellRegion_TagRemovedFromShell_NoDiagnostics()
    {
        var manifest = new ShellReplacementManifest(
            "strip-1", 10, ShellReplacementPolicy.ReplaceShellRegion, [], ["dead"]);

        var result = ShellReplacementDoubleCountingCheck.CheckLoads(manifest, []);

        Assert.True(result.IsCalculable);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void CheckLoads_DiagnosticOnly_TagMissingFromShell_ReturnsIncompleteDiagnostic()
    {
        var manifest = new ShellReplacementManifest(
            "strip-1", 10, ShellReplacementPolicy.DiagnosticOnly, [], ["dead"]);

        var result = ShellReplacementDoubleCountingCheck.CheckLoads(manifest, []);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_shell_replacement_diagnostic_incomplete");
    }

    [Fact]
    public void CheckLoads_DiagnosticOnly_AllTagsPresent_NoDiagnostics()
    {
        var manifest = new ShellReplacementManifest(
            "strip-1", 10, ShellReplacementPolicy.DiagnosticOnly, [], ["dead", "live"]);

        var result = ShellReplacementDoubleCountingCheck.CheckLoads(manifest, ["dead", "live", "wind"]);

        Assert.True(result.IsCalculable);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void CheckLoads_EmptyStripLoadSourceTags_NoDiagnosticsRegardlessOfPolicy()
    {
        var replace = new ShellReplacementManifest("strip-1", 10, ShellReplacementPolicy.ReplaceShellRegion, [], []);
        var diagnosticOnly = new ShellReplacementManifest("strip-2", 10, ShellReplacementPolicy.DiagnosticOnly, [], []);

        Assert.True(ShellReplacementDoubleCountingCheck.CheckLoads(replace, []).IsCalculable);
        Assert.True(ShellReplacementDoubleCountingCheck.CheckLoads(diagnosticOnly, []).IsCalculable);
    }

    [Fact]
    public void CheckLoads_SelfWeightTag_TreatedLikeAnyOtherTag()
    {
        var manifest = new ShellReplacementManifest(
            "strip-1", 10, ShellReplacementPolicy.ReplaceShellRegion, [], ["self_weight"]);

        var result = ShellReplacementDoubleCountingCheck.CheckLoads(manifest, ["self_weight"]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_shell_replacement_load_double_count");
    }

    [Fact]
    public void CheckLoads_TagSplitConvention_PartialRegionCoverage_NoFalsePositive()
    {
        // Регион шире коридора: Surface-нагрузка задана двумя PlanarLoad с разными тегами —
        // "dead-in-strip" (перенесён на полосу через StripLoadMapper) и "dead-outside-strip"
        // (остаётся на shell вне коридора). Регрессионный тест на сценарий из ревью спеки
        // (полоса уже региона) — правило разделения тегов должно исключать ложное срабатывание.
        var manifest = new ShellReplacementManifest(
            "strip-1", 10, ShellReplacementPolicy.ReplaceShellRegion, [], ["dead-in-strip"]);

        var result = ShellReplacementDoubleCountingCheck.CheckLoads(manifest, ["dead-outside-strip"]);

        Assert.True(result.IsCalculable);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void CheckLoads_NullManifest_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ShellReplacementDoubleCountingCheck.CheckLoads(null!, []));

    [Fact]
    public void CheckLoads_NullShellTags_Throws()
    {
        var manifest = new ShellReplacementManifest("strip-1", 10, ShellReplacementPolicy.DiagnosticOnly, [], []);

        Assert.Throws<ArgumentNullException>(() => ShellReplacementDoubleCountingCheck.CheckLoads(manifest, null!));
    }
}
