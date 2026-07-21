using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Результат сборки нелинейной модели: модель либо перечень ошибок валидации.</summary>
public sealed record FemNonlinearResolveResult(FemNonlinearModel? Model, IReadOnlyList<string> Errors)
{
    public bool Ok => Model is not null && Errors.Count == 0;
}
