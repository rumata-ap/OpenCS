using System.IO;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Раздел «Настройки»: таблица полей, разбор ввода окна, проверка диапазонов, запись только без ошибок.</summary>
[Collection("Loc language")]
public sealed class ProjectSettingsAuthoringTests : IDisposable
{
    public ProjectSettingsAuthoringTests() => Loc.SetLanguage(Loc.Russian);
    public void Dispose() => Loc.SetLanguage(null);

    [Fact]
    public void Paths_Unique_AndEveryDocumentFieldRoundTripsOnDefaults()
    {
        Assert.Equal(ProjectSettingsAuthoring.Fields.Count, ProjectSettingsAuthoring.Fields.Select(f => f.Path).Distinct().Count());
        var s = new ProjectSettings();
        Assert.Empty(ProjectSettingsAuthoring.Validate(s));
        foreach (var f in ProjectSettingsAuthoring.Fields)
        {
            var copy = s.Clone();
            f.Set(copy, f.Get(s));
            Assert.Equal(f.Format(s), f.Format(copy));
        }
    }

    [Theory]
    [InlineData("sections.newton.tolerance", "0,05", true)]
    [InlineData("sections.newton.tolerance", "0.05", true)]
    [InlineData("sections.newton.tolerance", "0", false)]
    [InlineData("sections.newton.tolerance", "abc", false)]
    [InlineData("sections.gridDensity", "40", true)]
    [InlineData("sections.gridDensity", "4,5", false)]
    [InlineData("sections.gridDensity", "0", false)]
    [InlineData("sections.descendingBranch.sp63EtaMin", "1", false)]
    [InlineData("sections.shear.stationStepM", "0", true)]
    [InlineData("sections.shear.anchorageFactor", "0", false)]
    [InlineData("mesh.seamToleranceMm", "0,001", true)]
    [InlineData("mesh.seamToleranceMm", "1,5", false)]
    [InlineData("mesh.algorithm", "7", false)]
    [InlineData("import.lira.tonToKn", "9,80665", true)]
    [InlineData("import.lira.tonToKn", "9,5", false)]
    [InlineData("openSees.fiberStatesIntegrationPoints", "1, 5", true)]
    [InlineData("openSees.fiberStatesIntegrationPoints", "", true)]
    [InlineData("openSees.fiberStatesIntegrationPoints", "1, x", false)]
    [InlineData("gui.csfeaThreads", "0", true)]
    public void Parse_WindowText(string path, string text, bool ok)
    {
        var error = ProjectSettingsAuthoring.Field(path).Parse(text, out _);
        Assert.Equal(ok, error == null);
    }

    [Fact]
    public void RangeError_TellsAllowedRange()
    {
        Assert.Equal("Допустимо: 0 < x < 1.", ProjectSettingsAuthoring.Field("csfea.stiffnessFloor").Check(1.0));
        Assert.Equal("Допустимо: x > 0.", ProjectSettingsAuthoring.Field("loads.gammaF.permanent").Check(-1.0));
    }

    [Fact]
    public void Choices_MapStoredValues()
    {
        var s = new ProjectSettings();
        var geom = ProjectSettingsAuthoring.Field("openSees.geomTransf");
        geom.Set(s, "pDelta");
        Assert.Equal("PDelta", s.Calc.OpenSeesGeomTransfKind);
        Assert.Equal("pDelta", geom.Get(s));
        var units = ProjectSettingsAuthoring.Field("import.autocad.units");
        units.Set(s, "m");
        Assert.Equal(1.0, s.Acad.ScaleFactor);
        Assert.NotNull(units.Check("km"));
        var seam = ProjectSettingsAuthoring.Field("mesh.seamToleranceMm");
        seam.Set(s, 0.5);
        Assert.Equal(0.0005, s.Gmsh.SeamToleranceM, 12);
        var points = ProjectSettingsAuthoring.Field("openSees.fiberStatesIntegrationPoints");
        points.Set(s, new List<int> { 5, 1 });
        Assert.Equal("5,1", s.Calc.OpenSeesFiberStatesIntegrationPoints);
        Assert.Equal([1, 5], (IReadOnlyList<int>)points.Get(s)!);
    }

    [Fact]
    public void Save_WithError_WritesNothing_ThenWrites()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-settings-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DatabaseService(path))
            {
                var draft = ProjectSettings.Load(db);
                draft.Calc.ImportedBarRebarTolerancePercent = 7;
                draft.Calc.Sp20GammaFPermanent = 1.3;
                draft.Calc.NewtonTolerance = -1;
                var errors = ProjectSettingsAuthoring.Save(db, draft);
                Assert.Equal("sections.newton.tolerance", Assert.Single(errors).Path);
                Assert.Equal(1.1, db.LoadCalcSettings().Sp20GammaFPermanent);

                draft.Calc.NewtonTolerance = 0.05;
                Assert.Empty(ProjectSettingsAuthoring.Save(db, draft));
                var saved = db.LoadCalcSettings();
                Assert.Equal(1.3, saved.Sp20GammaFPermanent);
                Assert.Equal(7, saved.ImportedBarRebarTolerancePercent);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
