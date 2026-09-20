using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text.Json;

namespace AlekrythaeCore.Tests;

[TestClass]
public sealed class PortableGameStoreSchemaTests
{
    [TestMethod]
    public void EnsureGameSchema_CreatesRequiredTables()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();
        string databasePath = Path.Combine(root, "game.db");

        try
        {
            using SqliteConnection connection = PortableGameStore.OpenDatabase(databasePath);
            PortableGameStore.EnsureGameSchema(connection);

            PortableGameStoreTestSupport.AssertTableExists(connection, "documents");
            PortableGameStoreTestSupport.AssertTableExists(connection, "document_history");
            PortableGameStoreTestSupport.AssertTableExists(connection, "game_meta");
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void EnsureRegistrySchema_CreatesRequiredTables()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();
        string databasePath = Path.Combine(root, "meggy.db");

        try
        {
            using SqliteConnection connection = PortableGameStore.OpenDatabase(databasePath);
            PortableGameStore.EnsureRegistrySchema(connection);

            PortableGameStoreTestSupport.AssertTableExists(connection, "adventures");
            PortableGameStoreTestSupport.AssertTableExists(connection, "app_state");
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void SetGameMeta_InsertsAndUpdatesValue()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();
        string databasePath = Path.Combine(root, "game.db");

        try
        {
            using SqliteConnection connection = PortableGameStore.OpenDatabase(databasePath);
            PortableGameStore.EnsureGameSchema(connection);

            PortableGameStore.SetGameMeta(connection, "mode", "real_world");
            Assert.AreEqual(
                "real_world",
                PortableGameStoreTestSupport.ReadGameMeta(connection, "mode"));

            PortableGameStore.SetGameMeta(connection, "mode", "alekrytha");
            Assert.AreEqual(
                "alekrytha",
                PortableGameStoreTestSupport.ReadGameMeta(connection, "mode"));
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void EnsureMediaFolders_CreatesExpectedStructure()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();
        string gameFolder = Path.Combine(root, "Games", "TestAdventure");

        try
        {
            PortableGameStore.EnsureMediaFolders(gameFolder);

            string media = Path.Combine(gameFolder, "Media");
            string[] expectedFolders =
            {
                media,
                Path.Combine(media, "Adventure"),
                Path.Combine(media, "Characters"),
                Path.Combine(media, "Locations"),
                Path.Combine(media, "Lore"),
                Path.Combine(media, "Map"),
                Path.Combine(media, "Reverie"),
                Path.Combine(media, "Audio"),
                Path.Combine(media, "Music"),
                Path.Combine(media, "Ambiance"),
                Path.Combine(media, "Effects"),
                Path.Combine(media, "Backgrounds"),
                Path.Combine(media, "Events")
            };

            foreach (string folder in expectedFolders)
            {
                Assert.IsTrue(
                    Directory.Exists(folder),
                    $"Expected media folder was not created: {folder}");
            }
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void TryHandleApi_EnsureGame_CreatesDatabaseAndMetadata()
    {
        string root = PortableGameStoreTestSupport.CreateTemporaryRoot();

        try
        {
            var store = new PortableGameStore(root);
            JsonElement payload = JsonSerializer.SerializeToElement(new
            {
                folder = "TestAdventure",
                mode = "real_world"
            });

            bool handled = store.TryHandleApi("db.game.ensure", payload, out object result);

            Assert.IsTrue(handled);
            JsonElement json = PortableGameStoreTestSupport.ToJson(result);
            Assert.IsTrue(json.GetProperty("ok").GetBoolean());
            Assert.IsTrue(json.GetProperty("created").GetBoolean());
            Assert.AreEqual("TestAdventure", json.GetProperty("folder").GetString());

            string databasePath = Path.Combine(root, "Games", "TestAdventure", "game.db");
            Assert.IsTrue(File.Exists(databasePath));

            using SqliteConnection connection = PortableGameStore.OpenDatabase(databasePath);
            Assert.AreEqual(
                "real_world",
                PortableGameStoreTestSupport.ReadGameMeta(connection, "mode"));
            Assert.AreEqual(
                "TestAdventure",
                PortableGameStoreTestSupport.ReadGameMeta(connection, "folder"));
        }
        finally
        {
            PortableGameStoreTestSupport.DeleteDirectory(root);
        }
    }
}
