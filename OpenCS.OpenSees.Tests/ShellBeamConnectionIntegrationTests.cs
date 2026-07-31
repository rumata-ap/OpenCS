using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellBeamConnectionIntegrationTests
{
    [Fact]
    public async Task SharedNodeColumn_GlobalEquilibriumHolds()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellResult result = await RunAsync(executable, ShellBeamConnectionFixtures.SharedNodeColumn());

        double reactionFx = result.Reactions.Sum(r => r.Fx);
        // Допуск ослаблен с 1e-6 до 1e-3 при переходе с точного одношагового линейного
        // решения на итеративный адаптивный Newton-цикл (срез 2) — критерий сходимости
        // NormDispIncr контролирует приращение перемещения, а не невязку силы напрямую,
        // остаточная невязка силы порядка 1e-4 Н на нагрузке 1000 Н ожидаема и не является
        // признаком ошибки.
        Assert.True(Math.Abs(reactionFx + 1000) < 1e-3,
            $"Сумма реакций Fx должна уравновешивать нагрузку 1000 Н на вершине колонны, получено {reactionFx}");
    }

    [Fact]
    public async Task EqualDofSeam_CoincidentNodesMatchDisplacementsAndEquilibriumHolds()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellResult result = await RunAsync(executable, ShellBeamConnectionFixtures.EqualDofSeam());

        var node2 = result.Displacements.Single(d => d.NodeTag == 2);
        var node6 = result.Displacements.Single(d => d.NodeTag == 6);
        Assert.True(Math.Abs(node2.Uz - node6.Uz) < 1e-9,
            $"equalDOF: Uz узлов 2 и 6 должны совпадать, {node2.Uz} vs {node6.Uz}");

        double reactionFz = result.Reactions.Sum(r => r.Fz);
        Assert.True(Math.Abs(reactionFz - 2000) < 1e-6,
            $"Сумма реакций Fz должна уравновешивать суммарную нагрузку -2000 Н, получено {reactionFz}");
    }

    [Fact]
    public async Task RigidLinkOffset_EccentricForceProducesMomentAtSupport()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellResult result = await RunAsync(executable, ShellBeamConnectionFixtures.RigidLinkOffset());

        double reactionFx = result.Reactions.Sum(r => r.Fx);
        double reactionMomentY = result.Reactions.Sum(r => r.My);
        const double offsetZ = 0.5;
        const double appliedFx = 1000;

        // См. комментарий в SharedNodeColumn_GlobalEquilibriumHolds про ослабление допуска.
        Assert.True(Math.Abs(reactionFx + appliedFx) < 1e-3,
            $"Сумма реакций Fx должна уравновешивать нагрузку {appliedFx} Н, получено {reactionFx}");
        Assert.True(Math.Abs(Math.Abs(reactionMomentY) - appliedFx * offsetZ) < 0.05 * appliedFx * offsetZ,
            $"Эксцентричная сила должна создавать момент ~Fx*offset={appliedFx * offsetZ} Н·м через rigidLink, получено {reactionMomentY}");
    }

    private static async Task<ShellResult> RunAsync(string executable, ShellOpenSeesModel model)
    {
        using var fixture = new ShellArtifactFixture();
        string scriptPath = Path.Combine(fixture.Directory, "script.tcl");
        File.WriteAllText(scriptPath, new ShellTclGenerator().Generate(model));

        OpenSeesRunResult run = await new OpenSeesProcessRunner().RunAsync(
            new OpenSeesRunRequest
            {
                ExecutablePath = executable,
                WorkingDirectory = fixture.Directory,
                ScriptPath = scriptPath,
                Timeout = TimeSpan.FromSeconds(30)
            }, CancellationToken.None);

        Assert.Equal(0, run.ExitCode);
        ShellResult result = new ShellResultParser().Parse(
            fixture.Directory, model.Elements.ToDictionary(e => e.Tag));
        Assert.Equal("completed", result.Status);
        return result;
    }
}
