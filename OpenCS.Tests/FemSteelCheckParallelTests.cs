using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.ParametricSteel;
using CScore.Sp16;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Стальная проверка по КЭ (steel_check) считается параллельно на общем сечении: результат совпадает с последовательным
/// побайтно, привязка параметрического профиля сохраняется (у клона сечения её нет).
/// </summary>
public sealed class FemSteelCheckParallelTests
{
    const int Elements = 60;

    /// <summary>Гнутый швеллер 200×80×6 с привязкой параметрического профиля: без неё профиль не распознаётся.</summary>
    static CrossSection BentChannel()
    {
        var definition = ParametricSteelSectionDefinition.BentChannel(0.20, 0.08, 0.006, 0.009) with { MaterialId = 1 };
        var generated = ParametricSteelSectionGenerator.Generate(definition);
        var section = generated.Section;
        section.Id = 7;
        section.Areas[0].Material = new Material
        {
            Id = 1, Type = MatType.Steel, E = 2.06e8,
            MaterialChars = [new MaterialChars(CalcType.C) { Ry = 240000, Ru = 360000 }],
        };
        section.ParametricSteel = new ParametricSteelBinding(generated.Profile!,
            ParametricSteelSectionFingerprint.Compute(section, ParametricSteelSectionGenerator.GeneratorVersion),
            ParametricSteelSectionGenerator.GeneratorVersion);
        return section;
    }

    static readonly FemCheck Check = new()
    {
        NormCode = "steel_check", Tag = "Сталь",
        ParamsJson = new SteelDesignParams { LefX = 3.0, LefY = 1.5 }.ToJson(),
    };

    static readonly FemMemberGroup Group = new() { Tag = "Стойки" };

    static FemCheckScope Scope(int sectionId) => new([],
        [.. Enumerable.Range(1, Elements).Select(n => new FemCheckScopeElement(n,
            new FemElement { ElemTag = n.ToString(), ElemType = "beam", CrossSectionId = sectionId }, null))],
        RefersToMeshElements: true);

    /// <summary>По две строки на КЭ (концы), сжатие с изгибом, у части КЭ — с превышением.</summary>
    static ForceSet Set()
    {
        var fs = new ForceSet { Id = 1, Tag = "РСУ (C)", Kind = "bar" };
        for (int n = 1; n <= Elements; n++)
            for (int s = 1; s <= 2; s++)
                fs.Items.Add(new LoadItem
                {
                    Num = fs.Items.Count + 1, Label = $"{n}-{s}", N = -20 - 3 * n, Mx = 0.2 * n * s, My = 0.05 * (n % 5),
                    Vy = 1 + s, SourceElementNum = n, SourceSectionNum = s,
                });
        return fs;
    }

    static (string Data, string Rows) Run(bool parallel, bool shared)
    {
        var section = BentChannel();
        var result = FemCheckRunner.RunPerElement(Check, Group, Scope(section.Id), [Set()], new FemPerElementInputs
        {
            BarSectionById = id => id == section.Id ? section : null,
            ParallelBars = parallel,
            SharedBarSection = shared,
        }, (task, sect, item) => TaskRunner.Run(task, sect, item));
        Assert.Equal(2 * Elements, result.FemCheckRows!.Count);
        Assert.All(result.FemCheckRows, r => Assert.False(r.NotChecked, $"{r.Label}: {r.WorstDescription}"));
        return (result.DataJson, JsonSerializer.Serialize(result.FemCheckRows));
    }

    [Fact]
    public void Parallel_OnSharedSection_EqualsSequential()
    {
        var sequential = Run(parallel: false, shared: false);
        var parallel = Run(parallel: true, shared: true);

        Assert.Equal(sequential.Data, parallel.Data);
        Assert.Equal(sequential.Rows, parallel.Rows);
        Assert.Contains("\"failed\"", sequential.Data);   // в наборе есть и непроходящие КЭ

        // Клон сечения теряет привязку профиля — потому сталь и считается на общем сечении.
        var cloned = Run(parallel: true, shared: false);
        Assert.NotEqual(sequential.Rows, cloned.Rows);
    }
}
