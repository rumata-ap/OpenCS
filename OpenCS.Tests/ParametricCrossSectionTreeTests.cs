using CScore;
using OpenCS;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверяет, что параметрический узел дерева остаётся wrapper-ом общего CrossSection.</summary>
public sealed class ParametricCrossSectionTreeTests
{
    [Fact]
    public void WrapperKeepsUnderlyingSectionAndMaterialAreas()
    {
        var area = new MaterialArea { Tag = "бетон" };
        var section = new CrossSection { Tag = "КС-1", Areas = [area] };
        var state = new ParametricRcSectionState(
            ParametricRcDefinitionLoadStatus.Supported, false, null);

        var item = new ParametricCrossSectionTreeItem(section, state);

        Assert.Same(section, item.Section);
        Assert.Equal(section.Tag, item.Tag);
        Assert.Single(item.Areas);
        Assert.Same(area, item.Areas[0]);
    }

    [Fact]
    public void FolderExposesTheSameItemsWithoutCreatingAnotherSectionPool()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<ParametricCrossSectionTreeItem>();
        var group = new ParametricCrossSectionTreeGroup(items);

        Assert.Same(items, group.Items);
        Assert.Empty(group.Items);
    }

    [Fact]
    public void SectionTreeContainsOnlyVisibleParametricSectionGroup()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-tree-{Guid.NewGuid():N}.db");
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path);
        try
        {
            Assert.Single(app.SectionTreeItems.Cast<object>()
                .OfType<ParametricCrossSectionTreeGroup>());
            Assert.Single(app.SectionTreeItems.Cast<object>()
                .OfType<ParametricSteelSectionTreeGroup>());
            Assert.Equal(6, app.SectionTreeItems.Count);
        }
        finally
        {
            app.db.Dispose();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(path + suffix); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    [Fact]
    public void SteelParametricSectionGoesToSteelGroupUntilDetached()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-tree-steel-{Guid.NewGuid():N}.db");
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path);
        try
        {
            var section = new CrossSection { Num = 1, Tag = "МК-1" };
            var service = new ParametricSteelSectionProjectService(app.db);
            service.GenerateAndSave(section, CScore.ParametricSteel.ParametricSteelSectionDefinition.Pipe(0.159, 0.006)
                with { Tag = "МК-1" });
            if (!app.CrossSections.Contains(section)) app.CrossSections.Add(section);

            app.RefreshSectionLiveCollections();
            Assert.Same(section, Assert.Single(app.ParametricSteelSectionsLive).Section);
            Assert.DoesNotContain(section, app.OrdinaryFiberSectionsLive);
            Assert.Empty(app.ParametricFiberSectionsLive);

            service.Detach(section);
            app.RefreshSectionLiveCollections();
            Assert.Empty(app.ParametricSteelSectionsLive);
            Assert.Contains(section, app.OrdinaryFiberSectionsLive);
        }
        finally
        {
            app.db.Dispose();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(path + suffix); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
