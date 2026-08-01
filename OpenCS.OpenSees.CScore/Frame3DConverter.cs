using CScore.Planar;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Переводит ортонормированный локальный базис PlanarRegion в ShellFrame OpenSees.
/// Прямое покомпонентное копирование — оба типа уже ортонормированы своими конструкторами
/// (Frame3D.Validate() / ShellFrame.Validate()), пересчёт не нужен.</summary>
public static class Frame3DConverter
{
    public static ShellFrame ToShellFrame(this Frame3D frame) => new(
        new ShellVector3(frame.LocalX.X, frame.LocalX.Y, frame.LocalX.Z),
        new ShellVector3(frame.LocalY.X, frame.LocalY.Y, frame.LocalY.Z),
        new ShellVector3(frame.LocalZ.X, frame.LocalZ.Y, frame.LocalZ.Z));
}
