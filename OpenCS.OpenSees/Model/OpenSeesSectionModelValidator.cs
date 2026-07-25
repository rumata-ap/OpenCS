namespace OpenCS.OpenSees.Model;

/// <summary>Проверяет ограничения нейтральной fiber-модели.</summary>
public static class OpenSeesSectionModelValidator
{
    /// <summary>Проверяет модель и выбрасывает ArgumentException при нарушении контракта.</summary>
    public static void Validate(OpenSeesSectionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

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
            case Steel01Spec s1:
                RequireFinite(s1.Fy, materialTag, nameof(s1.Fy));
                RequireFinite(s1.E0, materialTag, nameof(s1.E0));
                RequireFinite(s1.B, materialTag, nameof(s1.B));
                if (s1.Fy <= 0)
                    throw new ArgumentException($"Материал {materialTag}: Fy должно быть положительным.");
                if (s1.E0 <= 0)
                    throw new ArgumentException($"Материал {materialTag}: E0 должно быть положительным.");
                if (s1.B <= 0 || s1.B >= 1)
                    throw new ArgumentException($"Материал {materialTag}: b должно быть в диапазоне (0, 1).");
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
                if (s2.B <= 0 || s2.B >= 1)
                    throw new ArgumentException($"Материал {materialTag}: b должно быть в диапазоне (0, 1).");
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
