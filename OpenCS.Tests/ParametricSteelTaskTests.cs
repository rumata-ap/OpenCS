using System.Text.Json;
using CScore;
using CScore.ParametricSteel;
using CScore.Sp16;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Явный профиль параметрического МК-сечения в задачах СП 16 (срез 3).</summary>
public sealed class ParametricSteelTaskTests
{
    const string Kind = "steel_central_compression";

    /// <summary>Гнутый швеллер 200×80×6: распознаватель его не узнаёт (Generic), параметрика — Channel Bent.</summary>
    static CrossSection BentChannel(bool bind = true)
    {
        var definition = ParametricSteelSectionDefinition.BentChannel(0.20, 0.08, 0.006, 0.009) with { MaterialId = 1 };
        var generated = ParametricSteelSectionGenerator.Generate(definition);
        Assert.Empty(generated.Diagnostics);
        var section = generated.Section;
        section.Areas[0].Material = new Material
        {
            Id = 1, Type = MatType.Steel, E = 2.06e8,
            MaterialChars = [new MaterialChars(CalcType.C) { Ry = 240000, Ru = 360000 }]
        };
        if (bind)
            section.ParametricSteel = new ParametricSteelBinding(generated.Profile!,
                ParametricSteelSectionFingerprint.Compute(section, ParametricSteelSectionGenerator.GeneratorVersion),
                ParametricSteelSectionGenerator.GeneratorVersion);
        return section;
    }

    static CalcResult Run(CrossSection section, string? paramsJson = null) =>
        TaskRunner.Run(new CalcTask { Kind = Kind, ParamsJson = paramsJson ?? "" }, section, new LoadItem { N = -100 });

    static string[] Notes(JsonElement root) =>
        root.GetProperty("notes").EnumerateArray().Select(n => n.GetString()!).ToArray();

    static JsonElement[] Details(JsonElement root, string clausePrefix) =>
        root.GetProperty("details").EnumerateArray()
            .Where(d => d.GetProperty("clause").GetString()!.StartsWith(clausePrefix)).ToArray();

    static bool Applicable(JsonElement detail) => detail.GetProperty("status").GetString() != nameof(CheckStatus.NotApplicable);

    static bool HasNote(JsonElement detail, string text) =>
        detail.GetProperty("notes").EnumerateArray().Any(n => n.GetString()!.Contains(text));

    [Fact]
    public void BoundBentChannelUsesExplicitProfile()
    {
        var result = Run(BentChannel());

        Assert.NotEqual("error", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;
        Assert.Equal(Loc.S("Sp16ProfileFromParametric"), Notes(root)[0]);
        Assert.DoesNotContain(Notes(root), n => n.Contains("не распознан"));
        Assert.Contains(Details(root, "7.1.3"), d => HasNote(d, "тип сечения c (табл. 7)"));
        Assert.Contains(Details(root, "7.3"), Applicable);
    }

    [Fact]
    public void UnboundContourFallsBackToRecognition()
    {
        var result = Run(BentChannel(bind: false));

        using var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;
        Assert.DoesNotContain(Loc.S("Sp16ProfileFromParametric"), Notes(root));
        Assert.Contains(Notes(root), n => n.Contains("не распознан"));
        Assert.Contains(Details(root, "7.1.3"), d => HasNote(d, "не приведено в табл. 7"));
        Assert.DoesNotContain(Details(root, "7.3"), Applicable);
    }

    [Fact]
    public void EditedContourDropsParametricProfile()
    {
        var section = BentChannel();
        var hull = section.Areas[0].Hull!;
        hull.X[0] += 1e-4;
        Assert.Null(section.TryGetParametricSteelProfile());

        var result = Run(section);

        using var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;
        Assert.DoesNotContain(Loc.S("Sp16ProfileFromParametric"), Notes(root));
        Assert.Contains(Notes(root), n => n.Contains("не распознан"));
    }

    [Fact]
    public void ProfileInParamsJsonOverridesBinding()
    {
        var explicitProfile = new SteelProfile
        {
            Kind = SteelProfileKind.Generic
        };
        string json = new SteelDesignParams { Profile = explicitProfile }.ToJson();

        var result = Run(BentChannel(), json);

        using var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;
        Assert.DoesNotContain(Loc.S("Sp16ProfileFromParametric"), Notes(root));
        Assert.Contains(Details(root, "7.1.3"), d => HasNote(d, "не приведено в табл. 7"));
    }

    [Fact]
    public void MemberCacheDistinguishesBoundAndUnboundSections()
    {
        // Один и тот же ParamsJson и контур: кэш элемента не должен отдавать профиль другой привязки.
        var bound = Run(BentChannel());
        var unbound = Run(BentChannel(bind: false));
        var boundAgain = Run(BentChannel());

        using var a = JsonDocument.Parse(bound.DataJson);
        using var b = JsonDocument.Parse(unbound.DataJson);
        using var c = JsonDocument.Parse(boundAgain.DataJson);
        Assert.Contains(Details(a.RootElement, "7.3"), Applicable);
        Assert.DoesNotContain(Details(b.RootElement, "7.3"), Applicable);
        Assert.Contains(Details(c.RootElement, "7.3"), Applicable);
    }
}
