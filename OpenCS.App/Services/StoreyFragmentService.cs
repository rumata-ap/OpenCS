using CScore.Fem;
using CScore.Fem.Fragment;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Итог сборки фрагмента: новая схема (null — отказ построителя) и черновик с диагностикой.</summary>
public sealed record StoreyFragmentBuildResult(FemSchema? Schema, StoreyFragmentDraft Draft);

/// <summary>Состояние родителя фрагмента относительно отпечатка, снятого при сборке.</summary>
public enum FragmentParentStatus
{
    /// <summary>Схема не фрагмент.</summary>
    NotFragment,
    /// <summary>Схема-родитель удалена.</summary>
    ParentDeleted,
    /// <summary>Полоса родителя не менялась.</summary>
    Unchanged,
    /// <summary>Полоса родителя изменена после сборки.</summary>
    Changed,
}

/// <summary>
/// Сборка фрагмента этажа (спека 2026-10-23, разд. 2.2): снимок полосы родителя → <see cref="StoreyFragmentBuilder"/> →
/// запись новой схемы одной транзакцией; сверка отпечатка родителя. Сетку фрагмента не строит.
/// </summary>
public static class StoreyFragmentService
{
    /// <summary>Собрать фрагмент схемы <paramref name="parentId"/> по полосе запроса; при отказе схема не создаётся.</summary>
    public static StoreyFragmentBuildResult Build(DatabaseService db, int parentId, StoreyFragmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(request);
        if (db.GetFemSchemaSourceType(parentId) is null)
            throw new InvalidOperationException($"Схема-родитель {parentId} не найдена.");
        var snapshot = db.ReadStoreyFragmentSnapshot(parentId, request);
        var draft = StoreyFragmentBuilder.Build(snapshot, request);
        if (draft.HasErrors) return new(null, draft);
        var schema = db.CreateStoreyFragmentSchema(parentId, request, draft, StoreyFragmentFingerprint.Compute(snapshot));
        return new(schema, draft);
    }
}
