using System.Text.Json;
using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Проверка по КЭ: строка усилий считается с сечением своего КЭ (стержни, исполнитель-заглушка).</summary>
public class FemCheckPerElementTests
{
    static readonly CrossSection Own = new() { Id = 10, Tag = "КЭ" };
    static readonly CrossSection OfMember = new() { Id = 20, Tag = "КонЭ" };
    static readonly CrossSection OfTarget = new() { Id = 30, Tag = "Цель" };

    static FemElement Bar(string tag, string? memberTag = null, int? sectionId = null) =>
        new() { ElemTag = tag, ElemType = "beam", SourceMemberTag = memberTag, CrossSectionId = sectionId };

    static LoadItem Row(int? elem, double n = 0, int? section = 1) =>
        new() { Label = elem is int e ? $"э.{e} с{section}" : "ручная", N = n, SourceElementNum = elem, SourceSectionNum = section };

    static readonly FemCheck Check = new() { NormCode = "rc_check", Tag = "проверка" };
    static readonly FemMemberGroup Group = new() { Tag = "Балки", MemberTagsJson = "[1,2,3]" };

    /// <summary>КЭ 1 — своё сечение, КЭ 2 — сечение конструктивного элемента, КЭ 3 — сечение цели.</summary>
    static FemCheckScope Scope()
    {
        var member = new FemMember { ElemTag = "Б1", CrossSectionId = OfMember.Id };
        return new FemCheckScope([],
        [
            new FemCheckScopeElement(1, Bar("1", sectionId: Own.Id), null),
            new FemCheckScopeElement(2, Bar("2", "Б1"), member),
            new FemCheckScopeElement(3, Bar("3"), null),
        ], RefersToMeshElements: true);
    }

    static FemPerElementInputs Inputs(bool parallel = false) => new()
    {
        TargetBarSection = OfTarget,
        BarSectionById = id => id == Own.Id ? Own : id == OfMember.Id ? OfMember : null,
        ParallelBars = parallel,
        BarSelectedAsCm2 = new Dictionary<int, double> { [1] = 12.5 },
    };

    /// <summary>Коэффициент использования = N строки; сечение запоминается по метке строки.</summary>
    static Func<CalcTask, CrossSection, LoadItem, CalcResult> Executor(Dictionary<string, string> seen) =>
        (_, section, item) =>
        {
            lock (seen) seen[item.Label] = section.Tag;
            return new CalcResult
            {
                Status = item.N <= 1 ? "ok" : "not_passed",
                DataJson = JsonSerializer.Serialize(new { utilization = item.N }),
            };
        };

    static JsonElement Element(JsonElement root, int elemNum) =>
        root.GetProperty("elements").EnumerateArray().Single(e => e.GetProperty("elemNum").GetInt32() == elemNum);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachRowUsesSectionOfItsElement(bool parallel)
    {
        var fs = new ForceSet
        {
            Id = 1, Tag = "РСУ (C)",
            Items = [Row(1, 0.4), Row(1, 0.7, section: 2), Row(2, 0.5), Row(3, 0.2), Row(99, 5), Row(null, 0.1)],
        };
        var seen = new Dictionary<string, string>();

        var result = FemCheckRunner.RunPerElement(Check, Group, Scope(), [fs], Inputs(parallel), Executor(seen));

        Assert.Equal("ok", result.Status);
        // Параллельный расчёт идёт на клоне сечения — клон сохраняет имя.
        Assert.Equal("КЭ", seen["э.1 с1"]);
        Assert.Equal("КонЭ", seen["э.2 с1"]);
        Assert.Equal("Цель", seen["э.3 с1"]);
        Assert.Equal("Цель", seen["ручная"]);
        Assert.False(seen.ContainsKey("э.99 с1"));

        using var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("perElement").GetBoolean());
        Assert.Equal(5, root.GetProperty("totalRows").GetInt32());
        Assert.Equal(1, root.GetProperty("skippedRows").GetInt32());

        var e1 = Element(root, 1);
        Assert.Equal("ok", e1.GetProperty("status").GetString());
        Assert.Equal(0.7, e1.GetProperty("utilMax").GetDouble(), 6);
        Assert.Equal("э.1 с2", e1.GetProperty("label").GetString());
        Assert.Equal(2, e1.GetProperty("rows").GetInt32());
        Assert.Equal("КЭ", e1.GetProperty("sectionLabel").GetString());
        Assert.Equal(12.5, e1.GetProperty("asSelected").GetDouble(), 6);

