using System.Windows.Threading;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Вид «Вся схема» страницы КонЭ (ручная проверка 28.11): пластина родной схемы рисуется областью, и подсветка должна
/// относиться к области (раньше область рисовалась обычным цветом — плита на схеме не выделялась).
/// </summary>
public sealed class Fem3DHighlightTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-3d-highlight-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    /// <summary>Загрузка вида в STA-потоке с диспетчером (коллекции WPF создаются в потоке вида).</summary>
    static void OnDispatcher(Func<Task> body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                try { await body(); }
                catch (Exception ex) { error = ex; }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    [Fact]
    public void WholeSchema_HighlightsPlanarRegionOfMember()
    {
        OnDispatcher(async () =>
        {
            using var db = new DatabaseService(_path);
            var parent = StoreyFragmentDbFixture.CreateParent(db);
            db.LoadAll();
            var group = new FemMemberGroup { SchemaId = parent.Id, Tag = "S1", Kind = FemMemberGroup.KindMembers };
            group.SetTags(["S1"]);

            var whole = new Fem3DVM(group, db, highlightOnSchema: true);
            await whole.LoadAsync();
            Assert.True(whole.IsHighlightMode);
            Assert.Contains(whole.PlanarRegionVisuals, v => v.ElemTag == "S1");
            Assert.Contains(whole.PlanarRegionVisuals, v => v.ElemTag == "S2");
            Assert.True(whole.IsScopeShell("S1"));
            Assert.False(whole.IsScopeShell("S2"));

            var isolated = new Fem3DVM(group, db);
            await isolated.LoadAsync();
            Assert.False(isolated.IsHighlightMode);

            // Страницы КонЭ и узла: вид без глифов, независимо от умолчаний вида схемы.
            var page = new Fem3DVM(group, db, highlightOnSchema: true).WithoutGlyphs();
            await page.LoadAsync();
            Assert.False(page.ShowLoadGlyphs || page.ShowSupportGlyphs || page.ShowSpringGlyphs || page.ShowHingeGlyphs
                || page.ShowRigidBodyGlyphs || page.ShowFragmentCutGlyphs || page.ShowSectionGlyphs || page.ShowDirectionGlyphs
                || page.ShowLoadValues);
            Assert.True(whole.ShowLoadGlyphs && whole.ShowSupportGlyphs);
        });
    }
}
