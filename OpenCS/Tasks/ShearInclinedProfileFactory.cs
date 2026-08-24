using CScore;
using CScore.Sp63Shear;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Tasks;

/// <summary>Результат сборки профиля усилий: профиль либо текст ошибки.</summary>
/// <param name="Profile">Собранный профиль; null при ошибке.</param>
/// <param name="Error">Текст ошибки; null при успехе.</param>
/// <param name="Warnings">Предупреждения для отчёта.</param>
public sealed record ProfileBuildResult(
    IForceProfile? Profile, string? Error, IReadOnlyList<string> Warnings);

/// <summary>
/// Собирает профиль усилий вдоль элемента по параметрам задачи: постоянные усилия,
/// равномерная нагрузка либо эпюра из результата расчёта схемы OpenSees.
/// </summary>
public static class ShearInclinedProfileFactory
{
    /// <summary>Минимальное число КЭ на стержень, ниже которого выдаётся предупреждение.</summary>
    public const int RecommendedMeshElements = 8;

    /// <summary>Собирает профиль усилий для заданной плоскости сдвига.</summary>
    public static ProfileBuildResult Build(
        ShearInclinedParams parameters, LoadItem item, ShearPlane plane,
        ForceSet? forceSet, DatabaseService? database)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(item);

        double q = plane == ShearPlane.Vy ? item.Vy : item.Vx;
        double m = plane == ShearPlane.Vy ? item.Mx : item.My;

        return parameters.ForceSource switch
        {
            "uniform_load" => BuildUniform(parameters, q, m, item.N),
            "fem_profile" => BuildFem(parameters, plane, forceSet, database),
            _ => new ProfileBuildResult(
                new ConstantProfile(q, m, item.N, parameters.DistanceToSupport), null, [])
        };
    }

    /// <summary>Создаёт табличный профиль по готовому набору сечений.</summary>
    public static SampledProfile FromSamples(IReadOnlyList<ForceSample> samples) =>
        new(samples, 0.0, samples.Max(sample => sample.S));

    /// <summary>Профиль при равномерно распределённой нагрузке.</summary>
    static ProfileBuildResult BuildUniform(
        ShearInclinedParams parameters, double q, double m, double n)
    {
        if (parameters.DistanceToSupport <= 0.0)
            return new ProfileBuildResult(null,
                "Для режима равномерной нагрузки требуется положительное расстояние до опоры.", []);

        if (!parameters.SupportAtStart && !parameters.SupportAtEnd)
            return new ProfileBuildResult(null,
                "Не объявлено ни одной опоры — наклонное сечение строить не от чего.", []);

        return new ProfileBuildResult(
            new UniformLoadProfile(
                q, m, n, parameters.DistributedLoad, parameters.DistanceToSupport,
                parameters.SupportAtStart, parameters.SupportAtEnd),
            null, []);
    }

    /// <summary>Профиль по эпюре усилий конструктивного стержня из результата OpenSees.</summary>
    static ProfileBuildResult BuildFem(
        ShearInclinedParams parameters, ShearPlane plane,
        ForceSet? forceSet, DatabaseService? database)
    {
        if (forceSet?.SourceSchemaId is not int schemaId || forceSet.SourceMemberId is not int memberId)
            return new ProfileBuildResult(null,
                "Режим эпюры FEM требует набор усилий, созданный из расчётной схемы OpenSees.", []);

        if (database is null)
            return new ProfileBuildResult(null,
                "Режим эпюры FEM требует контекст выполнения с доступом к базе данных.", []);

        var schema = database.FemSchemas.FirstOrDefault(s => s.Id == schemaId);
        if (schema is null)
            return new ProfileBuildResult(null, $"Расчётная схема id={schemaId} не найдена.", []);

        var analysis = FemAnalysisResultResolver.FindLatestWithResult(schema.Analyses);
        if (analysis?.ResultId is not int resultId)
            return new ProfileBuildResult(null,
                "У расчётной схемы нет сохранённого результата расчёта.", []);

        var calcResult = database.GetCalcResultById(resultId);
        if (calcResult is null)
            return new ProfileBuildResult(null, "Результат расчёта схемы недоступен.", []);

        var member = database.GetFemMembers(schemaId).FirstOrDefault(m => m.Id == memberId);
        if (member is null)
            return new ProfileBuildResult(null,
                $"Конструктивный стержень id={memberId} не найден в схеме.", []);

        var step = FemMemberForceResultResolver.ResolveStep(calcResult, parameters.FemStepIndex);
        if (step is null)
            return new ProfileBuildResult(null,
                parameters.FemStepIndex is int requested
                    ? $"Шаг расчёта {requested} в результате не найден."
                    : "В результате расчёта нет усилий сошедшегося шага.", []);
        if (!step.Converged)
            return new ProfileBuildResult(null,
                $"Шаг расчёта {step.StepIndex} не сошёлся — усилия этого шага использовать нельзя.", []);
        if (step.Forces.Count == 0)
            return new ProfileBuildResult(null,
                $"На шаге {step.StepIndex} нет усилий стержневых элементов.", []);

        var input = new FemMemberForceSetBuildInput(
            schema, member,
            database.GetFemNodes(schemaId),
            database.GetFemMeshNodes(schemaId),
            database.GetFemMeshElements(schemaId),
            step.Forces,
            step.StepIndex,
            step.StepLabel,
            step.Converged);

        var build = FemMemberForceSetBuilder.Build(input);
        if (!build.IsSuccess)
            return new ProfileBuildResult(null,
                $"Не удалось построить эпюру усилий стержня: {build.Error}.", []);

        var rows = build.Preview!.Rows;
        if (rows.Count < 2)
            return new ProfileBuildResult(null,
                "Эпюра стержня содержит менее двух сечений.", []);

        var samples = new List<ForceSample>();
        foreach (var row in rows.OrderBy(r => r.PositionS))
        {
            var loadItem = row.ToLoadItem(samples.Count + 1);
            double q = plane == ShearPlane.Vy ? loadItem.Vy : loadItem.Vx;
            double m = plane == ShearPlane.Vy ? loadItem.Mx : loadItem.My;
            samples.Add(new ForceSample(row.PositionS, q, m, loadItem.N));
        }

        var warnings = new List<string>();
        if (rows.Count - 1 < RecommendedMeshElements)
            warnings.Add(
                $"Стержень разбит на {rows.Count - 1} КЭ — для надёжной эпюры моментов "
                + $"рекомендуется не менее {RecommendedMeshElements}.");
        warnings.Add($"Усилия сняты с шага расчёта {step.StepIndex} ({step.StepLabel}).");

        double length = samples[^1].S - samples[0].S;
        SampledProfile profile;
        try
        {
            profile = new SampledProfile(
                samples, 0.0, length, parameters.SupportAtStart, parameters.SupportAtEnd);
        }
        catch (ArgumentException ex)
        {
            return new ProfileBuildResult(null, $"Эпюра стержня непригодна: {ex.Message}", []);
        }

        warnings.AddRange(profile.Warnings);
        return new ProfileBuildResult(profile, null, warnings);
    }
}
