using System.Text.Json;
using Microsoft.Data.Sqlite;
using AlekrythaeCore.Storage;

namespace AlekrythaeCore;

/// <summary>Compatibility facade. Storage layout and existing bridge responses remain unchanged.</summary>
internal sealed class PortableGameStore : IStorageApiService
{
    private readonly GameRegistryStore _registry;
    private readonly GameDocumentStore _documents;
    private readonly GameFolderService _folders;
    private readonly GameSceneCopyService _sceneCopies;
    private readonly GameSceneStore _scenes;
    public PortableGameStore(string rootFolder)
    {
        var paths = new GamePaths(rootFolder);
        _registry = new GameRegistryStore(paths);
        _documents = new GameDocumentStore(paths);
        _folders = new GameFolderService(paths);
        _sceneCopies = new GameSceneCopyService(paths);
        _scenes = new GameSceneStore(paths);
    }
    public bool TryHandleApi(string operation, JsonElement payload, out object result)
    {
        switch (operation)
        {
            case "db.scene.read":
                result = _scenes.Read(payload);
                return true;
            case "db.scene.write":
                result = _scenes.Write(payload);
                return true;
            case "db.scene.importImage":
                result = _scenes.ImportImage(payload);
                return true;
            case "db.game.copyWithSceneGraph":
                result = _sceneCopies.Copy(payload);
                return true;
            case "db.registry.read":
                result = _registry.ReadRegistry();
                return true;

            case "db.registry.write":
                result = _registry.WriteRegistry(payload);
                return true;

            case "db.game.ensure":
                result = _folders.EnsureGame(payload);
                return true;

            case "db.game.read":
                result = _documents.ReadGameDocument(payload);
                return true;

            case "db.game.write":
                result = _documents.WriteGameDocument(payload);
                return true;

            case "db.game.hasDocument":
                result = _documents.HasGameDocument(payload);
                return true;

            case "db.game.delete":
                result = _folders.DeleteGame(payload);
                return true;

            case "db.game.rename":
                result = _folders.RenameGame(payload);
                return true;

            default:
                result = new { ok = false, error = "unsupported_portable_db_operation" };
                return false;
        }
    }


    internal static SqliteConnection OpenDatabase(string path) => SqliteDatabase.OpenDatabase(path);
    internal static void EnsureRegistrySchema(SqliteConnection connection) => SqliteDatabase.EnsureRegistrySchema(connection);
    internal static void EnsureGameSchema(SqliteConnection connection) => SqliteDatabase.EnsureGameSchema(connection);
    internal static void SetGameMeta(SqliteConnection connection, string key, string value) => SqliteDatabase.SetGameMeta(connection, key, value);
    internal static void EnsureMediaFolders(string folder) => GameFolderService.EnsureMediaFolders(folder);
}
