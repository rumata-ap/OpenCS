using System.IO;
using CScore;
using Microsoft.Data.Sqlite;
using netDxf;
using netDxf.Entities;
using netDxf.Units;
using OpenCS.Services.Cad;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Раздел «Импорт», 9а: чтение DXF, единицы чертежа, пул геометрии без дубликатов, окна выбора.</summary>
[Collection("Loc language")]
public sealed class ImportGeometryTests : IDisposable
{
    public ImportGeometryTests() => Loc.SetLanguage(Loc.Russian);
    public void Dispose() => Loc.SetLanguage(null);

    static string Temp(string ext) => Path.Combine(Path.GetTempPath(), $"opencs-import-{Guid.NewGuid():N}{ext}");

    static void Delete(params string[] paths)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in paths)
            try { if (File.Exists(p)) File.Delete(p); } catch (IOException) { }
    }

    /// <summary>Квадрат 300×300 (единицы чертежа) и окружность r = 10 в точке (50, 50).</summary>
    static string WriteSheet(DrawingUnits units)
    {
        var doc = new DxfDocument();
        doc.DrawingVariables.InsUnits = units;
        doc.Entities.Add(new Polyline2D(new List<netDxf.Vector2> { new(0, 0), new(300, 0), new(300, 300), new(0, 300) }, true));
        doc.Entities.Add(new Circle(new netDxf.Vector2(50, 50), 10));
        string path = Temp(".dxf");
        doc.Save(path);
        return path;
    }

    [Fact]
    public void DxfFile_DamagedFile_GivesReadableError()
    {
        string path = Temp(".dxf");
        File.WriteAllText(path, "это не DXF\n0\nSECTION\nмусор");
        try
        {
            var ex = Assert.Throws<DxfFileException>(() => DxfFile.Load(path));
            Assert.Contains(Path.GetFileName(path), ex.Message);
        }
        finally { Delete(path); }
    }

    [Theory]
    [InlineData(DrawingUnits.Millimeters, 0.001)]
    [InlineData(DrawingUnits.Meters, 1.0)]
    [InlineData(DrawingUnits.Unitless, null)]
    [InlineData(DrawingUnits.Inches, null)]
    public void DxfFile_UnitsFromInsUnits(DrawingUnits units, double? expected)
    {
        string path = WriteSheet(units);
        try { Assert.Equal(expected, DxfFile.FileUnitMeters(DxfFile.Load(path))); }
        finally { Delete(path); }
    }

    [Fact]
    public void Contours_MetersScale_AndRepeatedImport_NoDuplicates()
    {
        string dxf = WriteSheet(DrawingUnits.Meters);
        string dbPath = Temp(".db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var doc = DxfFile.Load(dxf);
            var first = DxfGeometryImport.Contours(db, doc, 1.0, "лист");
            Assert.Equal(new GeometryImportResult(1, 0, 0), first);
            var c = Assert.Single(db.Contours);
            Assert.Equal(300, c.Points.Max(p => p.X), 9); // единица = метр, без деления на 1000

            var again = DxfGeometryImport.Contours(db, doc, 1.0, "лист");
            Assert.Equal(new GeometryImportResult(0, 0, 1), again);
            Assert.Single(db.Contours);

            // Другой набор геометрии — не дубликат.
            Assert.Equal(1, DxfGeometryImport.Contours(db, doc, 1.0, "лист 2").Added);
        }
        finally { Delete(dxf, dbPath); }
    }

    [Fact]
    public void Circles_MillimetersScale_AndRepeatedImport_NoDuplicates()
    {
        string dxf = WriteSheet(DrawingUnits.Millimeters);
        string dbPath = Temp(".db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var doc = DxfFile.Load(dxf);
            Assert.Equal(new GeometryImportResult(1, 0, 0), DxfGeometryImport.Circles(db, doc, 0.001, "лист"));
            var circle = Assert.Single(db.Circles);
            Assert.Equal(0.01, circle.Radius, 12);
            Assert.Equal(0.05, circle.X, 12);
            Assert.Equal(new GeometryImportResult(0, 0, 1), DxfGeometryImport.Circles(db, doc, 0.001, "лист"));
            Assert.Single(db.Circles);
        }
        finally { Delete(dxf, dbPath); }
    }

    [Fact]
    public void CirclesCsv_RepeatedImport_NoDuplicates_SkippedCounted()
    {
        string dbPath = Temp(".db");
        try
        {
            using var db = new DatabaseService(dbPath);
            string[] lines = ["Tag;X;Y;Radius", "Ø25;0,1;-0,2;0,0125", "bad;x;0;1"];
            var parsed = CircleCsv.Parse(lines, out int skipped);
            Assert.Equal(new GeometryImportResult(1, 1, 0), DxfGeometryImport.Circles(db, parsed, skipped));
            parsed = CircleCsv.Parse(lines, out skipped);
            Assert.Equal(new GeometryImportResult(0, 1, 1), DxfGeometryImport.Circles(db, parsed, skipped));
            Assert.Single(db.Circles);
        }
        finally { Delete(dbPath); }
    }

    [Theory]
    [InlineData("5", true)]
    [InlineData("2,5", true)]
    [InlineData("2.5", true)]
    [InlineData("abc", false)]
    [InlineData("150", false)]
    [InlineData("-1", false)]
    [InlineData("", false)]
    public void ListChoiceDialog_NumberCheckedWithMessage(string text, bool ok)
    {
        Assert.Equal(ok, Views.ListChoiceDialog.TryReadNumber(text, 0, 100, out _, out string? error));
        if (ok) Assert.Null(error);
        else Assert.Contains("100", error);
    }

    [Fact]
    public void PlanarGeometryDxf_UnitsFromFile_ThenUserChoiceRemembered()
    {
        string dxf = WriteSheet(DrawingUnits.Meters);
        try
        {
            var settings = new AcadImportSettings { ScaleFactor = 0.001 };
            int saved = 0;
            var vm = new PlanarGeometryDxfImportVM(new FixedFile(dxf), settings, () => saved++);
            Assert.Equal(0, vm.UnitIdx); // запомненные мм
            vm.OpenFileCommand.Execute(null);
            Assert.Equal(2, vm.UnitIdx); // $INSUNITS = м
            Assert.Equal(0, saved);      // единицы файла не перезаписывают выбор пользователя

            vm.UnitIdx = 1;
            Assert.Equal(0.01, settings.ScaleFactor);
            Assert.Equal(1, saved);
        }
        finally { Delete(dxf); }
    }

    [Fact]
    public void LiraBlocksDialog_ConvertAvailableOnlyWithSelection()
    {
        var vm = new LiraBlocksToMembersVM([new CScore.Import.LiraBlockInfo(1, "Колонна", "1", "К1", ["10", "11"])], [], []);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(vm.HasSelection);
        vm.Rows[0].IsSelected = true;
        Assert.True(vm.HasSelection);
        Assert.Contains(nameof(LiraBlocksToMembersVM.HasSelection), changed);
    }

    sealed class FixedFile(string path) : OpenCS.Services.IFileDialogService
    {
        public string? OpenFile(string? filter = null, string? title = null) => path;
        public string? SaveFile(string? filter = null, string? defaultExt = null, string? title = null) => null;
        public string? SelectFolder(string? title = null, string? initialDirectory = null) => null;
    }
}
