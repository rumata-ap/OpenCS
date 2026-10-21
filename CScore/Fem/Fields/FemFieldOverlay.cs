using System.Globalization;

namespace CScore.Fem.Fields;

/// <summary>Поле со своими вычисленными значениями — слой наложения.</summary>
public sealed record FemFieldLayer(FemField Field, IReadOnlyList<FemFieldValue> Values);

/// <summary>Ключ значения: вид цели, тег, компонента.</summary>
public readonly record struct FemFieldValueKey(FemFieldTargetKind Kind, string Tag, string Component);

/// <summary>Эффективные КЭ и пружины и источник каждого переопределённого значения.</summary>
public sealed record FemFieldOverlayResult(
    IReadOnlyList<FemElement> Elements,
    IReadOnlyList<FemSpring> Springs,
    IReadOnlyDictionary<FemFieldValueKey, string> Sources,
    IReadOnlyList<FieldDiagnostic> Diagnostics);

/// <summary>
/// Наложение слоёв величин: импорт (сырые КЭ и пружины) → поля по порядку (последнее выигрывает) → ручные правки.
/// Пружины — по DOF: поле Rz меняет только Kz, Kx/Ky импорта сохраняются. На узлах с ручной пружиной
/// (<see cref="FemLoadOrigin.Manual"/>) поля не действуют — ручное важнее. Входные объекты не меняются.
/// </summary>
public static class FemFieldOverlay
{
    /// <summary>Происхождение пружины, созданной полем на узле без пружины.</summary>
    public const string FieldOrigin = "field";
    public const string ManualSource = "manual";

    public static string FieldSource(int fieldId) => "field:" + fieldId.ToString(CultureInfo.InvariantCulture);

    public static bool TryFieldId(string source, out int fieldId)
    {
        fieldId = 0;
        return source.StartsWith("field:", StringComparison.Ordinal)
            && int.TryParse(source.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out fieldId);
    }

    public static FemFieldOverlayResult Apply(IReadOnlyList<FemElement> elements, IReadOnlyList<FemSpring> springs,
        IReadOnlyList<FemFieldLayer> layers, IReadOnlyList<FemParamOverride> overrides)
    {
        var c1 = new Dictionary<string, (double V, string Src)>(StringComparer.Ordinal);
        var k = new Dictionary<string, (double?[] V, string?[] Src)>(StringComparer.Ordinal);
        var manualNodes = springs.Where(s => s.TargetKind == FemSpringTargetKinds.MeshNode && s.Origin == FemLoadOrigin.Manual)
            .Select(s => s.NodeTag).ToHashSet(StringComparer.Ordinal);
        var negative = new SortedSet<string>(StringComparer.Ordinal);
        var skippedManual = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var layer in layers.Where(l => l.Field.Enabled).OrderBy(l => l.Field.SortOrder).ThenBy(l => l.Field.Id))
        {
            string src = FieldSource(layer.Field.Id);
            bool allowNegative = FemFieldQuantities.Get(layer.Field.Quantity).AllowNegative;
            foreach (var v in layer.Values)
            {
                if (v.Value is not double value) continue;
                if (value < 0 && !allowNegative) { negative.Add(v.TargetTag); continue; }
                if (layer.Field.Quantity == FemFieldQuantities.FoundationC1) c1[v.TargetTag] = (value, src);
                else if (layer.Field.Quantity == FemFieldQuantities.Spring)
                {
                    if (manualNodes.Contains(v.TargetTag)) { skippedManual.Add(v.TargetTag); continue; }
                    SetSpring(k, v.TargetTag, v.Component, value, src);
                }
            }
        }
        foreach (var o in overrides)
        {
            if (o.Quantity == FemFieldQuantities.FoundationC1) c1[o.TargetTag] = (o.Value, ManualSource);
            else if (o.Quantity == FemFieldQuantities.Spring) SetSpring(k, o.TargetTag, o.Component, o.Value, ManualSource);
        }

        var sources = new Dictionary<FemFieldValueKey, string>();
        var outElements = new List<FemElement>(elements.Count);
        foreach (var e in elements)
        {
            if (e.ElemType == "shell" && c1.TryGetValue(e.ElemTag, out var c))
            {
                var copy = e.ShallowCopy();
                copy.FoundationC1 = c.V;
                outElements.Add(copy);
                sources[new(FemFieldTargetKind.Shell, e.ElemTag, "c1")] = c.Src;
            }
            else outElements.Add(e);
        }

        var outSprings = new List<FemSpring>(springs.Count + k.Count);
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in springs)
        {
            if (s.TargetKind == FemSpringTargetKinds.MeshNode && k.TryGetValue(s.NodeTag, out var kv) && done.Add(s.NodeTag))
                outSprings.Add(Merge(Copy(s), s.NodeTag, kv, sources));
            else outSprings.Add(s);
        }
        foreach (var (tag, kv) in k)
            if (!done.Contains(tag))
                outSprings.Add(Merge(new FemSpring { TargetKind = FemSpringTargetKinds.MeshNode, NodeTag = tag, Origin = FieldOrigin },
                    tag, kv, sources));

        var diags = new List<FieldDiagnostic>();
        if (negative.Count > 0)
            diags.Add(new("field_negative", "Отрицательные значения полей не учитываются; цели", negative.ToList()));
        if (skippedManual.Count > 0)
            diags.Add(new("field_manual_spring", "Поля не действуют на узлы с ручными пружинами; узлы", skippedManual.ToList()));
        return new FemFieldOverlayResult(outElements, outSprings, sources, diags);
    }

    static void SetSpring(Dictionary<string, (double?[] V, string?[] Src)> k, string node, string component, double value, string src)
    {
        int dof = Array.IndexOf(FemFieldQuantities.SpringComponents, component);
        if (dof < 0) return;
        if (!k.TryGetValue(node, out var kv)) k[node] = kv = (new double?[6], new string?[6]);
        kv.V[dof] = value;
        kv.Src[dof] = src;
    }

    static FemSpring Merge(FemSpring spring, string node, (double?[] V, string?[] Src) kv, Dictionary<FemFieldValueKey, string> sources)
    {
        var stiff = spring.Stiffnesses;
        for (int i = 0; i < 6; i++)
            if (kv.V[i] is double d)
            {
                stiff[i] = d;
                sources[new(FemFieldTargetKind.MeshNode, node, FemFieldQuantities.SpringComponents[i])] = kv.Src[i]!;
            }
        spring.SetStiffnesses(stiff);
        return spring;
    }

    static FemSpring Copy(FemSpring s) => new()
    {
        Id = s.Id, SchemaId = s.SchemaId, TargetKind = s.TargetKind, NodeTag = s.NodeTag,
        Kx = s.Kx, Ky = s.Ky, Kz = s.Kz, Kux = s.Kux, Kuy = s.Kuy, Kuz = s.Kuz,
        Origin = s.Origin, SourceElemTag = s.SourceElemTag,
    };
}
