namespace CScore;

/// <summary>Выбранная теория ответа стержневого поперечного сечения.</summary>
public enum SectionResponseTheory
{
    /// <summary>Нормальные плоские сечения без учёта поперечного сдвига.</summary>
    NormalPlaneSections,

    /// <summary>Линейно-упругий поперечный сдвиг по теории Тимошенко.</summary>
    ElasticTimoshenko,

    /// <summary>Пользовательские нелинейные диаграммы «поперечная сила — сдвиговая деформация».</summary>
    UserDefinedShearDiagram,

    /// <summary>Связанная модель наклонных трещин MCFT/DSFM.</summary>
    Mcft
}

/// <summary>Обобщённые деформации стержневого сечения с двумя сдвиговыми компонентами.</summary>
public sealed record ShearSectionDeformation(
    double AxialStrain,
    double CurvatureY,
    double CurvatureZ,
    double ShearStrainY,
    double ShearStrainZ);

/// <summary>Обобщённые усилия стержневого сечения с двумя поперечными силами.</summary>
public sealed record ShearSectionForces(
    double N,
    double Mx,
    double My,
    double Vy,
    double Vz);

/// <summary>Параметры линейно-упругого сдвигового отклика Тимошенко.</summary>
public sealed record ElasticShearSectionOptions(
    double G,
    double ShearAreaY,
    double ShearAreaZ)
{
    /// <summary>Проверяет конечность и физическую допустимость параметров.</summary>
    public void Validate()
    {
        ValidatePositiveFinite(G, nameof(G));
        ValidatePositiveFinite(ShearAreaY, nameof(ShearAreaY));
        ValidatePositiveFinite(ShearAreaZ, nameof(ShearAreaZ));
    }

    static void ValidatePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
            throw new ArgumentOutOfRangeException(name, value, "Параметр должен быть конечным и строго положительным.");
    }
}

/// <summary>Явно задаёт теорию и параметры расчёта ответа стержневого сечения.</summary>
public sealed record SectionResponseOptions(
    SectionResponseTheory Theory,
    ElasticShearSectionOptions? ElasticShear = null)
{
    /// <summary>Создаёт режим нормальных плоских сечений без поперечного сдвига.</summary>
    public static SectionResponseOptions NormalPlaneSections() =>
        new(SectionResponseTheory.NormalPlaneSections);

    /// <summary>Создаёт линейно-упругий режим Тимошенко с двумя сдвиговыми площадями.</summary>
    public static SectionResponseOptions ElasticTimoshenko(double g, double shearAreaY, double shearAreaZ)
    {
        var elastic = new ElasticShearSectionOptions(g, shearAreaY, shearAreaZ);
        elastic.Validate();
        return new(SectionResponseTheory.ElasticTimoshenko, elastic);
    }

    /// <summary>Проверяет сочетание теории, параметров и обобщённых деформаций.</summary>
    public void Validate(ShearSectionDeformation deformation)
    {
        ArgumentNullException.ThrowIfNull(deformation);
        ValidateFinite(deformation.AxialStrain, nameof(deformation.AxialStrain));
        ValidateFinite(deformation.CurvatureY, nameof(deformation.CurvatureY));
        ValidateFinite(deformation.CurvatureZ, nameof(deformation.CurvatureZ));
        ValidateFinite(deformation.ShearStrainY, nameof(deformation.ShearStrainY));
        ValidateFinite(deformation.ShearStrainZ, nameof(deformation.ShearStrainZ));

        switch (Theory)
        {
            case SectionResponseTheory.NormalPlaneSections:
                if (deformation.ShearStrainY != 0.0 || deformation.ShearStrainZ != 0.0)
                    throw new ArgumentException("Режим нормальных плоских сечений не допускает ненулевые сдвиговые деформации.", nameof(deformation));
                return;

            case SectionResponseTheory.ElasticTimoshenko:
                if (ElasticShear is null)
                    throw new ArgumentException("Для режима Тимошенко должны быть заданы параметры упругого сдвига.", nameof(ElasticShear));
                ElasticShear.Validate();
                return;

            case SectionResponseTheory.UserDefinedShearDiagram:
            case SectionResponseTheory.Mcft:
                throw new NotSupportedException($"Теория ответа сечения «{Theory}» пока не реализована.");

            default:
                throw new ArgumentOutOfRangeException(nameof(Theory), Theory, "Неизвестная теория ответа сечения.");
        }
    }

    static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, value, "Деформация должна быть конечной.");
    }
}

/// <summary>Результат вычисления ответа стержневого сечения с поперечным сдвигом.</summary>
public sealed class ShearSectionResult
{
    /// <summary>Поданные в расчёт обобщённые деформации.</summary>
    public required ShearSectionDeformation Deformation { get; init; }

    /// <summary>Вычисленные обобщённые усилия.</summary>
    public required ShearSectionForces Forces { get; init; }

    /// <summary>Фактически применённая теория ответа сечения.</summary>
    public required SectionResponseTheory Theory { get; init; }

    /// <summary>
    /// Касательная 5×5: строки соответствуют <c>(N,Mx,My,Vy,Vz)</c>, столбцы —
    /// <c>(ε0,κy,κz,γy,γz)</c>. Равна null, если жёсткость не вычислялась.
    /// </summary>
    public double[,]? Tangent { get; init; }

    /// <summary>Действия заданного преднапряжения относительно точки отсчёта.</summary>
    public PrestressActionsResult? Prestress { get; init; }
}
