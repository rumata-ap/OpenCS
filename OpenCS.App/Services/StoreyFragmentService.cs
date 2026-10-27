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

    /// <summary>
    /// Сверка родителя фрагмента: снимок полосы перечитывается тем же отбором и сравнивается с отпечатком сборки.
    /// Ничего не меняет — только сигнал «родитель изменён после сборки» или «родитель удалён».
    /// </summary>
    public static FragmentParentStatus CheckParent(DatabaseService db, FemSchema schema)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.ParentSchemaId is not int parentId || schema.FragmentZ1 is not double z1 || schema.FragmentZ2 is not double z2)
            return FragmentParentStatus.NotFragment;
        if (db.GetFemSchemaSourceType(parentId) is null) return FragmentParentStatus.ParentDeleted;
        var snapshot = db.ReadStoreyFragmentSnapshot(parentId, new StoreyFragmentRequest(z1, z2, schema.Tag));
        return StoreyFragmentFingerprint.Compute(snapshot) == schema.ParentFingerprint
            ? FragmentParentStatus.Unchanged
            : FragmentParentStatus.Changed;
    }
}
