using CScore;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public class ShellMeshPatchPreflightTests
{
    static (PlateSection Section, Diagramm Concrete, Diagramm Rebar) LinearFixture()
    {
        // MatType.ReSteelF даёт симметричную (тождественную по сжатию/растяжению) билинейную
        // диаграмму — по тому же образцу, что CSfea.Tests/EquivalentBeamAnalogyE2ETests.cs
        // использует для "линейного до предела текучести" материала (бетон asymmetric за счёт
        // автоматического среза растяжения, что мешает тестировать чистую линейность).
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = 30000.0, Ry = 60.0, Ru = 60.0, Ft = 60.0, Fc = -60.0,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = 30000.0, Type = MatType.ReSteelF, Tag = "linear" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        var diagram = m.GetDiagramms(DiagrammType.L2)![CalcType.C];

        var section = new PlateSection { H = 0.2, NLayers = 4, TensionConcrete = true };
        return (section, diagram, diagram);
    }

    [Fact]
    public void CheckLinear_LinearMaterialWithinBounds_ReportsLinear()
    {
        var (section, concrete, rebar) = LinearFixture();
        var bounds = new ShellMeshPatchStateBounds(1e-4, 0.01);

        // absoluteTolerance повышен с дефолтных 1e-8 до 1e-6: ComputeTangent — FD-based (fdStep
        // по умолчанию 1e-7), и теоретически нулевые off-diagonal B-блока (симметричное
        // сечение) на пробах, далёких от нуля, несут собственный шум порядка 1e-8-1e-9 —
        // сравнимый с дефолтным absoluteTolerance. Не признак нелинейности материала.
        var result = ShellMeshPatchPreflight.CheckLinear(
            section, concrete, rebar, layerDiagrams: null, bounds, absoluteTolerance: 1e-6);

        Assert.True(result.IsLinear);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void CheckLinear_KinkAtWorkingRange_ReportsNonLinear()
    {
        // Диаграмма с реалистичным пределом текучести Ry=20 попадает в пределы пробы при
        // достаточно широких границах KappaBoundAbs — момент на границе диапазона выводит
        // крайние волокна за предел текучести (излом за пределами малой окрестности нуля).
        var (section, concrete, rebar) = LinearFixture();
        var bounds = new ShellMeshPatchStateBounds(1e-4, 0.5);

        var result = ShellMeshPatchPreflight.CheckLinear(section, concrete, rebar, layerDiagrams: null, bounds);

        Assert.False(result.IsLinear);
        Assert.Contains(result.Diagnostics, d => d.Code == "shell_mesh_patch_nonlinear_source");
    }
}
