using System.IO;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.Views.Dialogs;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Раздел «Общие»: диалоги ввода числа и имени, имя группы схемы, файл языка пользователя.</summary>
[Collection("Loc language")]
public sealed class CommonDialogsTests : IDisposable
{
    public CommonDialogsTests() => Loc.SetLanguage(Loc.Russian);
    public void Dispose() => Loc.SetLanguage(null);

    // Без new Application(): глобальный экземпляр из потока теста ломает соседние STA-тесты окон.
    static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    [Fact]
    public void DoubleInput_Garbage_ErrorUnderField_NotAccepted() => RunSta(() =>
    {
        var dlg = new DoubleInputDialog("t", "dx", "dy");
        dlg.Box(2).Text = "abc";
        Assert.False(dlg.TryAccept());
        Assert.Equal("", dlg.ErrorText(1));
        Assert.Equal(Loc.S("NumberInputInvalid"), dlg.ErrorText(2));
        dlg.Box(2).Text = "1,5";
        Assert.Equal("", dlg.ErrorText(2)); // правка поля убирает ошибку
        Assert.True(dlg.TryAccept());
        Assert.Equal(1.5, dlg.Value2);
    });

    [Fact]
    public void DoubleInput_ValidateRejects_SecondField_KeepsInput() => RunSta(() =>
    {
        var dlg = new DoubleInputDialog("t", "kx", "ky", 1, 1)
        {
            Validate = (kx, ky) => kx <= 0 ? (1, "k>0") : ky <= 0 ? (2, "k>0") : null,
        };
        dlg.Box(1).Text = "2.5";
        dlg.Box(2).Text = "-1";
        Assert.False(dlg.TryAccept());
        Assert.Equal("k>0", dlg.ErrorText(2));
        Assert.Equal("-1", dlg.Box(2).Text);
        Assert.Equal("2.5", dlg.Box(1).Text);
    });

    [Fact]
    public void DoubleInput_SingleField_SecondHidden_DefaultsShortFormat() => RunSta(() =>
    {
        var dlg = new DoubleInputDialog("t", "d, мм", 16);
        Assert.Equal(System.Windows.Visibility.Collapsed, dlg.Box(2).Visibility);
        Assert.Equal("16", dlg.Box(1).Text);
        Assert.True(dlg.TryAccept());
        Assert.Equal(16, dlg.Value1);
        Assert.Equal(0, dlg.Value2);
    });

    [Fact]
    public void TextInput_Empty_AndValidate_ErrorUnderField() => RunSta(() =>
    {
        var dlg = new TextInputDialog("t", "имя", "  ") { Validate = s => s == "занято" ? "занято" : null };
        Assert.False(dlg.TryAccept());
        Assert.Equal(Loc.S("TextInputEmpty"), dlg.ErrorText);
        dlg.Box.Text = "занято";
        Assert.Equal("", dlg.ErrorText);
        Assert.False(dlg.TryAccept());
        Assert.Equal("занято", dlg.ErrorText);
        dlg.Box.Text = " новое ";
        Assert.True(dlg.TryAccept());
        Assert.Equal("новое", dlg.Value);
    });

    [Fact]
    public void GroupTag_TakenInSameSchema_Rejected_FreeTagNumbered()
    {
        var schema = new FemSchema { Id = 1, Tag = "s" };
        var a = new FemMemberGroup { Id = 10, SchemaId = 1, Tag = "Группа" };
        var b = new FemMemberGroup { Id = 11, SchemaId = 1, Tag = "Ригели" };
        schema.MemberGroups.Add(a);
        schema.MemberGroups.Add(b);
        Assert.NotNull(FemGroupService.TagError(schema, b, "Группа"));
        Assert.Null(FemGroupService.TagError(schema, a, "Группа")); // своё имя
        Assert.Null(FemGroupService.TagError(schema, b, "Колонны"));
        Assert.Equal(Loc.S("TextInputEmpty"), FemGroupService.TagError(schema, b, " "));
        Assert.Equal("Группа 2", FemGroupService.FreeTag(schema, "Группа"));
        Assert.Equal("Колонны", FemGroupService.FreeTag(schema, "Колонны"));
    }

    [Fact]
    public void UserPreferences_RoundTrip_AndBrokenFile_GivesDefault()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"opencs-ui-{Guid.NewGuid():N}");
        string path = Path.Combine(dir, "ui.json");
        try
        {
            Assert.Equal(0, UserUiPreferences.Load(path).LanguageIndex);
            Assert.True(new UserUiPreferences { Language = "en-US" }.Save(path));
            Assert.Equal(1, UserUiPreferences.Load(path).LanguageIndex);
            File.WriteAllText(path, "{ не json");
            Assert.Null(UserUiPreferences.Load(path).Language);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
