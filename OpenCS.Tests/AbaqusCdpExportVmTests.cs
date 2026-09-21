using System.Runtime.InteropServices;
using CScore;
using CScore.Abaqus;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;

using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверяет ViewModel экспорта Abaqus CDP без обращения к системному Clipboard.</summary>
public sealed class AbaqusCdpExportVmTests
{
    [Fact]
    public void ViewModel_BuildsBothFormatsAndCopiesCurrentText()
    {
        var clipboard = new RecordingClipboard();
        var vm = new AbaqusCdpExportVM(CreateConcrete(), clipboard);

        Assert.NotEmpty(vm.KeywordText);
        Assert.NotEmpty(vm.TsvText);
        Assert.Equal(Loc.S("AbaqusCdpSourceEkb"), vm.SourceText);
        Assert.NotEmpty(vm.ElasticModulusText);

        vm.CopyKeywordCommand.Execute(null);
        Assert.Equal(vm.KeywordText, clipboard.LastText);
        vm.CopyTsvCommand.Execute(null);
        Assert.Equal(vm.TsvText, clipboard.LastText);
    }

    [Theory]
    [InlineData("0.0726")]
    [InlineData("0,0726")]
    public void ViewModel_AcceptsBothDecimalSeparators(string value)
    {
        var vm = new AbaqusCdpExportVM(CreateConcrete(), new RecordingClipboard())
        {
            FractureEnergyText = value
        };

        Assert.NotEmpty(vm.KeywordText);
        Assert.Empty(vm.ErrorText);
    }

    [Fact]
    public void ViewModel_InvalidInputClearsOutputAndLocalizesError()
    {
        var vm = new AbaqusCdpExportVM(CreateConcrete(), new RecordingClipboard());

        vm.FractureEnergyText = "0";

        Assert.Empty(vm.KeywordText);
        Assert.Empty(vm.TsvText);
        Assert.Equal(Loc.S("AbaqusCdpInvalidInput"), vm.ErrorText);
    }

    [Fact]
    public void ViewModel_ProfileSwitchPreservesPhysicalValuesThroughFactory()
    {
        var vm = new AbaqusCdpExportVM(CreateConcrete(), new RecordingClipboard());

        vm.SelectedUnitProfile = AbaqusCdpUnitProfile.KpaMKN;

        Assert.Equal(0.0726, Parse(vm.FractureEnergyText), 12);
        Assert.Equal(0.01, Parse(vm.ElementLengthText), 12);
        Assert.Equal("kPa", vm.StressUnitText);
        Assert.Equal("kN/m", vm.EnergyUnitText);
        Assert.NotEmpty(vm.KeywordText);
    }

    [Fact]
    public void ViewModel_ClipboardFailureKeepsOutput()
    {
        var clipboard = new ThrowingClipboard();
        var vm = new AbaqusCdpExportVM(CreateConcrete(), clipboard);
        string keyword = vm.KeywordText;

        vm.CopyKeywordCommand.Execute(null);

        Assert.Equal(keyword, vm.KeywordText);
        Assert.Equal(Loc.S("AbaqusCdpCopyFailed"), vm.ErrorText);
    }

    static double Parse(string value) => Pars.ParseAny(value, out double result) ? result : double.NaN;

    [Fact]
    public void SteelMaterial_SwitchesToPlasticExport()
    {
        var vm = new AbaqusCdpExportVM(CreateSteel(), new RecordingClipboard());

        Assert.True(vm.IsSteel);
        Assert.False(vm.IsConcrete);
        Assert.True(vm.IsStructuralSteel);
        Assert.Equal("0.3", vm.PoissonRatioText);
        Assert.Equal(Loc.S("AbaqusSteelSource"), vm.SourceText);
        Assert.Contains("*Plastic", vm.KeywordText);
        Assert.DoesNotContain("*Concrete", vm.KeywordText);
        Assert.Contains("[Plastic]", vm.TsvText);
        Assert.NotEmpty(vm.WarningsText);
        Assert.Empty(vm.ErrorText);
    }

    [Fact]
    public void SteelMaterial_YieldPlateauToggleRebuildsTable()
    {
        var vm = new AbaqusCdpExportVM(CreateSteel(), new RecordingClipboard());
        int withPlateau = PlasticRows(vm.KeywordText);

        vm.HasYieldPlateau = false;

        Assert.Equal(withPlateau - 1, PlasticRows(vm.KeywordText));
    }

    [Fact]
    public void SteelMaterial_InvalidPoissonRatioShowsError()
    {
        var vm = new AbaqusCdpExportVM(CreateSteel(), new RecordingClipboard());

        vm.PoissonRatioText = "0.5";

        Assert.Empty(vm.KeywordText);
        Assert.Empty(vm.WarningsText);
        Assert.Equal(Loc.S("AbaqusCdpInvalidInput"), vm.ErrorText);
    }

    static int PlasticRows(string keyword) => keyword
        .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
        .SkipWhile(line => line != "*Plastic")
        .Count() - 1;

    /// <summary>С245 по СП 16.13330.2017 (Ry в кПа).</summary>
    static Material CreateSteel()
    {
        var material = new Material { Tag = "C245", Type = MatType.Steel };
        material.MaterialChars =
        [
            SteelChars(CalcType.C, 240_000, 360_000),
            SteelChars(CalcType.CL, 240_000, 360_000),
            SteelChars(CalcType.N, 245_000, 370_000),
            SteelChars(CalcType.NL, 245_000, 370_000)
        ];
        return material;
    }

    static MaterialChars SteelChars(CalcType calcType, double ry, double ru) => new()
    {
        Type = MatType.Steel,
        TypeCalc = calcType,
        Fc = -ry,
        Ft = ry,
        Ry = ry,
        Ru = ru,
        E = 206_000_000,
        Ec2 = -0.025,
        Et2 = 0.025
    };

    static Material CreateConcrete()
    {
        var material = new Material
        {
            Tag = "B25",
            Type = MatType.Concrete,
            E = 30_000_000
        };
        material.MaterialChars =
        [
            Chars(CalcType.C, -14500, 1050, 30_000_000),
            Chars(CalcType.CL, -13050, 1050, 30_000_000),
            Chars(CalcType.N, -18500, 1550, 30_000_000),
            Chars(CalcType.NL, -18500, 1550, 30_000_000)
        ];
        return material;
    }

    static MaterialChars Chars(CalcType calcType, double fc, double ft, double e) => new()
    {
        Type = MatType.Concrete,
        TypeCalc = calcType,
        Fc = fc,
        Ft = ft,
        E = e,
        Ec0 = -0.002,
        Ec1 = -0.00029,
        Ec2 = -0.0035,
        Et0 = 0.0001,
        Et1 = 0.000021,
        Et2 = 0.00015
    };

    sealed class RecordingClipboard : ITextClipboardService
    {
        public string? LastText { get; private set; }
        public void SetText(string text) => LastText = text;
    }

    sealed class ThrowingClipboard : ITextClipboardService
    {
        public void SetText(string text) => throw new ExternalException("Clipboard is busy.");
    }
}
