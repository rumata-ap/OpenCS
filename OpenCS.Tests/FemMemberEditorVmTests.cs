using CScore.Fem;
using OpenCS.Services;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Панель группы: у групп КЭ импортированной схемы тип не выбирается (вид — по составу), набор усилий не
/// привязывается; у группы пластин список сечений — пластинчатые, даже если тип не задан (кБ ЛИРЫ).
/// </summary>
public class FemMemberEditorVmTests
{
    static void WithApp(Action<AppViewModel, FemSchema> test)
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-group-editor-{Guid.NewGuid():N}.db");
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path);
        try
        {
            var schema = new FemSchema { Tag = "Схема Лира", SourceType = "lira" };
            app.db.SaveFemSchema(schema);
            app.db.SaveFemMeshSnapshot(schema.Id,
                [new FemMeshNode { NodeTag = "1" }, new FemMeshNode { NodeTag = "2", Z = 1 },
                 new FemMeshNode { NodeTag = "3", X = 1, Z = 1 }, new FemMeshNode { NodeTag = "4", X = 1 }],
                [new FemElement { ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]" },
                 new FemElement { ElemTag = "2", ElemType = "shell", NodeIdsJson = "[1,4,3,2]" }]);
            test(app, schema);
        }
        finally
        {
            app.db.Dispose();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void MeshPlateGroup_PlateSectionsWithoutTypeAndForceSet() => WithApp((app, schema) =>
    {
        var vm = new FemMemberEditorVM(new FemMemberGroup { SchemaId = schema.Id, Tag = "СТЕНА №1", MemberTagsJson = "[2]" }, app);

        Assert.True(vm.IsMeshGroup);
        Assert.False(vm.CanChooseType);
        Assert.False(vm.CanBindForceSet);
        Assert.True(vm.IsPlateType);
        Assert.Same(app.PlateSections, vm.AllSections);
        Assert.NotEmpty(vm.Composition);   // текст — из ресурсов, в тестах они не загружены
    });

    [Fact]
    public void MeshBarGroup_BarSections() => WithApp((app, schema) =>
    {
        var vm = new FemMemberEditorVM(new FemMemberGroup { SchemaId = schema.Id, Tag = "Колонны", MemberTagsJson = "[1]" }, app);

        Assert.False(vm.CanChooseType);
        Assert.False(vm.IsPlateType);
        Assert.Same(app.CrossSections, vm.AllSections);
    });

    /// <summary>Смешанная группа КЭ: вид не определить — тип выбирается вручную, как раньше.</summary>
    [Fact]
    public void MeshMixedGroup_TypeChosenManually() => WithApp((app, schema) =>
    {
        var vm = new FemMemberEditorVM(new FemMemberGroup { SchemaId = schema.Id, Tag = "Всё", MemberTagsJson = "[1,2]" }, app);

        Assert.True(vm.CanChooseType);
        vm.MemberType = "Стена";
        Assert.True(vm.IsPlateType);
    });
}
