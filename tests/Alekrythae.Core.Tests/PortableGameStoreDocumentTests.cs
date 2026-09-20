using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text.Json;

namespace AlekrythaeCore.Tests;

[TestClass]
public sealed class PortableGameStoreDocumentTests
{
    [TestMethod]
    public void TryHandleApi_WriteReadAndHasDocument_RoundTripsJson()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();

        try
        {
            var store = new PortableGameStore(root);
            JsonElement writePayload = JsonSerializer.SerializeToElement(new
            {
                folder = "TestAdventure",
                key = "story",
                data = new
                {
                    title = "Alekrythae",
                    chapter = 7
                }
            });

            bool writeHandled = store.TryHandleApi("db.game.write", writePayload, out object writeResult);
            Assert.IsTrue(writeHandled);
            PortableGameStoreTestSupport.AssertResultOk(writeResult);

            JsonElement request = JsonSerializer.SerializeToElement(new
            {
                folder = "TestAdventure",
                key = "story"
            });

            bool hasHandled = store.TryHandleApi("db.game.hasDocument", request, out object hasResult);
            Assert.IsTrue(hasHandled);
            JsonElement hasJson = PortableGameStoreTestSupport.ToJson(hasResult);
            Assert.IsTrue(hasJson.GetProperty("ok").GetBoolean());
            Assert.IsTrue(hasJson.GetProperty("exists").GetBoolean());

            bool readHandled = store.TryHandleApi("db.game.read", request, out object readResult);
            Assert.IsTrue(readHandled);

            JsonElement readJson = PortableGameStoreTestSupport.ToJson(readResult);
            Assert.IsTrue(readJson.GetProperty("ok").GetBoolean());
            Assert.IsTrue(readJson.GetProperty("exists").GetBoolean());

            JsonElement data = readJson.GetProperty("data");
            Assert.AreEqual("Alekrythae", data.GetProperty("title").GetString());
            Assert.AreEqual(7, data.GetProperty("chapter").GetInt32());
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void TryHandleApi_WhenDocumentChanges_StoresPreviousRevision()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();

        try
        {
            var store = new PortableGameStore(root);

            PortableGameStoreTestSupport.WriteDocument(
                store,
                "TestAdventure",
                "story",
                new { revision = 1, title = "First" });

            PortableGameStoreTestSupport.WriteDocument(
                store,
                "TestAdventure",
                "story",
                new { revision = 2, title = "Second" });

            string databasePath = Path.Combine(root, "Games", "TestAdventure", "game.db");
            using SqliteConnection connection = PortableGameStore.OpenDatabase(databasePath);

            using SqliteCommand countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM document_history WHERE document_key = 'story';";
            Assert.AreEqual(1L, Convert.ToInt64(countCommand.ExecuteScalar()));

            using SqliteCommand previousCommand = connection.CreateCommand();
            previousCommand.CommandText = @"
                SELECT json_text
                FROM document_history
                WHERE document_key = 'story'
                ORDER BY history_id DESC
                LIMIT 1;";

            string previousJson = Convert.ToString(previousCommand.ExecuteScalar()) ?? string.Empty;
            using JsonDocument previous = JsonDocument.Parse(previousJson);
            Assert.AreEqual(1, previous.RootElement.GetProperty("revision").GetInt32());
            Assert.AreEqual("First", previous.RootElement.GetProperty("title").GetString());
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void TryHandleApi_InvalidDocumentKey_ReturnsValidationError()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();

        try
        {
            var store = new PortableGameStore(root);
            JsonElement payload = JsonSerializer.SerializeToElement(new
            {
                folder = "TestAdventure",
                key = "../story"
            });

            bool handled = store.TryHandleApi("db.game.read", payload, out object result);

            Assert.IsTrue(handled);
            PortableGameStoreTestSupport.AssertError(result, "invalid_document_key");
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }
}
