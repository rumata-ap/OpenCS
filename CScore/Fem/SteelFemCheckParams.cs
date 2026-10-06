using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CScore.Sp16;

namespace CScore.Fem;

/// <summary>
/// Параметры стальной проверки по КЭ (<c>steel_check</c>), хранящиеся в самой проверке
/// (<see cref="FemCheck.ParamsJson"/>). Параметры СП 16 сечения и элемента (γc, lef, φb, гибкости …) остаются
/// у цели (<see cref="IFemCheckable.DesignParamsJson"/>); эти параметры накладываются на них при запуске
/// (<see cref="MergeInto"/>).
/// </summary>
public sealed record SteelFemCheckParams
{
    /// <summary>Признак параметров проверки (отличает их от полного набора СП 16 в старых проверках).</summary>
    public const string KindValue = "steel_fem";

    static readonly JsonSerializerOptions Opts = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Признак <see cref="KindValue"/>.</summary>
    public string Kind { get; init; } = KindValue;

    /// <summary>Расчётные длины lef = μ·l по сетке схемы; null — lef из параметров СП 16 цели.</summary>
    public SteelMeshLef? MeshLef { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    /// <summary>Параметры проверки из JSON; null — JSON не их (пусто или полный набор СП 16 старой проверки).</summary>
    public static SteelFemCheckParams? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            // Признак обязателен в самом JSON: без него это параметры СП 16 (Kind по умолчанию — признак).
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(nameof(Kind), out var kind)
                || kind.ValueKind != JsonValueKind.String || kind.GetString() != KindValue) return null;
            return JsonSerializer.Deserialize<SteelFemCheckParams>(json, Opts);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// JSON параметров СП 16 задания: параметры цели с режимом расчётных длин проверки (без параметров проверки
    /// режим снимается). Прочие поля, в том числе устаревшие, сохраняются как есть.
    /// </summary>
    public static string MergeInto(string? designParamsJson, SteelFemCheckParams? check)
    {
        string json = string.IsNullOrWhiteSpace(designParamsJson) ? "{}" : designParamsJson;
        var mesh = check?.MeshLef;
        if (mesh == null && !json.Contains(nameof(SteelDesignParams.MeshLef), StringComparison.Ordinal)) return json;
        if (JsonNode.Parse(json) is not JsonObject root) return json;
        root.Remove(nameof(SteelDesignParams.MeshLef));
        if (mesh != null) root[nameof(SteelDesignParams.MeshLef)] = JsonSerializer.SerializeToNode(mesh);
        return root.ToJsonString();
    }
}
