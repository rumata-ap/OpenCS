using CScore.PlateStrip;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Срез 8d: ViewModel постановки и результата нелинейного прогона полосы.</summary>
public sealed class StripNonlinearRunVmTests
{
    static void WithApp(Action<AppViewModel, EquivalentSection> test)
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-strip-run-vm-{Guid.NewGuid():N}.db");
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path);
        try
        {
            var section = new EquivalentSection
            {
                Tag = "equiv-vm", SourceSchemaId = 1, SourceRegionId = 1, SourcePlateSectionId = 1,
                WidthIntegrationPoints = 2, InputFingerprint = "fp-1", IsCalculable = true,
                BeamTangent = new double[3, 3],
                Strip = new PlateStripBeamAnalogy
                {
                    Id = "s", SourceRegionId = 1, ExplicitWidthM = 1.0, Fingerprint = "f",
                    Geometry = new PlateStripGeometry { LengthM = 6.0 }
                }
            };
            app.db.SaveEquivalentSection(section);
            test(app, section);
        }
        finally
        {
            app.db.Dispose();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    static StripNonlinearRunResult Saved(AppViewModel app, EquivalentSection section, string fingerprint)
    {
        var result = new StripNonlinearRunResult
        {
            EquivalentSectionId = section.Id,
            CreatedUtc = DateTime.UtcNow,
            EquivalentInputFingerprint = fingerprint,
            IsConverged = true,
            AchievedLoadFactor = 1.0,
            TotalIterations = 9,
            LengthM = 6.0,
            StationFractions = [0.0, 0.5, 1.0],
            BeamResultants = [[0, 0, 0], [0, -45.0, 0], [0, 0, 0]],
            BeamDeflections = [0.0, -0.01, 0.0],
            MaxDeflectionM = 0.01,
            FiberChecked = true,
            FiberResultants = [[0, 0, 0], [0, -45.1, 0], [0, 0, 0]],
            FiberDeflections = [0.0, -0.0101, 0.0],
            FiberMaxDeflectionM = 0.0101,
            FiberMomentMismatch = 0.002,
            FiberDeflectionMismatch = 0.01,
            Input = new StripNonlinearRunInput
            {
                EndCondition = StripBeamEndCondition.Fixed, LoadSteps = 5, ElementCount = 4, FiberCheck = true,
                Loads = [new StripNonlinearLoadItem { Value = -20.0 }]
            }
        };
        app.db.SaveStripNonlinearResult(result);
        return result;
    }

    [Fact]
    public void NoSavedResult_StartsWithDefaultInputAndNoRows() => WithApp((app, section) =>
    {
        var vm = new StripNonlinearRunVM(section, app);

        Assert.Single(vm.Loads);
        Assert.Equal(-10.0, vm.Loads[0].Value);
        Assert.Equal(0, vm.StartConditionIndex);
        Assert.Equal(8, vm.LoadSteps);
        Assert.False(vm.FiberCheck);
        Assert.Empty(vm.Rows);
        Assert.Equal(System.Windows.Visibility.Collapsed, vm.ResultVisibility);
    });

    [Fact]
    public void SavedResult_RestoresInputAndRows_AndDetectsStaleness() => WithApp((app, section) =>
    {
        Saved(app, section, "fp-1");

        var fresh = new StripNonlinearRunVM(section, app);
        Assert.Equal(1, fresh.EndConditionIndex);
        Assert.Equal(5, fresh.LoadSteps);
        Assert.Equal(4, fresh.ElementCount);
        Assert.True(fresh.FiberCheck);
        Assert.Equal(-20.0, Assert.Single(fresh.Loads).Value);
        Assert.Equal(3, fresh.Rows.Count);
        Assert.Equal(3.0, fresh.Rows[1].X, 12);
        Assert.Equal(-45.0, fresh.Rows[1].My, 12);
        Assert.Equal(System.Windows.Visibility.Visible, fresh.ResultVisibility);
        Assert.Equal(System.Windows.Visibility.Visible, fresh.FiberVisibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, fresh.StaleVisibility);

        section.InputFingerprint = "fp-changed";
        var stale = new StripNonlinearRunVM(section, app);
        Assert.Equal(System.Windows.Visibility.Visible, stale.StaleVisibility);
    });

    [Fact]
    public void EditingInput_DoesNotChangeStoredResultInput() => WithApp((app, section) =>
    {
        var stored = Saved(app, section, "fp-1");
        var vm = new StripNonlinearRunVM(section, app);

        vm.LoadSteps = 20;
        vm.Loads[0].Value = -99.0;
        vm.AddLoadCommand.Execute(null);

        Assert.Equal(5, stored.Input.LoadSteps);
        Assert.Equal(-20.0, stored.Input.Loads[0].Value);
        Assert.Equal(2, vm.Input.Loads.Count);
        vm.RemoveLoadCommand.Execute(vm.Loads[1]);
        Assert.Single(vm.Input.Loads);
    });

    [Fact]
    public void LoadRow_ToIsDisabledForPointForce() => WithApp((app, section) =>
    {
        var vm = new StripNonlinearRunVM(section, app);
        var row = vm.Loads[0];
        Assert.True(row.ToEnabled);

        row.KindIndex = 1;
        Assert.False(row.ToEnabled);
        Assert.Equal(StripNonlinearLoadKind.PointPz, row.Item.Kind);
    });

    [Fact]
    public void InvalidInput_ShowsMessageAndDoesNotRun() => WithApp((app, section) =>
    {
        var vm = new StripNonlinearRunVM(section, app);
        vm.Loads[0].ToFraction = 0.0;

        vm.RunCommand.Execute(null);

        Assert.NotEqual("", vm.StatusText);
        Assert.Empty(app.db.StripNonlinearResults);
    });
}
