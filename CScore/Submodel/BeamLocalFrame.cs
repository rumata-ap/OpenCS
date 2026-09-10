using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Поперечные оси стержня; продольная совпадает с направлением цепочки.</summary>
public sealed record BeamLocalFrame(PlanarVector3 LocalY, PlanarVector3 LocalZ);

/// <summary>Строит локальный кадр стержня в сборке, где доступна принятая конвенция осей.</summary>
public interface IBeamLocalFrameProvider
{
    BeamLocalFrame Frame(PlanarVector3 axisDirection, double betaDeg);
}
