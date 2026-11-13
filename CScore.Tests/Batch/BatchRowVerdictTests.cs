using System.Text.Json;
using CScore.Batch;
using Xunit;

namespace CScore.Tests.Batch;

/// <summary>Формат «причины» строки пакета: определяющий критерий, порог, JSON, сводка.</summary>
public sealed class BatchRowVerdictTests
{
    [Fact]
    public void Worst_PicksMaxUtil_AndMarksNotPassedAboveOne()
    {
        var v = BatchRowVerdict.Worst(
            new BatchCriterion("eps_b", 1.17, "п. 8.1.30", "εb > εb,ult"),
            new BatchCriterion("eps_s", 0.40, "п. 8.1.30", "εs ≤ εs,ult"));
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal("eps_b", v.Code);
        Assert.Equal(1.17, v.Util);
        Assert.Equal("п. 8.1.30", v.Ref);
    }

    [Fact]
    public void Worst_ExactlyOne_IsOk()
        => Assert.Equal(BatchRowStatus.Ok, BatchRowVerdict.Worst(new BatchCriterion("acrc", 1.0, "", "")).Status);

    [Fact]
    public void Worst_InfinityBeatsFinite_AndNullLoses()
    {
        var v = BatchRowVerdict.Worst(
            new BatchCriterion("a", null, "", ""),
            new BatchCriterion("b", 5.0, "", ""),
            new BatchCriterion("c", double.PositiveInfinity, "", ""));
        Assert.Equal("c", v.Code);
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
    }

    [Fact]
    public void Worst_OnlyNullUtil_IsOk()
    {
        var v = BatchRowVerdict.Worst(new BatchCriterion("a", null, "", "t"));
        Assert.Equal(BatchRowStatus.Ok, v.Status);
        Assert.Null(v.Util);
    }

    [Fact]
    public void Json_RoundTrip_InfinityAsFlag()
    {
        var v = BatchRowVerdict.Worst(new BatchCriterion("shear", double.PositiveInfinity, "ф. 8.55", "Qb = 0"));
        string json = JsonSerializer.Serialize(new { verdict = v.ToJson() });
        Assert.DoesNotContain("Infinity", json);
        using var doc = JsonDocument.Parse(json);
        var back = BatchRowVerdict.TryRead(doc.RootElement);
        Assert.Equal(v, back);
    }

    [Fact]
    public void Json_RoundTrip_Finite()
    {
        var v = BatchRowVerdict.Worst(new BatchCriterion("acrc", 1.2345, "п. 8.2.6", "acrc = 0,37 мм"));
        string json = JsonSerializer.Serialize(new { verdict = v.ToJson() });
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(v, BatchRowVerdict.TryRead(doc.RootElement));
    }

    [Fact]
    public void TryRead_NoVerdict_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"status\":\"ok\"}");
        Assert.Null(BatchRowVerdict.TryRead(doc.RootElement));
    }

    [Theory]
    [InlineData(new[] { "ok", "not_applicable" }, "ok")]
    [InlineData(new[] { "ok", "not_passed" }, "not_passed")]
    [InlineData(new[] { "not_passed", "not_converged" }, "partial")]
    [InlineData(new[] { "ok", "error" }, "partial")]
    public void PackageStatus(string[] rows, string expected)
        => Assert.Equal(expected, BatchVerdictSummary.PackageStatus(
            rows.Select(s => new BatchRowVerdict(s, null, "", "", ""))));

    [Fact]
    public void Summary_CountsAndGoverningNum()
    {
        var v = new[]
        {
            BatchRowVerdict.Worst(new BatchCriterion("x", 0.5, "", "")),
            BatchRowVerdict.Worst(new BatchCriterion("x", 1.3, "", "")),
            BatchRowVerdict.NotConverged("нет"),
        };
        string json = JsonSerializer.Serialize(BatchVerdictSummary.ToJson(v, [10, 20, 30]));
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        Assert.Equal(1, r.GetProperty("ok").GetInt32());
        Assert.Equal(1, r.GetProperty("not_passed").GetInt32());
        Assert.Equal(1, r.GetProperty("not_converged").GetInt32());
        Assert.Equal(0, r.GetProperty("error").GetInt32());
        Assert.Equal(20, r.GetProperty("governing_num").GetInt32());
    }

    [Fact]
    public void Summary_NoUtil_GoverningNull()
    {
        var v = new[] { BatchRowVerdict.Converged() };
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(BatchVerdictSummary.ToJson(v, [1])));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("governing_num").ValueKind);
    }

    [Fact]
    public void Text_UsesRussianDecimalComma()
    {
        Assert.Equal("0,0041", BatchVerdictText.N(0.0041));
        Assert.Equal("1,200", BatchVerdictText.F3(1.2));
        Assert.Equal("∞", BatchVerdictText.F3(double.PositiveInfinity));
    }
}
