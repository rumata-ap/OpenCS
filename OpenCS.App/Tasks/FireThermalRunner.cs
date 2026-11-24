using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Проверка перед тепловым расчётом: ошибка, запрещающая запуск, и предупреждения.</summary>
/// <param name="Error">Текст ошибки; null — расчёт можно запускать.</param>
/// <param name="Warnings">Предупреждения (шаг вне рекомендуемого диапазона, неизвестные диаметры).</param>
public sealed record FireThermalRunCheck(string? Error, IReadOnlyList<string> Warnings);

/// <summary>
/// Тепловой расчёт огневого сечения без UI: проверка сетки по п. 6.2 СП 468, снимок входных данных, расчёт и запись
/// результата (у огневого сечения один расчёт — прежний заменяется). Общий для страницы огневого сечения и
/// <c>opencs run fire</c>.
/// </summary>
public static class FireThermalRunner
{
    /// <summary>Шаг сетки в мм для сообщений.</summary>
    static double Mm(double m) => m * 1000.0;

    /// <summary>Предупреждение о шаге вне рекомендуемого диапазона; null — в диапазоне.</summary>
    public static string? MeshRangeWarning(CrossSection section, double meshStepM) =>
        FireMeshStepValidator.Check(section, meshStepM).OutOfRecommendedRange
            ? string.Format(Loc.S("FireSection_MeshStepOutOfRange"), Mm(meshStepM))
            : null;

    /// <summary>Проверка шага сетки для сечения.</summary>
    public static FireThermalRunCheck Check(FireSectionDef def, CrossSection section)
    {
        var check = FireMeshStepValidator.Check(section, def.MeshStepM);
        var warnings = new List<string>();
        if (check.UnknownDiameterCount > 0)
            warnings.Add(string.Format(Loc.S("FireSection_RebarDiameterUnknown"), check.UnknownDiameterCount));
        if (check.OutOfRecommendedRange)
            warnings.Add(string.Format(Loc.S("FireSection_MeshStepOutOfRange"), Mm(def.MeshStepM)));
        string? error = null;
        if (check.TooCoarse)
            error = string.Format(Loc.S("FireSection_MeshStepTooCoarse"), Mm(def.MeshStepM), Mm(check.MinSideM),
                FireMeshStepValidator.MinElementsAcross);
        else if (check.BelowRebarDiameter)
            error = string.Format(Loc.S("FireSection_MeshStepBelowRebarDiameter"), Mm(def.MeshStepM), Mm(check.MaxRebarDiameterM));
        return new FireThermalRunCheck(error, warnings);
    }

    /// <summary>
    /// Выполняет расчёт и записывает его. Огневое сечение должно быть сохранено (<c>Id &gt; 0</c>) с теми же данными,
    /// что переданы, — иначе хеш результата не совпадёт с базой.
    /// </summary>
    /// <returns>Идентификатор строки результата.</returns>
    /// <exception cref="InvalidOperationException">Сетка не проходит проверку или огневое сечение не сохранено.</exception>
    public static int Run(DatabaseService db, FireSectionDef def, CrossSection section, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(section);
        if (def.Id <= 0) throw new InvalidOperationException(Loc.S("FireErrNotSaved"));
        var check = Check(def, section);
        if (check.Error != null) throw new InvalidOperationException(check.Error);

        string aggregate = FireSectionAuthoring.EffectiveAggregate(def, section, db.Materials);
        var input = FireThermalInputSnapshot.Build(def, section, aggregate);
        ct.ThrowIfCancellationRequested();
        var result = FireThermalService.Run(def, section, aggregate);
        ct.ThrowIfCancellationRequested();
        return db.SaveFireThermalResult(def.Id, result, input.Json, input.Hash);
    }

    /// <summary>Причина устаревания расчёта или null (актуален); «нет расчёта» — отдельным флагом.</summary>
    public static (bool HasResult, string? StaleReason) State(DatabaseService db, FireSectionDef def, CrossSection? section)
    {
        var info = def.Id > 0 ? db.GetFireThermalResultInfo(def.Id) : null;
        if (info is null) return (false, null);
        if (section is null) return (true, null);
        try { return (true, FireThermalReference.StaleReason(db, def, section, info)); }
        catch (Exception) { return (true, null); }
    }
}
