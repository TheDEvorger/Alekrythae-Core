using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text.Json;

namespace AlekrythaeCore.Tests;

internal static class PortableGameStoreTestSupport
{
    public static string CreateTemporaryRoot()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "Alekrythae-Core-Tests",
            Guid.NewGuid().ToString("N"));
    }

    public static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    public static void EnsureGame(PortableGameStore store, string folder, string mode)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new { folder, mode });
        bool handled = store.TryHandleApi("db.game.ensure", payload, out object result);
        Assert.IsTrue(handled);
        AssertResultOk(result);
    }

    public static void WriteDocument(PortableGameStore store, string folder, string key, object data)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new { folder, key, data });
        bool handled = store.TryHandleApi("db.game.write", payload, out object result);
        Assert.IsTrue(handled);
        AssertResultOk(result);
    }

    public static void AssertTableExists(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);

        long count = Convert.ToInt64(command.ExecuteScalar());
        Assert.AreEqual(1L, count, $"Expected SQLite table '{tableName}' was not created.");
    }

    public static string? ReadGameMeta(SqliteConnection connection, string key)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT value
            FROM game_meta
            WHERE key = $key
            LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public static void AssertResultOk(object result)
    {
        JsonElement json = ToJson(result);
        Assert.IsTrue(json.GetProperty("ok").GetBoolean());
    }

    public static void AssertError(object result, string expectedError)
    {
        JsonElement json = ToJson(result);
        Assert.IsFalse(json.GetProperty("ok").GetBoolean());
        Assert.AreEqual(expectedError, json.GetProperty("error").GetString());
    }

    public static JsonElement ToJson(object result)
    {
        return JsonSerializer.SerializeToElement(result);
    }
}
