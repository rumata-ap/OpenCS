using CScore;
using CScore.ParametricRc;
using CScore.ParametricSteel;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Проверка параметрических определений службами до генерации: материалы нужного типа с путями документа
/// (<c>concrete</c>, <c>rebar</c>, <c>stirrups[i].material</c>, <c>steel</c>), геометрия — генератором (путь пустой).
/// </summary>
public class ParametricSectionValidateTests
{
    static Material Mat(int id, MatType type) => new() { Id = id, Tag = $"M{id}", Type = type };

    static readonly Material[] Materials =
        [Mat(1, MatType.Concrete), Mat(2, MatType.ReSteelF), Mat(3, MatType.ReSteelU), Mat(4, MatType.Steel), Mat(5, MatType.Custom)];

    static ParametricRcSectionDefinition Beam(int concrete, int rebar, int stirrups) =>
        ParametricRcSectionDefinition.Rectangle(0.3, 0.5) with
        {
            Tag = "Р",
            ConcreteMaterialId = concrete,
            LongitudinalMaterialId = rebar,
            LowerRebar = ParametricLongitudinalLayer.Physical(3, 0.02, -0.21),
            StirrupCuts = [new ParametricStirrupCutSet(ParametricStirrupZone.Body, ParametricStirrupDirection.Vertical, 2, 0.008, 0.15, 0.03, stirrups)],
        };

    [Fact]
    public void Rc_Valid_NoErrors() =>
        Assert.Empty(ParametricRcSectionProjectService.Validate(Beam(1, 2, 3), Materials));

    [Fact]
    public void Rc_WrongMaterialKinds_PathsPerField()
    {
        var errors = ParametricRcSectionProjectService.Validate(Beam(2, 1, 4), Materials);
        Assert.Equal(["concrete", "rebar", "stirrups[0].material"], errors.Select(e => e.Path));
    }

    [Fact]
    public void Rc_NoTagAndBadGeometry_TagPathAndGeneratorDiagnostics()
    {
        var bad = ParametricRcSectionDefinition.Tee(0.3, 0.5, 0.4, 0.1) with
        {
            Tag = " ", ConcreteMaterialId = 1, LongitudinalMaterialId = 2
        };
        var errors = ParametricRcSectionProjectService.Validate(bad, Materials);
        Assert.Equal("tag", errors[0].Path);
        Assert.Contains(errors, e => e.Path == "" && e.Message.Length > 0);
    }

    [Fact]
    public void Rc_StirrupMaterialIgnoredWhenNoCuts()
    {
        var d = Beam(1, 2, 0) with { StirrupCuts = [] };
        Assert.Empty(ParametricRcSectionProjectService.ValidateMaterials(d, Materials));
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    public void Steel_MaterialSteelOrCustom(int materialId, bool ok)
    {
        var d = ParametricSteelSectionDefinition.Pipe(0.159, 0.006) with { Tag = "Т", MaterialId = materialId };
        var errors = ParametricSteelSectionProjectService.Validate(d, Materials);
        if (ok) Assert.Empty(errors);
        else Assert.Equal("steel", Assert.Single(errors).Path);
    }

    [Fact]
    public void Steel_BadGeometry_GeneratorDiagnostics()
    {
        var d = ParametricSteelSectionDefinition.Pipe(0.159, 0.1) with { Tag = "Т", MaterialId = 4 };
        Assert.Equal("", Assert.Single(ParametricSteelSectionProjectService.Validate(d, Materials)).Path);
    }
}
