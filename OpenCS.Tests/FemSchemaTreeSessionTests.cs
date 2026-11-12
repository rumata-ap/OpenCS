using CScore.Fem;
using CScore.Fem.Editing;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Дерево схемы по несохранённой сессии редактора: счётчики узлов и КонЭ, отметка «изменено».</summary>
public sealed class FemSchemaTreeSessionTests
{
    [Fact]
    public void AddNode_CountsAndModifiedFromSession_SaveAndDiscardClear()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-tree-session-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Рама" };
            db.SaveFemSchema(schema);
            db.SaveFemTopology(schema.Id, [new FemNode { NodeTag = "1" }, new FemNode { NodeTag = "2" }], [], []);

            var tree = new FemSchemaTreeVM(schema, db, []);
            Assert.Equal(2, tree.NodesSubNode.Count);
            Assert.False(tree.IsModified);

            var session = new FemSchemaEditSession(schema);
            session.Nodes.AddRange(db.GetFemNodes(schema.Id));
            session.Execute(new AddNodeCommand(new FemNode { SchemaId = schema.Id, NodeTag = "3", X = 1 }));
            session.Members.Add(new FemMember { ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]" });
            tree.ReloadSupports(session);
            Assert.Equal(3, tree.NodesSubNode.Count);
            Assert.Equal(1, tree.ElementsSubNode.BarCount);
            Assert.True(tree.IsModified);

            // Сохранение сбрасывает историю — отметка снимается.
            session.MarkSaved();
            tree.ReloadSupports(session);
            Assert.False(tree.IsModified);

            // Отказ от правок: дерево перечитывается из базы.
            session.Execute(new AddNodeCommand(new FemNode { SchemaId = schema.Id, NodeTag = "4" }));
            tree.ReloadSupports(session);
            Assert.True(tree.IsModified);
            tree.ReloadTopology();
            Assert.Equal(2, tree.NodesSubNode.Count);
            Assert.False(tree.IsModified);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
