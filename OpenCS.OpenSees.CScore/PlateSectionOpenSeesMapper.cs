using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Строит backend-независимый LayeredShell snapshot из плитного сечения CScore.</summary>
public static class PlateSectionOpenSeesMapper
{
    private const double ZTolerance = 1e-12;

    /// <summary>Выполняет mapping бетона и явных smeared-слоёв арматуры.</summary>
    public static PlateSectionShellMappingResult Map(
        PlateSection section,
        ShellFrame frame,
        IPlateSectionShellMaterialResolver resolver,
        int sectionTag = 1)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(resolver);

        if (sectionTag <= 0)
            throw new CScoreMappingException("Tag shell-секции должен быть положительным.");
        if (!double.IsFinite(section.H) || section.H <= 0)
            throw new CScoreMappingException("Толщина PlateSection должна быть положительной и конечной.");
        if (section.NLayers < 1)
            throw new CScoreMappingException("NLayers PlateSection должен быть не меньше 1.");

        try
        {
            frame.Validate();
        }
        catch (Exception exception) when (exception is InvalidOperationException)
        {
            throw new CScoreMappingException("Локальный frame PlateSection не прошёл validation.", exception);
        }

        NativeShellMaterialDefinition concrete = ResolveMaterial(
            () => resolver.ResolveConcrete(section.ConcreteMaterialId), "бетона");
        concrete.Validate();

        var materials = new List<NativeShellMaterialDefinition>();
        var byFingerprint = new Dictionary<string, NativeShellMaterialDefinition>(StringComparer.Ordinal);
        var usedTags = new HashSet<int>();
        NativeShellMaterialDefinition concreteRegistered = Register(
            concrete, byFingerprint, usedTags, materials, concrete.Tag);

        int nextMaterialTag = usedTags.Count == 0 ? 1 : usedTags.Max() + 1;
        int nlayers = section.NLayers;
        double layerThickness = section.H / nlayers;
        var layers = new List<RCShellLayer>(nlayers + section.RebarLayers.Count * 2);

        for (int i = 0; i < nlayers; i++)
        {
            double centerZ = -section.H / 2.0 + (i + 0.5) * layerThickness;
            layers.Add(new RCShellLayer(
                i,
                ShellLayerKind.Concrete,
                centerZ,
                layerThickness,
                concreteRegistered.Tag,
                0,
                $"concrete:{section.ConcreteMaterialId}:{i}"));
        }

        bool hasRebar = false;
        var diagnostics = new List<string>();
        for (int sourceIndex = 0; sourceIndex < section.RebarLayers.Count; sourceIndex++)
        {
            PlateRebarLayer source = section.RebarLayers[sourceIndex];
            ValidateRebarCoordinate(source.Zsx, section.H, sourceIndex, "Zsx");
            ValidateRebarCoordinate(source.Zsy, section.H, sourceIndex, "Zsy");
            ValidateArea(source.Asx, sourceIndex, "Asx");
            ValidateArea(source.Asy, sourceIndex, "Asy");

            if (source.Asx > 0)
            {
                hasRebar = true;
                int sourceMaterialId = source.MaterialId != 0
                    ? source.MaterialId : section.RebarMaterialId;
                NativeShellMaterialDefinition baseDefinition = ResolveMaterial(
                    () => resolver.ResolveRebar(sourceMaterialId), "арматуры");
                NativeShellMaterialDefinition oriented = Orient(
                    baseDefinition, 0, nextMaterialTag, byFingerprint, usedTags, materials);
                nextMaterialTag = Math.Max(nextMaterialTag, oriented.Tag + 1);
                layers.Add(new RCShellLayer(
                    0, ShellLayerKind.RebarX, source.Zsx, source.Asx, oriented.Tag, 0,
                    $"rebar:{sourceIndex}:x"));
            }

            if (source.Asy > 0)
            {
                hasRebar = true;
                int sourceMaterialId = source.MaterialId != 0
                    ? source.MaterialId : section.RebarMaterialId;
                NativeShellMaterialDefinition baseDefinition = ResolveMaterial(
                    () => resolver.ResolveRebar(sourceMaterialId), "арматуры");
                NativeShellMaterialDefinition oriented = Orient(
                    baseDefinition, 90, nextMaterialTag, byFingerprint, usedTags, materials);
                nextMaterialTag = Math.Max(nextMaterialTag, oriented.Tag + 1);
                layers.Add(new RCShellLayer(
                    0, ShellLayerKind.RebarY, source.Zsy, source.Asy, oriented.Tag, 90,
                    $"rebar:{sourceIndex}:y"));
            }
        }

        ShellMappingMode mode = hasRebar
            ? ShellMappingMode.NativeWithExplicitApproximation
            : ShellMappingMode.Exact;
        if (hasRebar)
            diagnostics.Add("Smeared-арматура задана отдельными native слоями; точное сохранение z-координаты LayeredShell требует отдельной capability-проверки.");

        var orderedLayers = layers
            .OrderBy(layer => layer.CenterZ)
            .ThenBy(layer => LayerOrder(layer.Kind))
            .ThenBy(layer => layer.SourceId, StringComparer.Ordinal)
            .Select((layer, index) => layer with { Index = index })
            .ToArray();

