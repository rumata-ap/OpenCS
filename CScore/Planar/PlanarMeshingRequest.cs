namespace CScore.Planar;

/// <summary>Входные данные построения сетки одного плоского региона.</summary>
public sealed record PlanarMeshingRequest(PlanarRegion Region, PlanarMeshSettings Settings);

/// <summary>Независимый от конкретного генератора контракт построения плоской сетки.</summary>
public interface IPlanarMesher
{
    Task<PlanarMeshSnapshot> BuildAsync(PlanarMeshingRequest request, CancellationToken cancellationToken = default);
}
