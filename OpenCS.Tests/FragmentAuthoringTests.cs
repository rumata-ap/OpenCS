using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using CScore.Fem.Fragment;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Служба фрагментов (аудит GUI + CLI, раздел «Фрагменты»): проверка полей с привязкой к полю, уникальное имя, сборка без
/// окон; окна — ошибка у поля, окно не закрывается; смещение вставки — без тихих подстановок.
/// </summary>
public sealed class FragmentAuthoringTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-authoring-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    [Theory]
    [InlineData("1,5", "4.5", null, null)]
    [InlineData("abc", "4", FragmentAuthoring.FieldZ1, null)]
    [InlineData("1", "", null, FragmentAuthoring.FieldZ2)]
    [InlineData("5", "3", null, FragmentAuthoring.FieldZ2)]
    [InlineData("3", "3", null, FragmentAuthoring.FieldZ2)]
    public void ValidateLevels_ErrorAtField(string z1, string z2, string? error1, string? error2)
    {
        var errors = FragmentAuthoring.ValidateLevels(z1, z2, out double v1, out double v2);
        Assert.Equal(error1 != null, errors.Any(e => e.Field == FragmentAuthoring.FieldZ1));
        Assert.Equal(error2 != null, errors.Any(e => e.Field == FragmentAuthoring.FieldZ2));
        if (errors.Count == 0) Assert.Equal((1.5, 4.5), (v1, v2));
        if (z1 == "5") Assert.Equal(Loc.S("StoreyFragmentLevelsOrder"), errors.Single().Message);
    }

    [Theory]
    [InlineData("1,5", true)]
    [InlineData("2.25", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void OffsetError_PositiveNumber(string text, bool ok)
    {
        Assert.Equal(ok, FragmentAuthoring.OffsetError(text, out _) == null);
    }

    [Fact]
    public void NameError_EmptyAndDuplicate_FreeNameNumbers()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        db.LoadAll();

        Assert.Equal(Loc.S("StoreyFragmentNameEmpty"), FragmentAuthoring.NameError(db, "  "));
        Assert.NotNull(FragmentAuthoring.NameError(db, parent.Tag.ToUpperInvariant()));
        Assert.Null(FragmentAuthoring.NameError(db, "Новый"));
        Assert.Equal("Новый", FragmentAuthoring.FreeName(db, "Новый"));
        string second = FragmentAuthoring.FreeName(db, parent.Tag);
        Assert.StartsWith(parent.Tag, second);
        Assert.Contains("2", second);
        Assert.Null(FragmentAuthoring.NameError(db, second));
        FemSchemaAuthoring.CreateNew(db, second);
        string third = FragmentAuthoring.FreeName(db, parent.Tag);
        Assert.Contains("3", third);
        Assert.Null(FragmentAuthoring.NameError(db, third));
    }

    [Fact]
    public async Task Build_DuplicateName_RefusedWithoutSchema()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        db.LoadAll();
        int before = db.FemSchemas.Count;

        var sync = FragmentAuthoring.Build(db, parent, new StoreyFragmentRequest(1.5, 4.5, parent.Tag));
        var menu = await StoreyFragmentMenuFlow.RunAsync(db, parent, new StoreyFragmentRequest(1.5, 4.5, " " + parent.Tag + " "));

        foreach (var r in new[] { sync, menu })
        {
            Assert.True(r.IsError);
            var row = Assert.Single(r.Rows);
            Assert.True(row.IsError);
            Assert.Equal(string.Format(Loc.S("FemSchemaErrTagDuplicate"), parent.Tag), row.Message);
        }
        db.LoadAll();
        Assert.Equal(before, db.FemSchemas.Count);

        var ok = FragmentAuthoring.Build(db, parent, new StoreyFragmentRequest(1.5, 4.5, "Этаж 2"));
        Assert.False(ok.IsError, string.Join("; ", ok.Rows.Select(r => r.Message)));
        Assert.Equal("Этаж 2", ok.Schema!.Tag);
    }

    [Fact]
    public void StoreyVm_ErrorsAtFields_FirstField_DefaultNameFree()
    {
        var context = new StoreyFloorContext([], 0, 9);
        string taken = StoreyFragmentForm.DefaultName("Каркас", 1.5, 4.5, CultureInfo.CurrentCulture);
        var vm = new StoreyFragmentDialogVM("Каркас", context, null, null, CultureInfo.CurrentCulture,
            name => name.Trim() == taken ? "занято" : string.IsNullOrWhiteSpace(name) ? "пусто" : null,
            name => name == taken ? name + " (2)" : name);
        vm.Z1Text = "1,5";
        vm.Z2Text = "4,5";
        Assert.Equal(taken + " (2)", vm.Name);

        vm.Z1Text = "5";
        vm.Z2Text = "3";
        vm.Name = taken;
        Assert.Null(vm.TryRequest(out var error));
        Assert.Equal(Loc.S("StoreyFragmentLevelsOrder"), vm.Z2Error);
        Assert.Equal("", vm.Z1Error);
        Assert.Equal("занято", vm.NameError);
        Assert.Equal(FragmentAuthoring.FieldZ2, vm.FirstErrorField);
        Assert.Equal(vm.Z2Error, error);

        vm.Z2Text = "6";
        Assert.Equal("", vm.Z2Error);
        vm.Name = "Своё";
        Assert.Equal("", vm.NameError);
        Assert.Equal((5.0, 6.0, "Своё"), vm.TryRequest(out _) is { } r ? (r.Z1, r.Z2, r.Name) : default);
        Assert.Null(vm.FirstErrorField);
    }

    [Theory]
    [InlineData("0,5", "-1", "2.5", "3", true)]
    [InlineData("abc", "0", "0", "1", false)]
    [InlineData("0", "0", "0", "0", false)]
    [InlineData("0", "0", "0", "5000", false)]
    [InlineData("0", "0", "0", "2,5", false)]
    public void PasteOffset_TryRead_NoSilentSubstitution(string dx, string dy, string dz, string copies, bool ok)
    {
        Assert.Equal(ok, FemFragmentOffsetDialog.TryRead(dx, dy, dz, copies, out var r, out var errors));
        if (ok) Assert.Equal((0.5, -1.0, 2.5, 3), r);
        if (dx == "abc") Assert.Equal(Loc.S("FemPasteOffsetInvalid"), errors[0]);
        if (copies is "0" or "5000" or "2,5")
            Assert.Equal(string.Format(Loc.S("FemPasteCopiesInvalid"), FemFragmentOffsetDialog.MaxCopies), errors[3]);
    }

    [Fact]
    public void Dialogs_ErrorUnderField_WindowStaysOpen()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new StoreyFragmentDialogVM("Каркас", new StoreyFloorContext([], 0, 9), null, null,
                    CultureInfo.CurrentCulture, _ => null);
                var storey = new StoreyFragmentDialog(vm);
                ShowOffscreen(storey);
                storey.z1Box.Text = "7";
                vm.Z2Text = "2";
                ClickDefault(storey);
                Assert.True(storey.IsVisible);
                Assert.Null(storey.Request);
                Assert.Equal(Loc.S("StoreyFragmentLevelsOrder"), vm.Z2Error);
                Assert.Equal(FragmentAuthoring.FieldZ2, vm.FirstErrorField);
                storey.Close();

                var paste = new FemFragmentOffsetDialog();
                ShowOffscreen(paste);
                Assert.Equal("0", paste.dxBox.Text);
                paste.dxBox.Text = "abc";
                paste.copiesBox.Text = "0";
                ClickDefault(paste);
                Assert.True(paste.IsVisible);
                Assert.Equal(Loc.S("FemPasteOffsetInvalid"), paste.dxError.Text);
                Assert.Equal(Visibility.Visible, paste.dxError.Visibility);
                Assert.Equal(Visibility.Visible, paste.copiesError.Visibility);
                Assert.Equal(Visibility.Collapsed, paste.dyError.Visibility);
                paste.dxBox.Text = "1";
                Assert.Equal(Visibility.Collapsed, paste.dxError.Visibility);
                paste.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }

    static void ClickDefault(Window window)
    {
        var button = FindButtons(window).First(b => b.IsDefault);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.UpdateLayout();
    }

    static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button b) yield return b;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }

    static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.Show();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
