using System.Globalization;
using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Форма «Фрагмент по отметкам…»: разбор отметок и имени, строки отчёта сборки.</summary>
public sealed class StoreyFragmentFormTests : IDisposable
{
    static readonly CultureInfo Ru = Comma();

    /// <summary>Запятая — десятичный разделитель (тесты могут идти в инвариантной глобализации).</summary>
    static CultureInfo Comma()
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        c.NumberFormat.NumberDecimalSeparator = ",";
        return c;
    }
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-form-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    [Fact]
    public void DefaultName_ParentTagAndLevels()
    {
        Assert.Equal("Каркас · 1,5…4,5", StoreyFragmentForm.DefaultName("Каркас", 1.5, 4.5, Ru));
        Assert.Equal("Каркас · 1.5…4.5", StoreyFragmentForm.DefaultName("Каркас", 1.5, 4.5, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("1,5", "4.5")]
    [InlineData("1.5", "4,5")]
    public void TryRead_CommaOrDot(string z1, string z2)
    {
        var request = StoreyFragmentForm.TryRead(z1, z2, " Этаж 2 ", out var error);
        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal((1.5, 4.5, "Этаж 2"), (request.Z1, request.Z2, request.Name));
        Assert.Equal(0.001, request.ToleranceM);
    }

    [Theory]
    [InlineData("x", "4,5", "Этаж", "StoreyFragmentLevelInvalid")]
    [InlineData("1,5", "", "Этаж", "StoreyFragmentLevelInvalid")]
    [InlineData("4,5", "1,5", "Этаж", "StoreyFragmentLevelsOrder")]
    [InlineData("1,5", "1,5", "Этаж", "StoreyFragmentLevelsOrder")]
    [InlineData("1,5", "4,5", "  ", "StoreyFragmentNameEmpty")]
    public void TryRead_Invalid_ErrorKey(string z1, string z2, string name, string key)
    {
        Assert.Null(StoreyFragmentForm.TryRead(z1, z2, name, out var error));
        Assert.Equal(Loc.S(key), error);
    }

    [Fact]
    public void ReportRows_ErrorsFirst_WithLevel()
    {
        var rows = StoreyFragmentReportRows.From(
        [
            new FemValidationDiagnostic("w", "предупреждение", false),
            new FemValidationDiagnostic("e", "ошибка", true),
        ]);
        Assert.Equal(["ошибка", "предупреждение"], rows.Select(r => r.Message));
        Assert.Equal([Loc.S("StoreyFragmentReportError"), Loc.S("StoreyFragmentReportWarning")], rows.Select(r => r.Level));
        Assert.Equal([true, false], rows.Select(r => r.IsError));
        Assert.Equal($"{Loc.S("StoreyFragmentReportError")}\tошибка{Environment.NewLine}" +
            $"{Loc.S("StoreyFragmentReportWarning")}\tпредупреждение", StoreyFragmentReportRows.ToText(rows));
    }

    [Fact]
    public void MeshZRange_ParentMesh()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        Assert.Equal((0.0, 9.0), db.GetFemMeshZRange(parent.Id));
        var empty = new FemSchema { Tag = "Пусто" };
        db.SaveFemSchema(empty);
        Assert.Null(db.GetFemMeshZRange(empty.Id));
    }
}
