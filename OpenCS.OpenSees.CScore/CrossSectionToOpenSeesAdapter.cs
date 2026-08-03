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

        /// <summary>Учитывать ли работу бетона на растяжение (см.
        /// <see cref="MaterialDiagramMapper.Map"/>). На арматуру/сталь не влияет.</summary>
        public bool ConsiderConcreteTension { get; init; } = true;

        /// <summary>Источник построения диаграммы материала: перевод диаграммы CScore
        /// (по умолчанию) либо нативные параметрические материалы OpenSees.</summary>
        public MaterialSource MaterialSource { get; init; } = MaterialSource.Translated;

        /// <summary>Модель основной области при <see cref="MaterialSource.Native"/>.</summary>
        public MainMaterialModelKind MainMaterialModel { get; init; } = MainMaterialModelKind.Concrete04;

        /// <summary>Модель стали/арматуры при <see cref="MaterialSource.Native"/>.</summary>
        public SteelModelKind SteelModel { get; init; } = SteelModelKind.Steel02;

        /// <summary>Явное переопределение отношения модуля упрочнения стали/арматуры к E0 при
        /// <see cref="MaterialSource.Native"/>. <c>null</c> — вычисляется автоматически.</summary>
        public double? SteelHardeningRatioOverride { get; init; }

        /// <summary>Учитывать ли физическую (материальную) нелинейность. При <c>false</c> результат
        /// вообще не содержит fiber-секции: строится единая линейно-упругая
        /// <see cref="OpenSeesElasticSectionSpec"/> с приведёнными (transformed, к модулю упругости
        /// материала с наибольшей суммарной площадью волокон) EA/EIz/EIy — без явных
        /// материалов/волокон, диаграмма CScore не запрашивается вовсе. Игнорирует
        /// <see cref="MaterialSource"/>/<see cref="MainMaterialModel"/>/<see cref="SteelModel"/>/
        /// <see cref="ConsiderConcreteTension"/>.</summary>
        public bool ConsiderPhysicalNonlinearity { get; init; } = true;
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

        if (!options.ConsiderPhysicalNonlinearity)
        {
            return BuildElastic(section, calc, materials, options);
        }

        List<OpenSeesMaterialDefinition> definitions = [];
        List<OpenSeesFiber> fibers = [];
        Dictionary<MaterialKey, int> tags = [];

        foreach (MaterialArea area in section.Areas)
        {
            if (area.Fibers.Count == 0)
            {
                throw new CScoreMappingException(
                    $"Область '{area.Tag}' (id={area.Id}) не содержит подготовленной фибровой сетки. " +
                    "Для расчёта сечения через OpenSees необходимо заранее построить и сохранить " +
                    "сетку фибр; одного геометрического контура и точечных фибр арматуры недостаточно.");
            }

            Material material = ResolveMaterial(area, materials);
            int customDiagramId = material.CustomDiagramIds.TryGetValue(calc, out int id) ? id : 0;
            int sourceId = material.Id != 0 ? material.Id : area.MaterialId;
            MaterialKey key = new(sourceId, material.Type, area.DiagrammType, calc, customDiagramId);

            if (!tags.TryGetValue(key, out int materialTag))
            {
                materialTag = options.FirstMaterialTag + definitions.Count;
                OpenSeesMaterialDefinition definition;
                try
                {
                    Diagramm diagram = ResolveDiagram(area, material, calc, customPool);
                    bool isReinforcement = area.HostAreaId is not null;
                    NativeMaterialSpec? native = options.MaterialSource == MaterialSource.Native
                        ? NativeMaterialMapper.Map(
                            material.GetChars(calc), material.Type, options.ConsiderConcreteTension,
                            options.MainMaterialModel, options.SteelModel, isReinforcement,
                            options.SteelHardeningRatioOverride)
                        : null;

                    if (native != null)
                    {
                        definition = new OpenSeesMaterialDefinition
                        {
                            Tag = materialTag,
                            SourceId = sourceId.ToString(CultureInfo.InvariantCulture),
                            SourceType = material.Type.ToString(),
                            Native = native,
                            PositiveEnvelope = [],
                            NegativeEnvelope = []
                        };
                    }
                    else
                    {
                        definition = MaterialDiagramMapper.Map(
                            diagram,
                            materialTag,
                            sourceId.ToString(CultureInfo.InvariantCulture),
                            material.Type,
                            options.ConsiderConcreteTension);

                        if (options.MaterialSource == MaterialSource.Native)
                        {
                            definition = new OpenSeesMaterialDefinition
                            {
                                Tag = definition.Tag,
                                SourceId = definition.SourceId,
                                SourceType = definition.SourceType,
                                PositiveEnvelope = definition.PositiveEnvelope,
                                NegativeEnvelope = definition.NegativeEnvelope,
                                Warnings = [.. definition.Warnings,
                                    "Нативная параметризация недоступна для этого материала (Custom либо нет характеристик для данного вида расчёта) — использована переведённая диаграмма."]
                            };
                        }
                    }
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

    /// <summary>Строит линейно-упругую (приведённую, transformed) секцию без явных материалов и
    /// волокон — эталонный модуль упругости берётся у материала с наибольшей суммарной площадью
    /// волокон (для типового ЖБ сечения — бетон матрицы), остальные материалы приводятся к нему
    /// модульным отношением n = E_i / E_ref, взвешивая их площади волокон при накоплении
    /// EA/EIz/EIy. Крутильная жёсткость GJ берётся готовой из <paramref name="options"/> — она уже
    /// не зависит от диаграммы материала и в fiber-варианте секции.</summary>
    private static OpenSeesSectionModel BuildElastic(
        CrossSection section,
        CalcType calc,
        IReadOnlyDictionary<int, Material> materials,
        Options options)
    {
        Dictionary<int, double> totalAreaByMaterial = [];
        Dictionary<int, double> elasticModulusByMaterial = [];
        foreach (MaterialArea area in section.Areas)
        {
            if (area.Fibers.Count == 0)
            {
                throw new CScoreMappingException(
                    $"Область '{area.Tag}' (id={area.Id}) не содержит подготовленных fibers.");
            }

            Material material = ResolveMaterial(area, materials);
            double areaSum = 0;
            foreach (Fiber fiber in area.Fibers)
            {
                if (!double.IsFinite(fiber.Area) || fiber.Area <= 0)
                {
                    throw new CScoreMappingException(
                        $"Область '{area.Tag}' (id={area.Id}), fiber: площадь должна быть положительной.");
                }
                areaSum += fiber.Area;
            }

            totalAreaByMaterial[material.Id] = totalAreaByMaterial.GetValueOrDefault(material.Id) + areaSum;
            if (elasticModulusByMaterial.ContainsKey(material.Id))
                continue;
            MaterialChars chars = material.GetChars(calc)
                ?? throw new CScoreMappingException(
                    $"Область '{area.Tag}' (id={area.Id}): для материала отсутствуют характеристики вида расчёта {calc}.");
            elasticModulusByMaterial.Add(material.Id, CScoreUnitConverter.KilopascalsToPascals(chars.E));
        }

        if (totalAreaByMaterial.Count == 0)
        {
            throw new CScoreMappingException("Сечение не содержит подготовленных fibers.");
        }

        int refMaterialId = totalAreaByMaterial.OrderByDescending(kv => kv.Value).First().Key;
        double refModulusPa = elasticModulusByMaterial[refMaterialId];
        if (!double.IsFinite(refModulusPa) || refModulusPa <= 0)
        {
            throw new CScoreMappingException(
                "Эталонный модуль упругости для приведённого линейно-упругого сечения должен быть положительным.");
        }

        double transformedA = 0, transformedIz = 0, transformedIy = 0;
        foreach (MaterialArea area in section.Areas)
        {
            Material material = ResolveMaterial(area, materials);
            double modulusPa = elasticModulusByMaterial[material.Id];
            double n = modulusPa / refModulusPa;
            foreach (Fiber fiber in area.Fibers)
            {
                (double y, double z) = CScoreUnitConverter.ToOpenSeesCoordinates(
                    fiber.X, fiber.Y, options.Convention);
                double weightedArea = fiber.Area * n;
                transformedA += weightedArea;
                transformedIz += weightedArea * y * y;
                transformedIy += weightedArea * z * z;
            }
        }

        var elastic = new OpenSeesElasticSectionSpec(refModulusPa, transformedA, transformedIz, transformedIy, options.GJ);
        OpenSeesSectionModel result = new() { Elastic = elastic, Convention = options.Convention };
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
