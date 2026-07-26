using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Fixtures;

/// <summary>Минимальные упругие shell fixtures для unit и opt-in integration tests.</summary>
internal static class ShellModelFixtures
{
    /// <summary>Один квадратный Q4 shell patch.</summary>
    public static ShellOpenSeesModel Q4Elastic() => Create(
        ShellElementKind.ASDShellQ4, ShellIntegrationPolicy.Full, [1, 2, 3, 4], 10);

    /// <summary>Один треугольный T3 shell patch.</summary>
    public static ShellOpenSeesModel T3Elastic(ShellIntegrationPolicy policy) => Create(
        ShellElementKind.ASDShellT3, policy, [1, 2, 3], 11);

    /// <summary>Q4 patch со свободными узлами 2/3, нагруженными вертикальной силой (нетривиальный изгиб).</summary>
    public static ShellOpenSeesModel Q4WithTipLoad() => Q4Elastic() with
    {
        Loads = [new(2, 0, 0, -1000, 0, 0, 0), new(3, 0, 0, -1000, 0, 0, 0)]
    };

    /// <summary>T3 patch со свободным узлом 3, нагруженным вертикальной силой.</summary>
    public static ShellOpenSeesModel T3WithTipLoad(ShellIntegrationPolicy policy) => T3Elastic(policy) with
    {
        Loads = [new(3, 0, 0, -1000, 0, 0, 0)]
    };

    private static ShellOpenSeesModel Create(
        ShellElementKind kind,
        ShellIntegrationPolicy policy,
        IReadOnlyList<int> connectivity,
        int elementTag)
    {
        const string fingerprint = "shell-fixture-section";
        NormalizedShellNode[] allNodes = [
            new(1, 0, 0, 0, Fixed(kind, 1), "fixture:1"),
            new(2, 1, 0, 0, Fixed(kind, 2), "fixture:2"),
            new(3, 1, 1, 0, Fixed(kind, 3), "fixture:3"),
            new(4, 0, 1, 0, Fixed(kind, 4), "fixture:4")];
        // Только узлы, реально входящие в connectivity элемента — иначе несвязанный узел
        // с нулевой жёсткостью вырождает глобальную матрицу (см. находку по T3-фикстуре).
        return new ShellOpenSeesModel
        {
            Nodes = allNodes.Where(node => connectivity.Contains(node.Tag)).ToArray(),
            Materials = [new(1, "fixture:concrete", new ElasticIsotropicShellMaterialSpec(30e9, 0.25))],
            Sections = [new(20, "fixture:plate", 0.2, ShellFrame.Identity,
                [
                    new(0, ShellLayerKind.Concrete, -0.075, 0.05, 1, 0, "fixture:concrete-layer:0"),
                    new(1, ShellLayerKind.Concrete, -0.025, 0.05, 1, 0, "fixture:concrete-layer:1"),
                    new(2, ShellLayerKind.Concrete, 0.025, 0.05, 1, 0, "fixture:concrete-layer:2"),
                    new(3, ShellLayerKind.Concrete, 0.075, 0.05, 1, 0, "fixture:concrete-layer:3")
                ],
                ShellMappingMode.Exact, [], fingerprint)],
            Elements = [new(elementTag, kind, connectivity, 20, fingerprint,
                ShellFrame.Identity, policy, $"fixture:element:{elementTag}")]
        };
    }

    private static bool[] Fixed(ShellElementKind kind, int tag) =>
        kind == ShellElementKind.ASDShellQ4
            ? tag is 1 or 4 ? [true, true, true, true, true, true] : new bool[6]
            : tag is 1 or 2 ? [true, true, true, true, true, true] : new bool[6];
}
