using System.ComponentModel;
using System.IO;
using System.Windows.Controls;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// «Сохранить» проекта (меню, кнопка, Ctrl+S — и в редакторе схемы) сначала записывает черновик открытой страницы:
/// иначе «Проект сохранён», а правки страницы (в т. ч. сессия редактора схемы МКЭ) остаются только в памяти.
/// </summary>
public sealed class ProjectSaveTests
{
    sealed class DirtyPage(bool accepts) : IUnsavedChanges
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool IsDirty { get; private set; } = true;
        public string DisplayName => "Страница";
        public int Saves { get; private set; }
        public bool SaveChanges()
        {
            Saves++;
            if (!accepts) return false;
            IsDirty = false;
            PropertyChanged?.Invoke(this, new(nameof(IsDirty)));
            return true;
        }
        public void DiscardChanges() => IsDirty = false;
    }

    static void WithApp(Action<AppViewModel> test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"opencs-save-{Guid.NewGuid():N}.db");
            var app = new AppViewModel(new LogService(), new NullFileDialogService(), path);
            try { test(app); }
            catch (Exception ex) { error = ex; }
            finally
            {
                app.db.Dispose();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                    try { File.Delete(path + suffix); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    [Fact]
    public void SaveProject_WritesOpenPageDraftFirst()
    {
        WithApp(app =>
        {
            var page = new DirtyPage(accepts: true);
            app.CurrentPage = new UserControl { DataContext = page };
            app.SaveProjectCommand.Execute(null);
            Assert.Equal(1, page.Saves);
            Assert.False(page.IsDirty);
            Assert.DoesNotContain(" *", app.CurrentPageTitle);
        });
    }

    [Fact]
    public void SaveProject_PageRejects_StaysDirty()
    {
        WithApp(app =>
        {
            var page = new DirtyPage(accepts: false);
            app.CurrentPage = new UserControl { DataContext = page };
            app.SaveProjectCommand.Execute(null);
            Assert.Equal(1, page.Saves);
            Assert.True(page.IsDirty);
        });
    }

    /// <summary>Редактор схемы МКЭ — такая же страница с черновиком: «Сохранить» проекта пишет его сессию.</summary>
    [Fact]
    public void FemSchemaEditor_IsUnsavedChangesPage() =>
        Assert.True(typeof(IUnsavedChanges).IsAssignableFrom(typeof(FemSchemaEditorVM)));
}
