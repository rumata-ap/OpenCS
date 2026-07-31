using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Fixtures;

/// <summary>Минимальные фикстуры shell/beam connections: общий узел, equalDOF-шов, rigidLink-эксцентриситет.</summary>
internal static class ShellBeamConnectionFixtures
{
    private const string Fingerprint = "connection-section";

    /// <summary>Q4-плита, фиксированная по краю 1-4, с колонной (beam), растущей из узла 2 вверх (+Z);
    /// нагрузка Fx на вершине колонны передаётся в плиту через общий узел 2.</summary>
    public static ShellOpenSeesModel SharedNodeColumn()
    {
        var plate = Plate();
        return plate with
        {
            Nodes = plate.Nodes.Concat([new(5, 1, 0, 1.0, new bool[6], "column-tip")]).ToArray(),
            BeamElements = [new(100, 2, 5, 0.01, 200e9, 77e9, 1e-6, 1e-5, 1e-5, (1, 0, 0))],
            Stages = [new() { Tag = "stage-1", Loads = [new(5, 1000, 0, 0, 0, 0, 0)] }]
        };
    }

    /// <summary>Два смежных Q4-патча со швом из НЕЗАВИСИМЫХ (совпадающих геометрически) узлов 2/3 и 6/7,
    /// соединённых equalDOF по всем 6 DOF; нагрузка приложена на дальнем крае второго патча.</summary>
    public static ShellOpenSeesModel EqualDofSeam()
    {
        RCShellLayeredSection section = Section(Fingerprint);
        return new ShellOpenSeesModel
        {
            Nodes = [
                new(1, 0, 0, 0, [true, true, true, true, true, true], "A:1"),
                new(2, 1, 0, 0, new bool[6], "A:2"),
                new(3, 1, 1, 0, new bool[6], "A:3"),
                new(4, 0, 1, 0, [true, true, true, true, true, true], "A:4"),
                new(6, 1, 0, 0, new bool[6], "B:6-coincides-with-2"),
                new(7, 1, 1, 0, new bool[6], "B:7-coincides-with-3"),
                new(8, 2, 0, 0, new bool[6], "B:8"),
                new(9, 2, 1, 0, new bool[6], "B:9")],
            Materials = [new(1, "conn", new ElasticIsotropicShellMaterialSpec(30e9, 0.0))],
            Sections = [section],
            Elements = [
                new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20, Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "patch-a"),
                new(11, ShellElementKind.ASDShellQ4, [6, 8, 9, 7], 20, Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "patch-b")],
            EqualDofConstraints = [
                new(2, 6, [1, 2, 3, 4, 5, 6]),
                new(3, 7, [1, 2, 3, 4, 5, 6])],
            Stages = [new() { Tag = "stage-1", Loads = [new(8, 0, 0, -1000, 0, 0, 0), new(9, 0, 0, -1000, 0, 0, 0)] }]
        };
    }

    /// <summary>Q4-плита, фиксированная по краю 1-4, с rigidLink beam от узла 2 к эксцентричному узлу 5
    /// (смещён на +0.5 м по Z); горизонтальная нагрузка Fx на узле 5 создаёт момент в плите через жёсткую вставку.</summary>
    public static ShellOpenSeesModel RigidLinkOffset()
    {
        var plate = Plate();
        return plate with
        {
            Nodes = plate.Nodes.Concat([new(5, 1, 0, 0.5, new bool[6], "offset-node")]).ToArray(),
            RigidLinks = [new(2, 5, ShellRigidLinkType.Beam)],
            Stages = [new() { Tag = "stage-1", Loads = [new(5, 1000, 0, 0, 0, 0, 0)] }]
        };
    }

    private static ShellOpenSeesModel Plate() => new()
    {
        Nodes = [
            new(1, 0, 0, 0, [true, true, true, true, true, true], "1"),
            new(2, 1, 0, 0, new bool[6], "2"),
            new(3, 1, 1, 0, new bool[6], "3"),
            new(4, 0, 1, 0, [true, true, true, true, true, true], "4")],
        Materials = [new(1, "conn", new ElasticIsotropicShellMaterialSpec(30e9, 0.0))],
        Sections = [Section(Fingerprint)],
        Elements = [new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20, Fingerprint,
            ShellFrame.Identity, ShellIntegrationPolicy.Full, "plate")]
    };

    private static RCShellLayeredSection Section(string fingerprint) => new(
        20, "conn:plate", 0.2, ShellFrame.Identity,
        [
            new(0, ShellLayerKind.Concrete, -0.075, 0.05, 1, 0, "c0"),
            new(1, ShellLayerKind.Concrete, -0.025, 0.05, 1, 0, "c1"),
            new(2, ShellLayerKind.Concrete, 0.025, 0.05, 1, 0, "c2"),
            new(3, ShellLayerKind.Concrete, 0.075, 0.05, 1, 0, "c3")
        ],
        ShellMappingMode.Exact, [], fingerprint);
}
