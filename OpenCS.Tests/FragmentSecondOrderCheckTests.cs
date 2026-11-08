using CScore.Fem.Fragment.Masks;
using OpenCS.Services;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Порядок источника масок фрагмента без колонки БД (спека второго порядка фрагмента, срез 3) и новые JSON-поля
/// (флажок постановки, «только P-δ» в сводке).
/// </summary>
public sealed class FragmentSecondOrderCheckTests
{
    static FragmentMaskSet Set(string kind) => new() { SourceKind = kind, SourceRef = "1" };

    static FemCsfeaResultSummary Summary(bool secondOrder, bool local = false) => new() { SecondOrder = secondOrder, LocalPDelta = local };

    [Theory]
    [InlineData(FragmentMaskSet.SourceScad)]
    [InlineData(FragmentMaskSet.SourceLira)]
    public void ImportedSources_AreFirstOrder_EvenWhenStale(string kind)
    {
        Assert.False(FragmentSecondOrderCheck.SourceSecondOrder(Set(kind), FragmentMaskStatus.Current, null));
        Assert.False(FragmentSecondOrderCheck.SourceSecondOrder(Set(kind), FragmentMaskStatus.Stale, null));
    }

    [Theory]
    [InlineData(FragmentMaskSet.SourceCsfeaSecant, true, false, true)]   // секущий с геомнелином (и CR)
    [InlineData(FragmentMaskSet.SourceCsfeaSecant, false, false, false)] // секущий без геомнелина
    [InlineData(FragmentMaskSet.SourceCsfeaSecant, true, true, false)]   // «только P-δ» — P-Δ не учтён
    [InlineData(FragmentMaskSet.SourceCsfeaLinear, true, false, true)]   // линейный с P-Δ источника
    [InlineData(FragmentMaskSet.SourceCsfeaLinear, false, false, false)] // линейный без P-Δ
    public void CsfeaSources_BySummary(string kind, bool secondOrder, bool local, bool expected)
    {
        Assert.Equal(expected, FragmentSecondOrderCheck.SourceSecondOrder(Set(kind), FragmentMaskStatus.Current, Summary(secondOrder, local)));
    }

    [Theory]
    [InlineData(FragmentMaskStatus.Stale)]
    [InlineData(FragmentMaskStatus.SourceDeleted)]
    public void CsfeaSource_NotCurrent_Unknown(FragmentMaskStatus status)
    {
        Assert.Null(FragmentSecondOrderCheck.SourceSecondOrder(Set(FragmentMaskSet.SourceCsfeaSecant), status, Summary(true)));
    }

    [Fact]
    public void NoMasksOrNoSummary_Unknown()
    {
        Assert.Null(FragmentSecondOrderCheck.SourceSecondOrder(null, FragmentMaskStatus.None, null));
        Assert.Null(FragmentSecondOrderCheck.SourceSecondOrder(Set(FragmentMaskSet.SourceCsfeaSecant), FragmentMaskStatus.Current, null));
    }

    [Fact]
    public void Params_FragmentFlag_DefaultFalse_RoundTrips()
    {
        var old = new FemAnalysisParams { Csfea = new FemCsfeaParams() }.ToJson();
        Assert.False(FemAnalysisParams.Parse(old).Csfea!.FragmentSecondOrderOnFirstOrderMasks);
        var json = new FemAnalysisParams { Csfea = new FemCsfeaParams { FragmentSecondOrderOnFirstOrderMasks = true } }.ToJson();
        Assert.True(FemAnalysisParams.Parse(json).Csfea!.FragmentSecondOrderOnFirstOrderMasks);
    }

    [Fact]
    public void Summary_LocalPDelta_RoundTrips_OmittedByDefault()
    {
        var plain = new FemCsfeaResultSummary { SecondOrder = true };
        Assert.DoesNotContain("LocalPDelta", plain.ToJson());
        var local = new FemCsfeaResultSummary { SecondOrder = true, LocalPDelta = true };
        Assert.True(FemCsfeaResultSummary.Parse(local.ToJson())!.LocalPDelta);
    }

    [Fact]
    public void Prepared_SecondOrderOf()
    {
        var none = new FemCsfeaPrepared(null, null, []);
        Assert.True(none.SecondOrderOf(true));
        Assert.False(none.SecondOrderOf(false));
        Assert.False((none with { SecondOrderAllowed = false }).SecondOrderOf(true));
        Assert.True((none with { SecondOrderAllowed = true }).SecondOrderOf(true));
        Assert.False((none with { SecondOrderAllowed = true }).SecondOrderOf(false));
    }
}
