namespace CScore.Planar;

/// <summary>Преобразования vectors и points между Frame3D.</summary>
public static class PlanarBoundaryFrameConverter
{
    /// <summary>Преобразует локальный vector в глобальную систему.</summary>
    public static PlanarVector3 ToGlobalVector(Frame3D frame, PlanarVector3 local) =>
        frame.LocalX * local.X + frame.LocalY * local.Y + frame.LocalZ * local.Z;

    /// <summary>Преобразует глобальный vector в локальную систему.</summary>
    public static PlanarVector3 ToLocalVector(Frame3D frame, PlanarVector3 global) => new(
        global.Dot(frame.LocalX),
        global.Dot(frame.LocalY),
        global.Dot(frame.LocalZ));

    /// <summary>Преобразует локальную точку frame в глобальную систему.</summary>
    public static PlanarVector3 ToGlobalPoint(Frame3D frame, PlanarVector3 local) =>
        frame.Origin + ToGlobalVector(frame, local);

    /// <summary>Преобразует глобальную точку в локальную систему frame.</summary>
    public static PlanarVector3 ToLocalPoint(Frame3D frame, PlanarVector3 global)
    {
        var relative = global - frame.Origin;
        return ToLocalVector(frame, relative);
    }

    /// <summary>Переносит момент из source reference point в target reference point.</summary>
    public static PlanarVector3 TranslateMoment(
        PlanarVector3 momentAtSource,
        PlanarVector3 forceGlobal,
        PlanarVector3 sourcePointGlobal,
        PlanarVector3 targetPointGlobal) =>
        momentAtSource + (sourcePointGlobal - targetPointGlobal).Cross(forceGlobal);
}