        string sourceFingerprint = Fingerprint(
            "PlateSection",
            section.Id.ToString(CultureInfo.InvariantCulture),
            section.H.ToString("G17", CultureInfo.InvariantCulture),
            section.NLayers.ToString(CultureInfo.InvariantCulture),
            section.ConcreteMaterialId.ToString(CultureInfo.InvariantCulture),
            section.RebarMaterialId.ToString(CultureInfo.InvariantCulture),
            string.Join(";", section.RebarLayers.Select(layer => string.Join(",",
                layer.Asx.ToString("G17", CultureInfo.InvariantCulture),
                layer.Asy.ToString("G17", CultureInfo.InvariantCulture),
                layer.Zsx.ToString("G17", CultureInfo.InvariantCulture),
                layer.Zsy.ToString("G17", CultureInfo.InvariantCulture),
                layer.MaterialId.ToString(CultureInfo.InvariantCulture)))));
        string sectionFingerprint = Fingerprint(
            sourceFingerprint,
            FrameFingerprint(frame),
            mode.ToString(),
            string.Join(";", orderedLayers.Select(layer => string.Join(",",
                layer.Index,
                layer.Kind,
                layer.CenterZ.ToString("G17", CultureInfo.InvariantCulture),
                layer.Thickness.ToString("G17", CultureInfo.InvariantCulture),
                layer.MaterialTag,
                layer.DirectionDegrees.ToString("G17", CultureInfo.InvariantCulture),
                layer.SourceId))),
            string.Join(";", materials.OrderBy(material => material.Tag).Select(material =>
                $"{material.Tag}:{material.Fingerprint}")));

        var resultSection = new RCShellLayeredSection(
            sectionTag,
            sourceFingerprint,
            section.H,
            frame,
            orderedLayers,
            mode,
            diagnostics,
            sectionFingerprint);
        resultSection.Validate();
        return new PlateSectionShellMappingResult(resultSection, materials, diagnostics);
    }

    private static NativeShellMaterialDefinition ResolveMaterial(
        Func<NativeShellMaterialDefinition> resolver,
        string kind)
    {
        try
        {
            return resolver() ?? throw new CScoreMappingException($"Не разрешён material {kind}.");
        }
        catch (CScoreMappingException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CScoreMappingException($"Не удалось разрешить material {kind}.", exception);
        }
    }

    private static NativeShellMaterialDefinition Register(
        NativeShellMaterialDefinition definition,
        Dictionary<string, NativeShellMaterialDefinition> byFingerprint,
        HashSet<int> usedTags,
        List<NativeShellMaterialDefinition> materials,
        int preferredTag)
    {
        if (byFingerprint.TryGetValue(definition.Fingerprint, out NativeShellMaterialDefinition? existing))
            return existing;

        int tag = preferredTag > 0 && !usedTags.Contains(preferredTag)
            ? preferredTag
            : NextTag(usedTags);
        NativeShellMaterialDefinition registered = definition with { Tag = tag };
        registered.Validate();
        usedTags.Add(tag);
        byFingerprint.Add(registered.Fingerprint, registered);
        materials.Add(registered);
        return registered;
    }

    private static NativeShellMaterialDefinition Orient(
        NativeShellMaterialDefinition definition,
        double direction,
        int preferredTag,
        Dictionary<string, NativeShellMaterialDefinition> byFingerprint,
        HashSet<int> usedTags,
        List<NativeShellMaterialDefinition> materials)
    {
        NativeShellMaterialDefinition oriented = definition.Spec is PlateRebarShellMaterialSpec plateRebar
            ? definition with { Spec = plateRebar with { AngleDegrees = direction } }
            : definition;
        return Register(oriented, byFingerprint, usedTags, materials, preferredTag);
    }

    private static int NextTag(HashSet<int> usedTags)
    {
        int tag = 1;
        while (usedTags.Contains(tag)) tag++;
        return tag;
    }

    private static int LayerOrder(ShellLayerKind kind) => kind switch
    {
        ShellLayerKind.Concrete => 0,
        ShellLayerKind.RebarX => 1,
        ShellLayerKind.RebarY => 2,
        _ => 3
    };

    private static void ValidateArea(double value, int index, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new CScoreMappingException($"Арматурный слой {index}: {name} должен быть конечным и неотрицательным.");
    }

    private static void ValidateRebarCoordinate(double value, double thickness, int index, string name)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > thickness / 2.0 + ZTolerance)
            throw new CScoreMappingException($"Арматурный слой {index}: {name} выходит за физическую толщину PlateSection.");
    }

    private static string FrameFingerprint(ShellFrame frame) => string.Join(",",
        frame.Ex.X.ToString("G17", CultureInfo.InvariantCulture),
        frame.Ex.Y.ToString("G17", CultureInfo.InvariantCulture),
        frame.Ex.Z.ToString("G17", CultureInfo.InvariantCulture),
        frame.Ey.X.ToString("G17", CultureInfo.InvariantCulture),
        frame.Ey.Y.ToString("G17", CultureInfo.InvariantCulture),
        frame.Ey.Z.ToString("G17", CultureInfo.InvariantCulture),
        frame.Normal.X.ToString("G17", CultureInfo.InvariantCulture),
        frame.Normal.Y.ToString("G17", CultureInfo.InvariantCulture),
        frame.Normal.Z.ToString("G17", CultureInfo.InvariantCulture));

    private static string Fingerprint(params string[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)))).ToLowerInvariant();
}
