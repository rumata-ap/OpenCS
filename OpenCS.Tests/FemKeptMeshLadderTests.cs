using CScore.Fem;
using CScore.Fem.Fragment;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Оркестратор лестницы перемешивания сохранённой сетки (срез 4) на подставных сборках.</summary>
public sealed class FemKeptMeshLadderTests
{
    static readonly List<FemMember> Members =
    [
        new() { ElemTag = "P1", MeshSource = FemMember.MeshSourceImported },
        new() { ElemTag = "P2", MeshSource = FemMember.MeshSourceImported },
        new() { ElemTag = "W1" },
    ];

    static FemSchemaMeshBuildResult Faulty(params string[] members) => new()
    {
        Diagnostics = [new("planar_materialize_imported_hanging_node", $"стык {string.Join(",", members)}", true, members)],
        ImportedMembersAtFault = members.ToHashSet(StringComparer.Ordinal),
    };

    static FemSchemaMeshBuildResult Result(IEnumerable<string> unlocked, int step, bool errors)
    {
        var plan = new FemKeptMeshUnlockPlan();
        foreach (var t in unlocked) plan.UnlockedMemberTags.Add(t);
        return new FemSchemaMeshBuildResult
        {
            Diagnostics = errors ? [new("planar_mesh_failed", "ошибка", true)] : [],
            Unlock = plan, LadderStep = step,
        };
    }

    [Fact]
    public async Task NoFaults_SingleBuild()
    {
        int calls = 0;
        var first = new FemSchemaMeshBuildResult();
        var result = await FemKeptMeshLadder.RunAsync(first, Members, (_, _, _) => { calls++; return Task.FromResult<FemSchemaMeshBuildResult?>(null); });
        Assert.Same(first, result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Step1Succeeds_OnlyFaultyUnlocked()
    {
        var steps = new List<(int Step, string Tags, string Reason)>();
        var result = await FemKeptMeshLadder.RunAsync(Faulty("P1"), Members, (tags, step, reason) =>
        {
            steps.Add((step, string.Join(",", tags), reason));
            return Task.FromResult<FemSchemaMeshBuildResult?>(Result(tags, step, errors: false));
        });

        Assert.Equal([(1, "P1", "стык P1")], steps);
        Assert.Equal(1, result.LadderStep);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public async Task Step1Fails_Step2UnlocksAllLocked()
    {
        var steps = new List<(int, string)>();
        var result = await FemKeptMeshLadder.RunAsync(Faulty("P1"), Members, (tags, step, _) =>
        {
            steps.Add((step, string.Join(",", tags)));
            return Task.FromResult<FemSchemaMeshBuildResult?>(Result(tags, step, errors: step == 1));
        });

        Assert.Equal([(1, "P1"), (2, "P1,P2")], steps);
        Assert.Equal(2, result.LadderStep);
        Assert.False(result.HasErrors);
    }

    /// <summary>Шаг 1 назвал новый виновный КонЭ с замком — шаг 1 повторяется с ним, до шага 2 не доходит.</summary>
    [Fact]
    public async Task Step1NewFaulty_Step1Repeated()
    {
        var members = Members.Append(new FemMember { ElemTag = "P3", MeshSource = FemMember.MeshSourceImported }).ToList();
        var steps = new List<(int, string)>();
        var result = await FemKeptMeshLadder.RunAsync(Faulty("P1"), members, (tags, step, _) =>
        {
            steps.Add((step, string.Join(",", tags)));
            if (tags.Count == 1)
            {
                var r = Faulty("P2", "W1");
                return Task.FromResult<FemSchemaMeshBuildResult?>(new FemSchemaMeshBuildResult
                {
                    Diagnostics = r.Diagnostics, ImportedMembersAtFault = r.ImportedMembersAtFault, Unlock = Result(tags, step, true).Unlock,
                    LadderStep = step,
                });
            }
            return Task.FromResult<FemSchemaMeshBuildResult?>(Result(tags, step, errors: false));
        });

        Assert.Equal([(1, "P1"), (1, "P1,P2")], steps);
        Assert.Equal(1, result.LadderStep);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public async Task Step2Fails_ErrorsReturned()
    {
        var result = await FemKeptMeshLadder.RunAsync(Faulty("P1"), Members, (tags, step, _) =>
            Task.FromResult<FemSchemaMeshBuildResult?>(Result(tags, step, errors: true)));

        Assert.Equal(2, result.LadderStep);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public async Task AllLockedFaulty_NoRepeatedStep2()
    {
        var steps = new List<int>();
        var result = await FemKeptMeshLadder.RunAsync(Faulty("P1", "P2"), Members, (tags, step, _) =>
        {
            steps.Add(step);
            return Task.FromResult<FemSchemaMeshBuildResult?>(Result(tags, step, errors: true));
        });

        Assert.Equal([1], steps);
        Assert.Equal(1, result.LadderStep);
    }

    [Fact]
    public async Task NothingToUnlock_FirstReturned()
    {
        var first = Faulty("P1");
        var result = await FemKeptMeshLadder.RunAsync(first, Members, (_, _, _) => Task.FromResult<FemSchemaMeshBuildResult?>(null));
        Assert.Same(first, result);
    }
}
