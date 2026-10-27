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
    public IReadOnlyList<FemLoadCase> LoadCases { get; init; } = [];
    public IReadOnlyList<FemLoadDefinition> LoadDefinitions { get; init; } = [];
    /// <summary>Нагрузки КонЭ-стержней (<see cref="FemMemberLoad.MemberId"/> — Id КонЭ снимка).</summary>
    public IReadOnlyList<FemMemberLoad> MemberLoads { get; init; } = [];
    /// <summary>Нагрузки на КЭ (цели — КЭ, КонЭ, группы снимка).</summary>
    public IReadOnlyList<FemElementLoad> ElementLoads { get; init; } = [];
    public IReadOnlyList<FemMeshNodeLoad> MeshNodeLoads { get; init; } = [];
    /// <summary>Узловые нагрузки схемы (<see cref="FemNodeLoad.NodeId"/> — Id узла снимка).</summary>
    public IReadOnlyList<FemNodeLoad> NodeLoads { get; init; } = [];
    public IReadOnlyList<FemKinematicLoad> KinematicLoads { get; init; } = [];
    /// <summary>Группы — для нагрузок с целью «группа».</summary>
    public IReadOnlyList<FemMemberGroup> Groups { get; init; } = [];
    /// <summary>Пружины на узлах сетки полосы и на узлах схемы.</summary>
    public IReadOnlyList<FemSpring> Springs { get; init; } = [];
    /// <summary>Закрепления узлов сетки полосы.</summary>
    public IReadOnlyList<FemMeshNodeSupport> MeshNodeSupports { get; init; } = [];
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
    /// <summary>Копии загружений родителя (Id родителя).</summary>
    public List<FemLoadCase> LoadCases { get; } = [];
    /// <summary>Копии определений родителя (выражения — на Id загружений родителя).</summary>
    public List<FemLoadDefinition> LoadDefinitions { get; } = [];
    /// <summary>Нагрузки на КЭ с целью «КонЭ фрагмента» (поднятые равномерные и собственный вес).</summary>
    public List<FemElementLoad> ElementLoads { get; } = [];
    public List<FragmentMemberLoad> MemberLoads { get; } = [];
    public List<FragmentNodeLoad> NodeLoads { get; } = [];
    public List<FragmentKinematicLoad> KinematicLoads { get; } = [];
    /// <summary>Пружины на узлах схемы фрагмента (<see cref="FemSpringTargetKinds.Node"/>, тег узла фрагмента).</summary>
    public List<FemSpring> Springs { get; } = [];
    public List<FragmentFieldLoad> FieldLoads { get; } = [];
    /// <summary>Опорная сетка: КЭ родителя полосы.</summary>
    public List<FragmentRefElement> RefElements { get; } = [];
    public List<FemValidationDiagnostic> Diagnostics { get; } = [];
    /// <summary>Есть отказ — схему не создавать.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.IsError);
}
