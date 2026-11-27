using System.IO;
using netDxf;
using OpenCS.Utilites;

namespace OpenCS.Services.Cad;

/// <summary>Файл DXF не прочитан: текст для пользователя (имя файла и причина).</summary>
public sealed class DxfFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Общее для всех импортов DXF: загрузка с понятной ошибкой (повреждённый файл, версия старше AutoCAD 2000) и
/// единицы чертежа — мм, см, м; единицы берутся из <c>$INSUNITS</c> файла, если заданы.
/// </summary>
public static class DxfFile
{
    /// <summary>Метров в единице для пунктов <see cref="UnitLabels"/>.</summary>
    public static readonly double[] UnitMeters = [0.001, 0.01, 1.0];

    /// <summary>Подписи единиц: мм, см, м.</summary>
    public static List<string> UnitLabels() => [Loc.S("Unit_mm"), Loc.S("Unit_cm"), Loc.S("Unit_m")];

    /// <summary>Пункт единиц для множителя «метров в единице»; −1 — не мм/см/м.</summary>
    public static int UnitIndexOf(double? metersPerUnit) =>
        metersPerUnit is double m ? Array.FindIndex(UnitMeters, u => Math.Abs(u - m) < 1e-12) : -1;

    /// <summary>Читает DXF; ошибка — <see cref="DxfFileException"/> с понятным текстом.</summary>
    public static DxfDocument Load(string path)
    {
        string name = Path.GetFileName(path);
        DxfDocument? doc;
        try
        {
            doc = DxfDocument.Load(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new DxfFileException(string.Format(Loc.S("DxfLoadFailed"), name, ex.Message), ex);
        }
        return doc ?? throw new DxfFileException(string.Format(Loc.S("DxfLoadUnsupported"), name));
    }

    /// <summary>Метров в единице чертежа по <c>$INSUNITS</c>, если это мм, см или м; иначе null.</summary>
    public static double? FileUnitMeters(DxfDocument doc) =>
        DxfSnapshotReader.MetersPerUnit(doc.DrawingVariables.InsUnits) is double m && UnitIndexOf(m) >= 0 ? m : null;
}
