namespace OpenCS.OpenSees.Model;

/// <summary>Проверяет ограничения нейтральной fiber-модели.</summary>
public static class OpenSeesSectionModelValidator
{
    /// <summary>Проверяет модель и выбрасывает ArgumentException при нарушении контракта.</summary>
    public static void Validate(OpenSeesSectionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.Elastic is { } elastic)
        {
            if (model.Materials.Count != 0 || model.Fibers.Count != 0)
            {
                throw new ArgumentException(
                    "Линейно-упругая секция (Elastic) не должна содержать материалы/волокна.", nameof(model));
            }
            ValidateElastic(elastic);
            return;
        }

        if (model.Materials.Count == 0)
        {
            throw new ArgumentException("Секция должна содержать хотя бы один материал.", nameof(model));
        }

        if (model.Fibers.Count == 0)
        {
            throw new ArgumentException("Секция должна содержать хотя бы одно волокно.", nameof(model));
        }

        if (!double.IsFinite(model.GJ) || model.GJ < 0)
        {
            throw new ArgumentException("GJ должно быть конечным и неотрицательным.", nameof(model));
        }

        HashSet<int> materialTags = [];
        foreach (OpenSeesMaterialDefinition material in model.Materials)
        {
            if (material is null)
            {
                throw new ArgumentException("Список материалов не может содержать null.", nameof(model));
            }

            if (material.Tag <= 0 || !materialTags.Add(material.Tag))
            {
                throw new ArgumentException(
                    $"Тег материала должен быть положительным и уникальным: {material.Tag}.",
                    nameof(model));
            }

            if (material.Native is not null)
            {
                ValidateNative(material.Native, material.Tag);
            }
            else
            {
                ValidateEnvelope(material.PositiveEnvelope, material.Tag, "положительная");
                ValidateEnvelope(material.NegativeEnvelope, material.Tag, "отрицательная");
            }
        }