        // Строки — не в DataJson, а в FemCheckRows (в БД пишутся отдельной таблицей).
        Assert.True(root.GetProperty("rowsStored").GetBoolean());
        Assert.False(root.TryGetProperty("rows", out _));
        var row = result.FemCheckRows!.First(r => r.Label == "э.1 с2");
        Assert.Equal(1, row.ElemNum);
        Assert.Equal(2, row.SectionNum);
    }

    [Fact]
    public void ElementWithoutForces_MakesResultIncomplete()
    {
        var fs = new ForceSet { Id = 1, Tag = "РСУ (C)", Items = [Row(1, 0.4), Row(2, 0.5)] };

        var result = FemCheckRunner.RunPerElement(Check, Group, Scope(), [fs], Inputs(), Executor([]));

        Assert.Equal("incomplete", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        var e3 = Element(doc.RootElement, 3);
        Assert.Equal("no_forces", e3.GetProperty("status").GetString());
        Assert.False(e3.GetProperty("passed").GetBoolean());
        Assert.Equal("3", doc.RootElement.GetProperty("summary").GetProperty("elementsWithoutForces").GetString());
    }

    [Fact]
    public void FailedElement_GivesNotPassed_EvenWithUncheckedOnes()
    {
        var fs = new ForceSet { Id = 1, Tag = "РСУ (C)", Items = [Row(1, 0.4), Row(2, 1.3)] };

        var result = FemCheckRunner.RunPerElement(Check, Group, Scope(), [fs], Inputs(), Executor([]));

        Assert.Equal("not_passed", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        Assert.Equal("failed", Element(doc.RootElement, 2).GetProperty("status").GetString());
        var sources = doc.RootElement.GetProperty("summary").GetProperty("sources")[0];
        Assert.Equal(1, sources.GetProperty("passed").GetInt32());
        Assert.Equal(1, sources.GetProperty("failed").GetInt32());
        Assert.Equal(1, sources.GetProperty("notChecked").GetInt32());
    }

    [Fact]
    public void ElementWithoutSection_RowsAreNotChecked()
    {
        var fs = new ForceSet { Id = 1, Tag = "РСУ (C)", Items = [Row(1, 0.4), Row(3, 0.5)] };
        var inputs = new FemPerElementInputs { BarSectionById = id => id == Own.Id ? Own : null };

        var result = FemCheckRunner.RunPerElement(Check, Group, Scope(), [fs], inputs, Executor([]));

        Assert.Equal("incomplete", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        var e3 = Element(doc.RootElement, 3);
        Assert.Equal("no_section", e3.GetProperty("status").GetString());
        Assert.Equal(1, e3.GetProperty("notChecked").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("notCheckedRows").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("failedRows").GetInt32());
    }

    [Fact]
    public void NoRowsForTarget_IsError()
    {
        var fs = new ForceSet { Id = 1, Tag = "РСУ (C)", Items = [Row(98), Row(99)] };

        var result = FemCheckRunner.RunPerElement(Check, Group, Scope(), [fs], Inputs(),
            (_, _, _) => throw new InvalidOperationException("исполнитель не должен вызываться"));

        Assert.Equal("error", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        Assert.Contains("нет строк усилий", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Cancelled_Throws()
    {
        var fs = new ForceSet { Id = 1, Tag = "РСУ (C)", Items = [Row(1, 0.4)] };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            FemCheckRunner.RunPerElement(Check, Group, Scope(), [fs], Inputs(), Executor([]), ct: cts.Token));
    }
}

/// <summary>Готовность цели к проверке по КЭ.</summary>
public class FemCheckReadinessTests
{
    static FemCheckScopeElement Shell(int num) =>
        new(num, new FemElement { ElemTag = num.ToString(), ElemType = "shell" }, null);

    static ShellLoadItem Row(int? elem) => new() { SourceElementNum = elem };

    [Fact]
    public void Evaluate_CountsCoverage()
    {
        var elements = new[] { Shell(1), Shell(2), Shell(3), Shell(5) };
        var sets = new[]
        {
            new ForceSet { Kind = "shell", ShellItems = [Row(1), Row(1), Row(2), Row(40)] },
            new ForceSet { Kind = "shell", ShellItems = [Row(null)] },
        };

        var r = FemCheckReadiness.Evaluate(true, elements, sets,
            [new FemCheckSectionProbe("assigned", e => e.ElemNum == 5 ? "КЭ не назначены ТЗА" : null)]);

        Assert.True(r.CanRun);
        Assert.False(r.IsComplete);
        Assert.Equal(4, r.ElementsTotal);
        Assert.Equal(2, r.ElementsWithForces);
        Assert.Equal([3, 5], r.ElementsWithoutForces);
        Assert.Equal(3, r.RowsForTarget);
        Assert.Equal(1, r.RowsOutsideTarget);
        Assert.Equal(1, r.RowsWithoutElement);
        Assert.Equal(1, r.SetsWithoutElementNumbers);
        var source = Assert.Single(r.Sources);
        Assert.Equal(3, source.Ready);
        Assert.Equal([5], source.NotReady);
        Assert.Equal(("КЭ не назначены ТЗА", 1), Assert.Single(source.Reasons));
    }

    [Fact]
    public void Evaluate_BlocksWhenNothingToCheck()
    {
        var elements = new[] { Shell(1) };
        var outside = new[] { new ForceSet { ShellItems = [Row(7)] } };
        var inside = new[] { new ForceSet { ShellItems = [Row(1)] } };
        FemCheckSectionProbe ok = new("section", _ => null), none = new("selected", _ => "КЭ нет в файле ASP");

        Assert.False(FemCheckReadiness.Evaluate(true, elements, outside, [ok]).CanRun);
        Assert.False(FemCheckReadiness.Evaluate(true, elements, inside, [none]).CanRun);
        Assert.False(FemCheckReadiness.Evaluate(true, elements, inside, [ok], "нет шаблона").CanRun);
        // Хотя бы один источник с армированием — проверять есть что.
        Assert.True(FemCheckReadiness.Evaluate(true, elements, inside, [none, ok]).CanRun);
        Assert.True(FemCheckReadiness.Evaluate(true, elements, inside, [ok]).IsComplete);
    }

    [Theory]
    [InlineData(new[] { 5, 1, 2, 3, 8, 10, 11 }, "1-3, 5, 8, 10-11")]
    [InlineData(new[] { 4 }, "4")]
    [InlineData(new int[0], "")]
    public void FormatRanges(int[] numbers, string expected) =>
        Assert.Equal(expected, FemCheckReadiness.FormatRanges(numbers));

    [Fact]
    public void FormatRanges_TruncatesLongLists() =>
        Assert.Equal("1, 3, …", FemCheckReadiness.FormatRanges([1, 3, 5, 7], maxRanges: 2));
}
