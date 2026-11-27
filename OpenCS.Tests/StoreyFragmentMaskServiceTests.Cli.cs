using Microsoft.Data.Sqlite;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проект с родителем, его линейным результатом CSfea и фрагментом — для CLI-тестов масок и ручной проверки.</summary>
public sealed partial class StoreyFragmentMaskServiceTests
{
    /// <summary>
    /// Пишет в <paramref name="target"/> родителя «Каркас» (плита на четырёх колоннах, сетка), его постановку «Линейный» с
    /// результатом (загружения «Пост.» и «Ветер») и фрагмент «Этаж 2» (полоса 1,5…4,5). Возвращает имя фрагмента.
    /// </summary>
    internal static string WriteProject(string target)
    {
        var t = new StoreyFragmentMaskServiceTests();
        try
        {
            var p = t.Create();
            using (var db = p.Db)
            {
                var lc = db.GetFemLoadCases(p.Schema.Id);
                var stages = new[] { Single("Пост.", lc[0].Id), Single("Ветер", lc[1].Id) };
                SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages);
            }
            SqliteConnection.ClearAllPools();
            File.Copy(t._path, target, overwrite: true);
            return p.Fragment.Tag;
        }
        finally { t.Dispose(); }
    }

    /// <summary>
    /// Проект ручной проверки раздела (не тест): при заданной OPENCS_FRAGMENT_CHECK_DB пишет его туда
    /// (<see cref="WriteProject"/>). Без переменной — ничего не делает.
    /// </summary>
    [Fact]
    public void ManualCheck_WritesProject()
    {
        string? target = Environment.GetEnvironmentVariable("OPENCS_FRAGMENT_CHECK_DB");
        if (!string.IsNullOrEmpty(target)) WriteProject(target);
    }
}
