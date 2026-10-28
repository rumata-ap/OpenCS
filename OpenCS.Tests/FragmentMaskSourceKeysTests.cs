using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using OpenCS.Services;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Ключи масок по источникам (срез 3 C/D, задача 2; спека разд. 4.5): линейный CSfea — загружение стадии, секущий —
/// накопленное сочетание стадии на шаге; карта загружений родителя → фрагмента; пропуски с предупреждениями.
/// </summary>
public sealed class FragmentMaskSourceKeysTests
{
    static readonly FemLoadCase[] Parent =
    [
        new() { Id = 10, Tag = "Пост." }, new() { Id = 11, Tag = "Полезная" }, new() { Id = 12, Tag = "Ветер" },
    ];
    static readonly Dictionary<int, int> Map = new() { [10] = 110, [11] = 111 };

    static FemAnalysisStage Single(string tag, int lc) => new()
    {
        Tag = tag, LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [lc] }.ToJson(),
    };

    static FemAnalysisStage Sum(string tag, double? max, params (int Lc, double K)[] terms) => new()
    {
        Tag = tag, MaxLoadFactor = max,
        LoadExpressionJson = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = terms.Select(t => new FemLoadTerm { LoadCaseId = t.Lc, Coefficient = t.K }).ToList(),
        }.ToJson(),
    };

    static FemCsfeaStepSummary Step(int n, int stage, double lambda, bool converged = true, bool fields = true) =>
        new(n, stage, 1, lambda, false, converged, 1, 0, null, Fields: fields);

    static FemCsfeaResultSummary Summary(string kind, params FemCsfeaStepSummary[] steps) => new() { Kind = kind, Steps = [.. steps] };

    [Fact]
    public void Linear_StageOfOneLoadCase_MapsToFragmentLoadCase()
    {
        var warnings = new List<string>();
        var stages = new[] { Single("1", 10), Sum("1+2", null, (10, 1), (11, 1)), Single("3", 12), Single("2", 11), Single("1′", 10) };
        var summary = Summary(FemCsfeaRunner.LinearAnalysisKind,
            Step(1, 0, 1), Step(2, 1, 1), Step(3, 2, 1), Step(4, 3, 1, fields: false), Step(5, 4, 1));

        var keys = FragmentMaskSourceKeys.Select(summary, stages, Parent, Map, warnings);

        var k = Assert.Single(keys);
        Assert.Equal((FragmentMaskKey.OfLoadCase(110), 1, "1"), (k.Key, k.StepN, k.Label));
        Assert.Equal(4, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("«1+2»") && w.Contains("не одно загружение"));
        Assert.Contains(warnings, w => w.Contains("«Ветер»") && w.Contains("нет во фрагменте"));
        Assert.Contains(warnings, w => w.Contains("«2»") && w.Contains("нет сошедшегося шага"));
        Assert.Contains(warnings, w => w.Contains("«Пост.»") && w.Contains("повторно"));
    }

    /// <summary>
    /// Секущий: стадии накапливаются — стадия 2 на шаге λ = 0,6 несёт «Пост.» × 1 + «Полезная» × 1,5 × 0,6 = 0,9; ключ —
    /// сочетание на Id загружений фрагмента в каноническом виде (по возрастанию Id). Стадия без сошедшегося шага с полями
    /// и стадия с загружением без пары пропускаются; последующие стадии с непарным загружением в накоплении — тоже.
    /// </summary>
    [Fact]
    public void Secant_LastConvergedStepOfStage_AccumulatedExpression()
    {
        var warnings = new List<string>();
        var stages = new[] { Single("Г", 10), Sum("П", 1.5, (11, 1)) };
        var summary = Summary(FemCsfeaRunner.AnalysisKind,
            Step(1, 0, 0.5), Step(2, 0, 1), Step(3, 1, 0.3), Step(4, 1, 0.6), Step(5, 1, 0.8, converged: false));

        var keys = FragmentMaskSourceKeys.Select(summary, stages, Parent, Map, warnings);

        Assert.Equal(2, keys.Count);
        Assert.Equal(FragmentMaskSourceKeys.StageKey([(110, 1.0)]), keys[0].Key);
        Assert.Equal(2, keys[0].StepN);
        Assert.Equal(FragmentMaskKeyKind.Stage, keys[1].Key.Kind);
        var terms = FemLoadExpression.Parse(keys[1].Key.StageExpressionJson).Terms;
        Assert.Equal([110, 111], terms.Select(t => t.LoadCaseId));
        Assert.Equal(1.0, terms[0].Coefficient, 12);
        Assert.Equal(0.9, terms[1].Coefficient, 12);
        Assert.Equal(4, keys[1].StepN);
        Assert.Contains(warnings, w => w.Contains("«П»") && w.Contains($"λ = {0.6.ToString(System.Globalization.CultureInfo.CurrentCulture)}"));
        Assert.Equal(FragmentMaskSourceKeys.StageKey([(111, 0.9), (110, 1.0)]), keys[1].Key);

        warnings.Clear();
        var noPair = FragmentMaskSourceKeys.Select(Summary(FemCsfeaRunner.AnalysisKind, Step(1, 0, 1), Step(2, 1, 1)),
            [Single("В", 12), Single("Г", 10)], Parent, Map, warnings);
        Assert.Empty(noPair);
        Assert.Contains(warnings, w => w.Contains("«В»") && w.Contains("«Ветер»"));
        Assert.Contains(warnings, w => w.Contains("«Г»") && w.Contains("«Ветер»"));

        warnings.Clear();
        var notConverged = FragmentMaskSourceKeys.Select(
            Summary(FemCsfeaRunner.AnalysisKind, Step(1, 0, 0.5, converged: false), Step(2, 1, 1)),
            [Single("Г", 10), Single("П", 11)], Parent, Map, warnings);
        Assert.Equal([2], notConverged.Select(k => k.StepN));
        Assert.Equal(FragmentMaskSourceKeys.StageKey([(110, 1.0), (111, 1.0)]), notConverged[0].Key);
        Assert.Contains(warnings, w => w.Contains("«Г»") && w.Contains("нет сошедшегося шага"));
    }

    [Fact]
    public void Batch_NotSupported()
    {
        var warnings = new List<string>();
        var summary = Summary(FemCsfeaRunner.AnalysisKind, Step(1, 0, 1));
        summary.Batch = [];
        Assert.Empty(FragmentMaskSourceKeys.Select(summary, [Single("Г", 10)], Parent, Map, warnings));
        Assert.Contains(warnings, w => w.Contains("пакет"));
    }

    [Fact]
    public void LoadCaseMap_ByOrderThenByUniqueTag()
    {
        var warnings = new List<string>();
        FemLoadCase[] fragment = [new() { Id = 110, Tag = "Пост." }, new() { Id = 111, Tag = "Полезная" }, new() { Id = 112, Tag = "Ветер" }];
        Assert.Equal(new Dictionary<int, int> { [10] = 110, [11] = 111, [12] = 112 },
            FragmentMaskSourceKeys.LoadCaseMap(Parent, fragment, warnings));
        Assert.Empty(warnings);

        // Порядок другой, «Ветер» удалён во фрагменте, у «Пост.» два двойника — по тегу только однозначные.
        FemLoadCase[] edited = [new() { Id = 120, Tag = "Полезная" }, new() { Id = 121, Tag = "Пост." }, new() { Id = 122, Tag = "Пост." }];
        Assert.Equal(new Dictionary<int, int> { [11] = 120 }, FragmentMaskSourceKeys.LoadCaseMap(Parent, edited, warnings));
        Assert.Contains(warnings, w => w.Contains("«Пост.»") && w.Contains("неоднозначно"));
        Assert.Contains(warnings, w => w.Contains("«Ветер»") && w.Contains("нет во фрагменте"));
    }
}
