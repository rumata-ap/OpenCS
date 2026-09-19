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
            Assert.Equal(5, app.SectionTreeItems.Count);
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
