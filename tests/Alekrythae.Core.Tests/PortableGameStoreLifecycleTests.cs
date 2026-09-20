using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text.Json;

namespace AlekrythaeCore.Tests;

[TestClass]
public sealed class PortableGameStoreLifecycleTests
{
    [TestMethod]
    public void TryHandleApi_RenameGame_MovesFolderAndUpdatesMetadata()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();

        try
        {
            var store = new PortableGameStore(root);
            PortableGameStoreTestSupport.EnsureGame(store, "OldAdventure", "other_realm");

            JsonElement renamePayload = JsonSerializer.SerializeToElement(new
            {
                oldFolder = "OldAdventure",
                newFolder = "NewAdventure"
            });

            bool handled = store.TryHandleApi("db.game.rename", renamePayload, out object result);

            Assert.IsTrue(handled);
            PortableGameStoreTestSupport.AssertResultOk(result);
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "Games", "OldAdventure")));
            Assert.IsTrue(Directory.Exists(Path.Combine(root, "Games", "NewAdventure")));

            string databasePath = Path.Combine(root, "Games", "NewAdventure", "game.db");
            using SqliteConnection connection = PortableGameStore.OpenDatabase(databasePath);
            Assert.AreEqual(
                "NewAdventure",
                PortableGameStoreTestSupport.ReadGameMeta(connection, "folder"));
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void TryHandleApi_DeleteGame_RemovesExistingGame()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();

        try
        {
            var store = new PortableGameStore(root);
            PortableGameStoreTestSupport.EnsureGame(store, "DeleteMe", "other_realm");

            JsonElement payload = JsonSerializer.SerializeToElement(new { folder = "DeleteMe" });
            bool handled = store.TryHandleApi("db.game.delete", payload, out object result);

            Assert.IsTrue(handled);
            JsonElement json = PortableGameStoreTestSupport.ToJson(result);
            Assert.IsTrue(json.GetProperty("ok").GetBoolean());
            Assert.IsTrue(json.GetProperty("deleted").GetBoolean());
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "Games", "DeleteMe")));
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void TryHandleApi_ReservedWindowsFolder_ReturnsValidationError()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();

        try
        {
            var store = new PortableGameStore(root);
            JsonElement payload = JsonSerializer.SerializeToElement(new
            {
                folder = "CON",
                mode = "real_world"
            });

            bool handled = store.TryHandleApi("db.game.ensure", payload, out object result);

            Assert.IsTrue(handled);
            PortableGameStoreTestSupport.AssertError(result, "invalid_adventure_folder");
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void TryHandleApi_WriteThenReadRegistry_RoundTripsAdventure()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();

        try
        {
            var store = new PortableGameStore(root);
            JsonElement writePayload = JsonSerializer.SerializeToElement(new
            {
                data = new
                {
                    activeId = "adv-1",
                    adventures = new[]
                    {
                        new
                        {
                            id = "adv-1",
                            name = "First Adventure",
                            folder = "FirstAdventure",
                            cover = "",
                            mode = "invalid_mode",
                            coverPolicy = "none",
                            createdAt = "2026-01-01T00:00:00.0000000+00:00",
                            updatedAt = "2026-01-01T00:00:00.0000000+00:00"
                        }
                    }
                }
            });

            bool writeHandled = store.TryHandleApi("db.registry.write", writePayload, out object writeResult);
            Assert.IsTrue(writeHandled);
            PortableGameStoreTestSupport.AssertResultOk(writeResult);

            bool readHandled = store.TryHandleApi("db.registry.read", default, out object readResult);
            Assert.IsTrue(readHandled);

            JsonElement json = PortableGameStoreTestSupport.ToJson(readResult);
            Assert.IsTrue(json.GetProperty("ok").GetBoolean());

            JsonElement data = json.GetProperty("data");
            Assert.AreEqual("adv-1", data.GetProperty("activeId").GetString());

            JsonElement adventures = data.GetProperty("adventures");
            Assert.AreEqual(1, adventures.GetArrayLength());
            Assert.AreEqual("First Adventure", adventures[0].GetProperty("name").GetString());
            Assert.AreEqual("FirstAdventure", adventures[0].GetProperty("folder").GetString());
            Assert.AreEqual("other_realm", adventures[0].GetProperty("mode").GetString());
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }
}
