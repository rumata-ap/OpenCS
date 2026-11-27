using System.CommandLine;
using OpenCS.Services.Cad;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>
/// Пул геометрии сечений из файлов: <c>import contours</c> (замкнутые полилинии DXF) и <c>import circles</c> (окружности
/// DXF или CSV «Tag;X;Y;Radius» в метрах) — служба меню «Импорт → DXF» (<see cref="DxfGeometryImport"/>). Единицы DXF —
/// <c>--units</c>, иначе <c>$INSUNITS</c> файла; не заданы нигде — отказ (молча считать мм нельзя).
/// </summary>
sealed partial class CliApp
{
    readonly Argument<string> _geometryFile = new("file");
    readonly Option<string?> _dxfUnits = new("--units");
    readonly Option<string?> _geometrySet = new("--set");

    void DescribeGeometryOptions()
    {
        _geometryFile.Description = Loc.S("CliArgGeometryFile");
        _dxfUnits.Description = Loc.S("CliOptDxfUnits");
        _dxfUnits.AcceptOnlyFromAmong("mm", "cm", "m");
        _geometrySet.Description = Loc.S("CliOptGeometrySet");
    }

    Command ImportContoursCommand() => EditCommand("contours", "CliCmdImportContours", c => ImportGeometryAsync(c, circles: false),
        _geometryFile, _dxfUnits, _geometrySet);

    Command ImportCirclesCommand() => EditCommand("circles", "CliCmdImportCircles", c => ImportGeometryAsync(c, circles: true),
        _geometryFile, _dxfUnits, _geometrySet);

    Task<int> ImportGeometryAsync(CliCall call, bool circles)
    {
        string file = call.Parse.GetValue(_geometryFile)!;
        string path = Path.GetFullPath(file);
        if (!File.Exists(path)) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrFileNotFound"), file));
        string ext = Path.GetExtension(path).ToLowerInvariant();
        bool csv = circles && ext == ".csv";
        if (!csv && ext != ".dxf") throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrImportFormat"), Path.GetFileName(path)));
        string set = call.Parse.GetValue(_geometrySet) ?? Path.GetFileNameWithoutExtension(path);
        // Круги CSV — как в окне: без набора геометрии, если --set не задан.
        string? shownSet = csv && call.Parse.GetValue(_geometrySet) == null ? null : set;

        GeometryImportResult? result = null;
        double? mpu = null;
        var code = Edit(call, session =>
        {
            var db = session.Context.Db;
            if (csv)
            {
                var parsed = CircleCsv.Parse(File.ReadAllLines(path), out int skipped);
                if (call.Parse.GetValue(_geometrySet) != null) foreach (var c in parsed) c.GeometrySet = set;
                result = DxfGeometryImport.Circles(db, parsed, skipped);
                return;
            }
            netDxf.DxfDocument dxf;
            try { dxf = DxfFile.Load(path); }
            catch (DxfFileException ex) { throw new CliException(CliExitCode.Usage, ex.Message); }
            mpu = call.Parse.GetValue(_dxfUnits) switch
            {
                "mm" => 0.001,
                "cm" => 0.01,
                "m" => 1.0,
                _ => DxfFile.FileUnitMeters(dxf)
                     ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrDxfUnits"), Path.GetFileName(path))),
            };
            result = circles ? DxfGeometryImport.Circles(db, dxf, mpu.Value, set) : DxfGeometryImport.Contours(db, dxf, mpu.Value, set);
        });
        var r = result!;
        if (call.Json)
            CliOutput.WriteJson(call.Out, new { r.Added, r.Skipped, r.Duplicates, GeometrySet = shownSet, MetersPerUnit = mpu });
        else
            call.Out.WriteLine(string.Format(Loc.S(circles ? "CliCirclesImported" : "CliContoursImported"), r.Added, r.Skipped,
                r.Duplicates, shownSet ?? Loc.S("CliNoGeometrySet")));
        return code;
    }
}
