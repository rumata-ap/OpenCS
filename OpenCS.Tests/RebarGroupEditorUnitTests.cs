using System;
using System.Collections.Generic;
using System.IO;
using OpenCS;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public sealed class RebarGroupEditorUnitTests
{
    sealed class EditorContext : IDisposable
    {
        readonly string _databasePath;

        public AppViewModel App { get; }
        public RebarGroupEditorVM Editor { get; }

        public EditorContext()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(), $"opencs-rebar-group-{Guid.NewGuid():N}.db");
            App = new AppViewModel(
                new LogService(), new NullFileDialogService(), _databasePath);
            Editor = new RebarGroupEditorVM(null, App);
        }

        public void Dispose()
        {
            App.db.Dispose();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(_databasePath + suffix); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    [Fact]
    public void BarItem_MillimeterProperties_ConvertBothWaysAndNotify()
    {
        var bar = new BarItem { X = 0.125, Y = -0.030, Diameter = 0.016 };
        var changed = new List<string>();
        bar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changed.Add(e.PropertyName);
        };

        Assert.Equal(125.0, bar.XMm, 10);
        Assert.Equal(-30.0, bar.YMm, 10);

        bar.XMm = 125.4;
        bar.YMm = -30.2;

        Assert.Equal(0.1254, bar.X, 10);
        Assert.Equal(-0.0302, bar.Y, 10);
        Assert.Contains(nameof(BarItem.XMm), changed);
        Assert.Contains(nameof(BarItem.YMm), changed);

        changed.Clear();
        bar.X = 0.2;
        bar.Y = 0.04;

        Assert.Equal(200.0, bar.XMm, 10);
        Assert.Equal(40.0, bar.YMm, 10);
        Assert.Contains(nameof(BarItem.XMm), changed);
        Assert.Contains(nameof(BarItem.YMm), changed);
    }

    [Fact]
    public void EdgeItem_OffsetMm_ConvertsAndPreservesNonNegativeConstraint()
    {
        var edge = new EdgeItem { Offset = 0.025 };
        var changed = new List<string>();
        edge.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changed.Add(e.PropertyName);
        };

        Assert.Equal(25.0, edge.OffsetMm, 10);

        edge.OffsetMm = 35.6;

        Assert.Equal(0.0356, edge.Offset, 10);
        Assert.Contains(nameof(EdgeItem.OffsetMm), changed);

        changed.Clear();
        edge.Offset = 0.0123;

        Assert.Equal(12.3, edge.OffsetMm, 10);
        Assert.Contains(nameof(EdgeItem.OffsetMm), changed);

        changed.Clear();
        edge.OffsetMm = -1.0;

        Assert.Equal(0.0, edge.Offset, 10);
        Assert.Equal(0.0, edge.OffsetMm, 10);
        Assert.Contains(nameof(EdgeItem.OffsetMm), changed);
    }

    [Fact]
    public void RebarGroupEditorVM_GeometryMillimeterProperties_ConvertAndNotify()
    {
        using var context = new EditorContext();
        var editor = context.Editor;
        var changed = new List<string>();
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changed.Add(e.PropertyName);
        };

        editor.GlobalOffsetMm = 35.6;
        editor.OffsetStepMm = 1.2;
        editor.FillArcRadiusMm = 250.0;

        Assert.Equal(0.0356, editor.GlobalOffset, 10);
        Assert.Equal(0.0012, editor.OffsetStep, 10);
        Assert.Equal(0.2500, editor.FillArcRadius, 10);
        Assert.Contains(nameof(RebarGroupEditorVM.GlobalOffsetMm), changed);
        Assert.Contains(nameof(RebarGroupEditorVM.OffsetStepMm), changed);
        Assert.Contains(nameof(RebarGroupEditorVM.FillArcRadiusMm), changed);

        changed.Clear();
        editor.GlobalOffset = 0.040;
        editor.OffsetStep = 0.002;
        editor.FillArcRadius = 0.310;

        Assert.Equal(40.0, editor.GlobalOffsetMm, 10);
        Assert.Equal(2.0, editor.OffsetStepMm, 10);
        Assert.Equal(310.0, editor.FillArcRadiusMm, 10);
        Assert.Contains(nameof(RebarGroupEditorVM.GlobalOffsetMm), changed);
        Assert.Contains(nameof(RebarGroupEditorVM.OffsetStepMm), changed);
        Assert.Contains(nameof(RebarGroupEditorVM.FillArcRadiusMm), changed);
    }
}
