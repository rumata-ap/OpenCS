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

            ValidateEnvelope(material.PositiveEnvelope, material.Tag, "положительная");
            ValidateEnvelope(material.NegativeEnvelope, material.Tag, "отрицательная");
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
}