        for (int index = 0; index < model.Fibers.Count; index++)
        {
            OpenSeesFiber fiber = model.Fibers[index];
            if (!double.IsFinite(fiber.Y) || !double.IsFinite(fiber.Z))
            {
                throw new ArgumentException($"Координаты волокна {index} должны быть конечными.", nameof(model));
            }

            if (!double.IsFinite(fiber.AreaM2) || fiber.AreaM2 <= 0)
            {
                throw new ArgumentException($"Площадь волокна {index} должна быть положительной и конечной.", nameof(model));
            }

            if (!materialTags.Contains(fiber.MaterialTag))
            {
                throw new ArgumentException(
                    $"Волокно {index} ссылается на неизвестный тег материала {fiber.MaterialTag}.",
                    nameof(model));
            }
        }
    }

    private static void ValidateElastic(OpenSeesElasticSectionSpec elastic)
    {
        if (!double.IsFinite(elastic.E) || elastic.E <= 0)
            throw new ArgumentException("Elastic-секция: модуль упругости E должен быть положительным.");
        if (!double.IsFinite(elastic.A) || elastic.A <= 0)
            throw new ArgumentException("Elastic-секция: площадь A должна быть положительной.");
        if (!double.IsFinite(elastic.Iz) || elastic.Iz <= 0)
            throw new ArgumentException("Elastic-секция: момент инерции Iz должен быть положительным.");
        if (!double.IsFinite(elastic.Iy) || elastic.Iy <= 0)
            throw new ArgumentException("Elastic-секция: момент инерции Iy должен быть положительным.");
        if (!double.IsFinite(elastic.GJ) || elastic.GJ < 0)
            throw new ArgumentException("Elastic-секция: GJ должно быть конечным и неотрицательным.");
    }

    private static void ValidateEnvelope(
        IReadOnlyList<EnvelopePoint> envelope,
        int materialTag,
        string envelopeName)
    {
        if (envelope.Count == 0)
        {
            throw new ArgumentException(
                $"Материал {materialTag} должен иметь непустую {envelopeName} огибающую.");
        }

        for (int index = 0; index < envelope.Count; index++)
        {
            EnvelopePoint point = envelope[index];
            if (!double.IsFinite(point.Strain) || !double.IsFinite(point.StressPa))
            {
                throw new ArgumentException(
                    $"Точка {index} огибающей материала {materialTag} должна содержать конечные значения.");
            }
        }
    }

    private static void ValidateNative(NativeMaterialSpec native, int materialTag)
    {
        switch (native)
        {
            case Concrete01Spec c1:
                RequireFinite(c1.Fpc, materialTag, nameof(c1.Fpc));
                RequireFinite(c1.Epsc0, materialTag, nameof(c1.Epsc0));
                RequireFinite(c1.Fpcu, materialTag, nameof(c1.Fpcu));
                RequireFinite(c1.EpsU, materialTag, nameof(c1.EpsU));
                if (c1.Fpc >= 0)
                    throw new ArgumentException($"Материал {materialTag}: Fpc должно быть отрицательным (сжатие).");
                if (c1.Epsc0 >= 0)
                    throw new ArgumentException($"Материал {materialTag}: Epsc0 должно быть отрицательным (сжатие).");
                if (c1.EpsU >= c1.Epsc0)
                    throw new ArgumentException($"Материал {materialTag}: EpsU должно быть более отрицательным, чем Epsc0.");
                break;
            case Concrete02Spec c2:
                RequireFinite(c2.Fpc, materialTag, nameof(c2.Fpc));
                RequireFinite(c2.Epsc0, materialTag, nameof(c2.Epsc0));
                RequireFinite(c2.Fpcu, materialTag, nameof(c2.Fpcu));
                RequireFinite(c2.EpsU, materialTag, nameof(c2.EpsU));
                RequireFinite(c2.Lambda, materialTag, nameof(c2.Lambda));
                RequireFinite(c2.Ft, materialTag, nameof(c2.Ft));
                RequireFinite(c2.Ets, materialTag, nameof(c2.Ets));
                if (c2.Fpc >= 0)
                    throw new ArgumentException($"Материал {materialTag}: Fpc должно быть отрицательным (сжатие).");
                if (c2.Epsc0 >= 0)
                    throw new ArgumentException($"Материал {materialTag}: Epsc0 должно быть отрицательным (сжатие).");
                if (c2.EpsU >= c2.Epsc0)
                    throw new ArgumentException($"Материал {materialTag}: EpsU должно быть более отрицательным, чем Epsc0.");
                if (c2.Ft <= 0)
                    throw new ArgumentException($"Материал {materialTag}: Ft должно быть положительным (растяжение).");
                if (c2.Ets <= 0)
                    throw new ArgumentException($"Материал {materialTag}: Ets должно быть положительным.");
                break;
            case Concrete04Spec c4:
                RequireFinite(c4.Fc, materialTag, nameof(c4.Fc));
                RequireFinite(c4.Ec0, materialTag, nameof(c4.Ec0));
                RequireFinite(c4.Ecu, materialTag, nameof(c4.Ecu));
                RequireFinite(c4.Ec, materialTag, nameof(c4.Ec));
                if (c4.Fc >= 0)
                    throw new ArgumentException($"Материал {materialTag}: Fc должно быть отрицательным (сжатие).");
                if (c4.Ec0 >= 0)
                    throw new ArgumentException($"Материал {materialTag}: Ec0 должно быть отрицательным (сжатие).");
                if (c4.Ecu >= c4.Ec0)
                    throw new ArgumentException($"Материал {materialTag}: Ecu должно быть более отрицательным, чем Ec0.");
                if (c4.Ec <= 0)
                    throw new ArgumentException($"Материал {materialTag}: Ec должно быть положительным.");
                if (c4.Fct is not null || c4.Et is not null || c4.Beta is not null)
                {
                    if (c4.Fct is not { } fct || fct <= 0)
                        throw new ArgumentException($"Материал {materialTag}: Fct должно быть положительным при заданном растяжении.");
                    RequireFinite(fct, materialTag, nameof(c4.Fct));
                    if (c4.Et is not { } et || et <= 0)
                        throw new ArgumentException($"Материал {materialTag}: Et должно быть положительным при заданном растяжении.");
                    RequireFinite(et, materialTag, nameof(c4.Et));
                    if (c4.Beta is not { } beta || beta <= 0)
                        throw new ArgumentException($"Материал {materialTag}: Beta должно быть положительным при заданном растяжении.");
                    RequireFinite(beta, materialTag, nameof(c4.Beta));
                }
                break;
            case Steel01Spec s1:
                RequireFinite(s1.Fy, materialTag, nameof(s1.Fy));
                RequireFinite(s1.E0, materialTag, nameof(s1.E0));
                RequireFinite(s1.B, materialTag, nameof(s1.B));
                if (s1.Fy <= 0)
                    throw new ArgumentException($"Материал {materialTag}: Fy должно быть положительным.");
                if (s1.E0 <= 0)
                    throw new ArgumentException($"Материал {materialTag}: E0 должно быть положительным.");
                if (s1.B < 0 || s1.B >= 1)
                    throw new ArgumentException($"Материал {materialTag}: b должно быть в диапазоне [0, 1).");
                break;
            case Steel02Spec s2:
                RequireFinite(s2.Fy, materialTag, nameof(s2.Fy));
                RequireFinite(s2.E0, materialTag, nameof(s2.E0));
                RequireFinite(s2.B, materialTag, nameof(s2.B));
                RequireFinite(s2.R0, materialTag, nameof(s2.R0));
                RequireFinite(s2.CR1, materialTag, nameof(s2.CR1));
                RequireFinite(s2.CR2, materialTag, nameof(s2.CR2));
                if (s2.Fy <= 0)
                    throw new ArgumentException($"Материал {materialTag}: Fy должно быть положительным.");
                if (s2.E0 <= 0)
                    throw new ArgumentException($"Материал {materialTag}: E0 должно быть положительным.");
                if (s2.B < 0 || s2.B >= 1)
                    throw new ArgumentException($"Материал {materialTag}: b должно быть в диапазоне [0, 1).");
                break;
            default:
                throw new ArgumentException($"Материал {materialTag}: неизвестный тип NativeMaterialSpec «{native.GetType().Name}».");
        }
    }

    private static void RequireFinite(double value, int materialTag, string fieldName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException($"Материал {materialTag}: поле {fieldName} должно быть конечным.");
    }
}
