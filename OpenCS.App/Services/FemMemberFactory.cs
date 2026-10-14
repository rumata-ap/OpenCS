using CScore;
using CScore.Fem;

namespace OpenCS.Services;

/// <summary>Создаёт конструктивные FEM-стержни с уже материализованным GJ.</summary>
public sealed class FemMemberFactory
{
    readonly FemGjDefaultResolver _gjResolver;

    /// <summary>Создаёт фабрику с resolver-ом, принадлежащим текущему редактору.</summary>
    public FemMemberFactory(FemGjDefaultResolver gjResolver)
        => _gjResolver = gjResolver ?? throw new ArgumentNullException(nameof(gjResolver));

    /// <summary>
    /// Создаёт двухузловой beam-member. GJ сохраняется как обычное ручное значение
    /// в Н·м², поэтому downstream OpenSees-код не требует отдельной стратегии.
    /// </summary>
    public FemMember CreateBeam(
        int schemaId,
        string elemTag,
        string nodeIdsJson,
        int? crossSectionId,
        CrossSection? section)
    {
        var resolution = _gjResolver.Resolve(section);
        return new FemMember
        {
            SchemaId = schemaId,
            ElemTag = elemTag,
            ElemType = "beam",
            NodeIdsJson = nodeIdsJson,
            CrossSectionId = crossSectionId,
            GjStrategy = "manual",
            GjManualValue = resolution.GjNm2,
            GjTorsionTaskId = null
        };
    }
}
