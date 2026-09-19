using CScore;
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
}
