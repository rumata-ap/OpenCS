using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>Запрос фрагмента этажа: полоса z₁…z₂ (середины этажей ниже и выше перекрытия), м; допуск, м.</summary>
public sealed record StoreyFragmentRequest(double Z1, double Z2, string Name, double ToleranceM = 0.001);

/// <summary>Часть схемы-родителя, нужная построителю (сервис читает только полосу).</summary>
public sealed class StoreyFragmentParentSnapshot
{
    public IReadOnlyList<FemNode> Nodes { get; init; } = [];
    public IReadOnlyList<FemMember> Members { get; init; } = [];
    /// <summary>Области КонЭ-пластин по <see cref="FemMember.PlanarRegionId"/>.</summary>
    public IReadOnlyList<PlanarRegion> Regions { get; init; } = [];
    public IReadOnlyList<FemMeshNode> MeshNodes { get; init; } = [];
    public IReadOnlyList<FemElement> MeshElements { get; init; } = [];
    public IReadOnlyList<FemRigidBody> RigidBodies { get; init; } = [];
}

/// <summary>Вид сечения рассечения: конец стержня или кромка стены.</summary>
public enum FragmentCutKind { End, Edge }

/// <summary>Сторона полосы: z₁ (низ) или z₂ (верх).</summary>
public enum FragmentCutSide { Bottom, Top }

/// <summary>
/// Сечение рассечения фрагмента — место, где отброшена остальная схема; сюда подпроекты C/D кладут маски и ГУ.
/// </summary>
/// <param name="MemberTag">КонЭ фрагмента.</param>
/// <param name="NodeTag">Узел конца стержня (<see cref="FragmentCutKind.End"/>), иначе null.</param>
/// <param name="Segments">Отрезки кромки стены в общих осях (<see cref="FragmentCutKind.Edge"/>), иначе пусто.</param>
public sealed record FragmentCut(
    string MemberTag, FragmentCutKind Kind, FragmentCutSide Side, double Z, string? NodeTag,
    IReadOnlyList<(PlanarVector3 P, PlanarVector3 Q)> Segments);

/// <summary>КонЭ фрагмента, его область (у пластин) и КонЭ родителя, из которого он получен.</summary>
public sealed record StoreyFragmentMember(FemMember Member, PlanarRegion? Region, string ParentMemberTag);

/// <summary>Черновик фрагмента: всё, что сервис запишет в новую схему.</summary>
public sealed class StoreyFragmentDraft
{
    public List<FemNode> Nodes { get; } = [];
    public List<StoreyFragmentMember> Members { get; } = [];
    public List<FragmentCut> Cuts { get; } = [];
    public List<FemValidationDiagnostic> Diagnostics { get; } = [];
    /// <summary>Есть отказ — схему не создавать.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.IsError);
}
