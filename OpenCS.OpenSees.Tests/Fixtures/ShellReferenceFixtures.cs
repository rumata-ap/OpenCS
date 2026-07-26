using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Fixtures;

/// <summary>
/// Cantilever-патчи для reference validation: чистый узловой момент на свободном крае
/// даёт постоянный изгибающий момент без поперечной силы (равновесие проверяется точно,
/// независимо от материала/типа элемента). ν=0 — чтобы исключить биаксиальную связь,
/// которой нет в послойной (uniaxial) модели CScore.PlateSection.
/// </summary>
internal static class ShellReferenceFixtures
{
    public const double E = 30e9;
    public const double Nu = 0.0;
    public const double Thickness = 0.2;
    public const double Length = 2.0;
    public const double Width = 1.0;
    public const double TipMomentEach = 1000.0;

    private const string Fingerprint = "reference-section";

    public static ShellOpenSeesModel Q4EndMoment() => new()
    {
        Nodes = [
            new(1, 0, 0, 0, [true, true, true, true, true, true], "fixed:1"),
            new(2, Length, 0, 0, new bool[6], "free:2"),
            new(3, Length, Width, 0, new bool[6], "free:3"),
            new(4, 0, Width, 0, [true, true, true, true, true, true], "fixed:4")],
        Materials = [new(1, "reference:concrete", new ElasticIsotropicShellMaterialSpec(E, Nu))],
        Sections = [Section()],
        Elements = [new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20, Fingerprint,
            ShellFrame.Identity, ShellIntegrationPolicy.Full, "reference:q4")],
        Loads = [
            new(2, 0, 0, 0, 0, TipMomentEach, 0),
            new(3, 0, 0, 0, 0, TipMomentEach, 0)]
    };

    public static ShellOpenSeesModel T3EndMoment(ShellIntegrationPolicy policy) => new()
    {
        Nodes = [
            new(1, 0, 0, 0, [true, true, true, true, true, true], "fixed:1"),
            new(3, Length, Width, 0, new bool[6], "free:3"),
            new(4, 0, Width, 0, [true, true, true, true, true, true], "fixed:4")],
        Materials = [new(1, "reference:concrete", new ElasticIsotropicShellMaterialSpec(E, Nu))],
        Sections = [Section()],
        Elements = [new(11, ShellElementKind.ASDShellT3, [1, 3, 4], 20, Fingerprint,
            ShellFrame.Identity, policy, "reference:t3")],
        Loads = [new(3, 0, 0, 0, 0, 2 * TipMomentEach, 0)]
    };

    private static RCShellLayeredSection Section() => new(
        20, "reference:plate", Thickness, ShellFrame.Identity,
        [
            new(0, ShellLayerKind.Concrete, -0.075, 0.05, 1, 0, "reference:concrete-layer:0"),
            new(1, ShellLayerKind.Concrete, -0.025, 0.05, 1, 0, "reference:concrete-layer:1"),
            new(2, ShellLayerKind.Concrete, 0.025, 0.05, 1, 0, "reference:concrete-layer:2"),
            new(3, ShellLayerKind.Concrete, 0.075, 0.05, 1, 0, "reference:concrete-layer:3")
        ],
        ShellMappingMode.Exact, [], Fingerprint);
}
