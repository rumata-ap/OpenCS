using System.Globalization;
using CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Преобразует подготовленное CScore-сечение в нейтральную fiber-модель.</summary>
public static class CrossSectionToOpenSeesAdapter
{
    /// <summary>Настройки единиц, координат и крутильной жёсткости секции.</summary>
    public sealed class Options
    {
        /// <summary>Крутильная жёсткость GJ в Н·м².</summary>
        public double GJ { get; init; }

        /// <summary>Соглашение координат OpenSees.</summary>
        public OpenSeesCoordinateConvention Convention { get; init; } =
            OpenSeesCoordinateConvention.CScoreDefault;

        /// <summary>Первый тег материала OpenSees.</summary>
        public int FirstMaterialTag { get; init; } = 1;
    }

    /// <summary>Строит модель из уже подготовленных фибр без изменения исходного сечения.</summary>
    public static OpenSeesSectionModel Build(
        CrossSection section,
        CalcType calc,
        IReadOnlyDictionary<int, Material> materials,
        IReadOnlyList<Diagramm>? customPool,
        Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(materials);
        options ??= new Options();

        if (options.FirstMaterialTag <= 0)
        {
            throw new CScoreMappingException("Первый тег материала OpenSees должен быть положительным.");
        }

        List<OpenSeesMaterialDefinition> definitions = [];
        List<OpenSeesFiber> fibers = [];
        Dictionary<MaterialKey, int> tags = [];

        foreach (MaterialArea area in section.Areas)
        {
            if (area.Fibers.Count == 0)
            {
                throw new CScoreMappingException(
                    $"Область '{area.Tag}' (id={area.Id}) не содержит подготовленных fibers.");
            }

            Material material = ResolveMaterial(area, materials);
            Diagramm diagram = ResolveDiagram(area, material, calc, customPool);
            int customDiagramId = material.CustomDiagramIds.TryGetValue(calc, out int id) ? id : 0;
            int sourceId = material.Id != 0 ? material.Id : area.MaterialId;
            MaterialKey key = new(sourceId, material.Type, area.DiagrammType, calc, customDiagramId);

            if (!tags.TryGetValue(key, out int materialTag))
            {
                materialTag = options.FirstMaterialTag + definitions.Count;
                OpenSeesMaterialDefinition definition;
                try
                {
                    definition = MaterialDiagramMapper.Map(
                        diagram,
                        materialTag,
                        sourceId.ToString(CultureInfo.InvariantCulture),
                        material.Type);
                }
                catch (CScoreMappingException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new CScoreMappingException(
                        $"Не удалось преобразовать диаграмму области '{area.Tag}' (id={area.Id}).",
                        exception);
                }

                definitions.Add(definition);
                tags.Add(key, materialTag);
            }

            for (int fiberIndex = 0; fiberIndex < area.Fibers.Count; fiberIndex++)
            {
                Fiber fiber = area.Fibers[fiberIndex];
                if (!double.IsFinite(fiber.Area) || fiber.Area <= 0)
                {
                    throw new CScoreMappingException(
                        $"Область '{area.Tag}' (id={area.Id}), fiber {fiberIndex}: площадь должна быть положительной.");
                }

                (double y, double z) = CScoreUnitConverter.ToOpenSeesCoordinates(
                    fiber.X,
                    fiber.Y,
                    options.Convention);

                fibers.Add(new OpenSeesFiber(y, z, fiber.Area, materialTag));
            }
        }

        OpenSeesSectionModel result = new()
        {
            Materials = definitions,
            Fibers = fibers,
            GJ = options.GJ,
            Convention = options.Convention
        };
        result.Validate();
        return result;
    }

    private static Material ResolveMaterial(
        MaterialArea area,
        IReadOnlyDictionary<int, Material> materials)
    {
        if (area.Material != null)
            return area.Material;

        if (materials.TryGetValue(area.MaterialId, out Material? material))
            return material;

        throw new CScoreMappingException(
            $"Область '{area.Tag}' (id={area.Id}) ссылается на отсутствующий материал {area.MaterialId}.");
    }

    private static Diagramm ResolveDiagram(
        MaterialArea area,
        Material material,
        CalcType calc,
        IReadOnlyList<Diagramm>? customPool)
    {
        try
        {
            Dictionary<CalcType, Diagramm>? diagrams;
            if (material.Type == MatType.Custom)
            {
                diagrams = customPool == null
                    ? null
                    : material.ResolveCustomDiagramms(customPool);
            }
            else
            {
                diagrams = material.GetDiagramms(area.DiagrammType);
            }

            if (diagrams == null || !diagrams.TryGetValue(calc, out Diagramm? diagram))
            {
                throw new CScoreMappingException(
                    $"Для области '{area.Tag}' (id={area.Id}) отсутствует диаграмма {area.DiagrammType}/{calc}.");
            }

            return diagram;
        }
        catch (CScoreMappingException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CScoreMappingException(
                $"Не удалось получить диаграмму {area.DiagrammType}/{calc} для области '{area.Tag}' (id={area.Id}).",
                exception);
        }
    }

    private readonly record struct MaterialKey(
        int SourceId,
        MatType SourceType,
        DiagrammType DiagramType,
        CalcType CalcType,
        int CustomDiagramId);
}
