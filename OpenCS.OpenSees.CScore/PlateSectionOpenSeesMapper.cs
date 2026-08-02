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
        var byFingerprint = new Dictionary<string, NativeShellMaterialDefinition>(StringComparer.Ordinal);
        var usedTags = new HashSet<int>();
        var materials = new List<NativeShellMaterialDefinition>();

        RCShellLayeredSection resultSection = BuildSection(
            section, frame, sectionTag, resolver, byFingerprint, usedTags, materials, out List<string> diagnostics);

        return new PlateSectionShellMappingResult(resultSection, materials, diagnostics);
    }

    /// <summary>Выполняет mapping нескольких PlateSection с ОДНИМ разделяемым регистром
    /// материалов — корректный глобальный дедуп/нумерация тегов при нескольких секциях
    /// в одной shell-модели (в отличие от нескольких независимых вызовов Map).</summary>
    public static PlateSectionShellMappingResultBatch MapMany(
        IReadOnlyList<(PlateSection Section, ShellFrame Frame, int SectionTag)> requests,
        IPlateSectionShellMaterialResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(resolver);
        if (requests.Count == 0)
            throw new CScoreMappingException("MapMany требует хотя бы один запрос.");

        var byFingerprint = new Dictionary<string, NativeShellMaterialDefinition>(StringComparer.Ordinal);
        var usedTags = new HashSet<int>();
        var materials = new List<NativeShellMaterialDefinition>();
        var sections = new List<RCShellLayeredSection>(requests.Count);
        var diagnostics = new List<string>();
        var usedSectionTags = new HashSet<int>();

        foreach (var request in requests)
        {
            if (!usedSectionTags.Add(request.SectionTag))
                throw new CScoreMappingException($"Дублирующийся tag shell-секции {request.SectionTag} в MapMany.");

            RCShellLayeredSection built = BuildSection(
                request.Section, request.Frame, request.SectionTag, resolver,
                byFingerprint, usedTags, materials, out List<string> requestDiagnostics);
            sections.Add(built);

            foreach (string diagnostic in requestDiagnostics)
                if (!diagnostics.Contains(diagnostic, StringComparer.Ordinal))
                    diagnostics.Add(diagnostic);
        }

        return new PlateSectionShellMappingResultBatch(sections, materials, diagnostics);
    }

    private static RCShellLayeredSection BuildSection(
        PlateSection section,
        ShellFrame frame,
        int sectionTag,
        IPlateSectionShellMaterialResolver resolver,
        Dictionary<string, NativeShellMaterialDefinition> byFingerprint,
        HashSet<int> usedTags,
        List<NativeShellMaterialDefinition> materials,
        out List<string> diagnostics)
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

        IReadOnlyList<NativeShellMaterialDefinition> concreteChain = ResolveMaterial(
            () => resolver.ResolveConcrete(section.ConcreteMaterialId), "бетона");
        NativeShellMaterialDefinition concreteRegistered = RegisterChain(
            concreteChain, concreteChain[^1].Tag, null, byFingerprint, usedTags, materials);

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
        diagnostics = new List<string>();
        for (int sourceIndex = 0; sourceIndex < section.RebarLayers.Count; sourceIndex++)
        {
            PlateRebarLayer source = section.RebarLayers[sourceIndex];
            ValidateRebarCoordinate(source.Zsx, section.H, sourceIndex, "Zsx");
            ValidateRebarCoordinate(source.Zsy, section.H, sourceIndex, "Zsy");
            ValidateArea(source.Asx, sourceIndex, "Asx");
            ValidateArea(source.Asy, sourceIndex, "Asy");
            ValidateRebarAngle(source.Angle, sourceIndex);
            double angleX = NormalizeDegrees(source.Angle);
            double angleY = NormalizeDegrees(source.Angle + 90.0);

            if (source.Asx > 0)
            {
                hasRebar = true;
                int sourceMaterialId = source.MaterialId != 0
                    ? source.MaterialId : section.RebarMaterialId;
                IReadOnlyList<NativeShellMaterialDefinition> rebarChain = ResolveMaterial(
                    () => resolver.ResolveRebar(sourceMaterialId), "арматуры");
                NativeShellMaterialDefinition oriented = RegisterChain(
                    rebarChain, nextMaterialTag, angleX, byFingerprint, usedTags, materials);
                nextMaterialTag = Math.Max(nextMaterialTag, oriented.Tag + 1);
                layers.Add(new RCShellLayer(
                    0, ShellLayerKind.RebarX, source.Zsx, source.Asx, oriented.Tag, angleX,
                    $"rebar:{sourceIndex}:x"));
            }

            if (source.Asy > 0)
            {
                hasRebar = true;
                int sourceMaterialId = source.MaterialId != 0
                    ? source.MaterialId : section.RebarMaterialId;
                IReadOnlyList<NativeShellMaterialDefinition> rebarChain = ResolveMaterial(
                    () => resolver.ResolveRebar(sourceMaterialId), "арматуры");
                NativeShellMaterialDefinition oriented = RegisterChain(
                    rebarChain, nextMaterialTag, angleY, byFingerprint, usedTags, materials);
                nextMaterialTag = Math.Max(nextMaterialTag, oriented.Tag + 1);
                layers.Add(new RCShellLayer(
                    0, ShellLayerKind.RebarY, source.Zsy, source.Asy, oriented.Tag, angleY,
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
                layer.MaterialId.ToString(CultureInfo.InvariantCulture),
                layer.Angle.ToString("G17", CultureInfo.InvariantCulture),
                layer.Face.ToString()))));
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
        return resultSection;
    }

    private static IReadOnlyList<NativeShellMaterialDefinition> ResolveMaterial(
        Func<IReadOnlyList<NativeShellMaterialDefinition>> resolver,
        string kind)
    {
        try
        {
            IReadOnlyList<NativeShellMaterialDefinition>? result = resolver();
            if (result is null || result.Count == 0)
                throw new CScoreMappingException($"Не разрешён material {kind}.");
            return result;
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

    /// <summary>Регистрирует цепочку зависимых материалов (база → обёртки) по порядку,
    /// переписывая DependsOnMaterialTag каждого элемента на ФИНАЛЬНЫЙ tag предыдущего элемента
    /// цепочки (резолвер не знает финальную нумерацию из-за глобальной дедупликации между
    /// секциями в MapMany). Если orientLastAsDegrees задан и последний элемент —
    /// PlateRebarShellMaterialSpec, переопределяет его AngleDegrees перед регистрацией.</summary>
    private static NativeShellMaterialDefinition RegisterChain(
        IReadOnlyList<NativeShellMaterialDefinition> chain,
        int preferredLastTag,
        double? orientLastAsDegrees,
        Dictionary<string, NativeShellMaterialDefinition> byFingerprint,
        HashSet<int> usedTags,
        List<NativeShellMaterialDefinition> materials)
    {
        var resolvedTagByOriginal = new Dictionary<int, int>();
        NativeShellMaterialDefinition? last = null;

        for (int i = 0; i < chain.Count; i++)
        {
            NativeShellMaterialDefinition definition = chain[i];
            NativeShellMaterialSpec spec = definition.Spec;

            if (spec.DependsOnMaterialTag is int dependsOn)
            {
                if (!resolvedTagByOriginal.TryGetValue(dependsOn, out int finalDependsOn))
                    throw new CScoreMappingException(
                        $"Материал {definition.SourceId}: зависимость (tag {dependsOn}) не зарегистрирована раньше в цепочке резолвера.");
                spec = spec.WithDependencyTag(finalDependsOn);
            }

            bool isLast = i == chain.Count - 1;
            if (isLast && orientLastAsDegrees is double direction && spec is PlateRebarShellMaterialSpec plateRebar)
                spec = plateRebar with { AngleDegrees = direction };

            NativeShellMaterialDefinition toRegister = definition with { Spec = spec };
            toRegister.Validate();
            int preferredTag = isLast ? preferredLastTag : definition.Tag;
            NativeShellMaterialDefinition registered = Register(
                toRegister, byFingerprint, usedTags, materials, preferredTag);

            resolvedTagByOriginal[definition.Tag] = registered.Tag;
            last = registered;
        }

        return last ?? throw new CScoreMappingException("Резолвер вернул пустую цепочку shell-материалов.");
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

    private static void ValidateRebarAngle(double value, int index)
    {
        if (!double.IsFinite(value))
            throw new CScoreMappingException(
                $"rebar_angle_invalid: арматурный слой {index}: угол должен быть конечным.");
    }

    private static double NormalizeDegrees(double degrees)
    {
        double value = degrees % 360.0;
        if (value >= 180.0) value -= 360.0;
        if (value < -180.0) value += 360.0;
        return value;
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
