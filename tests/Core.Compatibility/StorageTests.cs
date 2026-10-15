using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AlekrythaeCore;
using AlekrythaeCore.Storage;
using static Program;

internal static class StorageTests
{
    public static Task LegacyDatabase()
    {
        string oldRoot = Folder("r9");
        string newRoot = Folder("current");
        var old = new R9PortableGameStore(oldRoot);
        var current = new PortableGameStore(newRoot);
        int comparisons = 0;
        void Compare(string op, object? payload = null)
        {
            var p = Json(payload ?? new { });
            Assert(old.TryHandleApi(op, p, out object previous), op);
            Assert(current.TryHandleApi(op, p, out object next), op);
            Assert(Json(previous).ToString() == Json(next).ToString(), $"R9 mismatch {op}: {Json(previous)} != {Json(next)}");
            comparisons++;
        }
        Compare("db.registry.read");
        Compare("db.registry.write", new { data = new { activeId = "a", adventures = new[] { new { id = "a", name = "Türkçe Dünya", folder = "Türkçe Dünya", mode = "real_world", cover = "", coverPolicy = "none", createdAt = "2026-09-01", updatedAt = "2026-09-01" } } } });
        Compare("db.registry.read");
        Compare("db.registry.write", new { data = new { adventures = new[] { new { id = "a", name = "bad", folder = "../escape" } } } });
        Compare("db.registry.read");
        Compare("db.game.ensure", new { folder = "Türkçe Dünya", mode = "real_world" });
        Compare("db.game.ensure", new { folder = "Türkçe Dünya", mode = "real_world" });
        Compare("db.game.read", new { folder = "Türkçe Dünya", key = "missing" });
        Compare("db.game.hasDocument", new { folder = "Türkçe Dünya", key = "map" });
        for (int i = 0; i < 60; i++)
            Compare("db.game.write", new { folder = "Türkçe Dünya", key = "map", data = new { index = i, unicode = "ışık 🌙", nested = new[] { 1, 2, 3 } } });
        Compare("db.game.write", new { folder = "Türkçe Dünya", key = "map", data = new { index = 59, unicode = "ışık 🌙", nested = new[] { 1, 2, 3 } } });
        Compare("db.game.hasDocument", new { folder = "Türkçe Dünya", key = "map" });
        Compare("db.game.read", new { folder = "Türkçe Dünya", key = "map" });
        Compare("db.game.write", new { folder = "Türkçe Dünya", key = "bad/key", data = 1 });
        Compare("db.game.write", new { folder = "Türkçe Dünya", key = "map" });
        foreach (string folder in new[] { "../escape", "CON", "LPT1.txt", "C:\\outside", "", "foo/bar" })
            Compare("db.game.ensure", new { folder });
        string[] History(string root)
        {
            using var db = SqliteDatabase.OpenDatabase(Path.Combine(root, "Games", "Türkçe Dünya", "game.db"));
            using var sql = db.CreateCommand();
            sql.CommandText = "SELECT json_text FROM document_history WHERE document_key='map' ORDER BY history_id;";
            using var reader = sql.ExecuteReader();
            var values = new List<string>();
            while (reader.Read()) values.Add(reader.GetString(0));
            return values.ToArray();
        }
        Assert(History(newRoot).Length == 50 && History(oldRoot).SequenceEqual(History(newRoot)), "R9 history changed");
        // The new service must open an existing R9 file without a schema migration.
        var onOldData = new PortableGameStore(oldRoot);
        Ok(Call(onOldData, "db.game.read", new { folder = "Türkçe Dünya", key = "map" }));
        Compare("db.game.rename", new { oldFolder = "Türkçe Dünya", newFolder = "Yeni Dünya" });
        Compare("db.game.read", new { folder = "Yeni Dünya", key = "map" });
        Compare("db.game.delete", new { folder = "Yeni Dünya" });
        Compare("db.game.delete", new { folder = "Yeni Dünya" });
        Console.WriteLine($"COMPAT {comparisons} legacy/current responses equal; history=50; existing R9 database readable");
        return Task.CompletedTask;
    }

    public static Task SceneArt()
    {
        string root = Folder("scene-art");
        var db = new PortableGameStore(root);
        Ok(Call(db, "db.game.ensure", new { folder = "Origin", mode = "alekrytha" }));
        var content = new { strokes = Array.Empty<object>(), images = new[] {
            new { id = "image-1", path = "Games/Origin/Media/tree.png",
                position = new { x = 0, y = 0 }, width = 100, height = 100 } } };
        Error(Call(db, "db.scene.read", new { folder = "Origin", sceneId = "../bad" }), "invalid_scene_path");
        Assert(!Call(db, "db.scene.read", new { folder = "Origin", sceneId = "inn" })
            .GetProperty("exists").GetBoolean(), "scene unexpectedly exists");
        Ok(Call(db, "db.scene.write", new { folder = "Origin", sceneId = "inn",
            expectedRevision = -1, data = new { schemaVersion = 1, sceneId = "inn", revision = 0, content } }));
        Error(Call(db, "db.scene.write", new { folder = "Origin", sceneId = "inn",
            expectedRevision = -1, data = new { schemaVersion = 1, sceneId = "inn", revision = 0, content } }),
            "scene_revision_conflict");
        string source = Path.Combine(root, "Games", "Origin", "Media");
        Directory.CreateDirectory(source);
        File.WriteAllBytes(Path.Combine(source, "tree.png"), new byte[] { 137, 80, 78, 71 });
        var imported = Call(db, "db.scene.importImage",
            new { folder = "Origin", sceneId = "inn", path = "Games/Origin/Media/tree.png" });
        Ok(imported);
        string imagePath = imported.GetProperty("path").GetString()!;
        Assert(File.Exists(Path.Combine(root, imagePath.Replace('/', Path.DirectorySeparatorChar))),
            "scene image was not copied");
        Error(Call(db, "db.scene.importImage",
            new { folder = "Origin", sceneId = "inn", path = "../other/tree.png" }), "image_outside_adventure");
        var next = new { strokes = Array.Empty<object>(), images = new[] {
            new { id = "image-1", path = imagePath, position = new { x = 1, y = 2 }, width = 100, height = 100 } } };
        Ok(Call(db, "db.scene.write", new { folder = "Origin", sceneId = "inn",
            expectedRevision = 0, data = new { schemaVersion = 1, sceneId = "inn", revision = 1, content = next } }));
        for (int revision = 2; revision <= 60; revision++)
        {
            var events = Enumerable.Range(0, revision == 60 ? 1000 : 1)
                .Select(index => new { id = "event-" + index, text = "Painted scene" }).ToArray();
            Ok(Call(db, "db.scene.write", new { folder = "Origin", sceneId = "inn",
                expectedRevision = revision - 1,
                data = new { schemaVersion = 1, sceneId = "inn", revision,
                    content = new { strokes = Array.Empty<object>(), images = next.images, events } } }));
        }
        using (var sceneDb = SqliteDatabase.OpenDatabase(Path.Combine(root, "Games", "Origin", "Mekanlar", "inn", "scene.db")))
        using (var count = sceneDb.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM document_history WHERE document_key='art'";
            Assert(Convert.ToInt32(count.ExecuteScalar()) == 50, "scene undo history grew without bound");
        }
        Assert(Call(db, "db.scene.read", new { folder = "Origin", sceneId = "inn" })
            .GetProperty("data").GetProperty("content").GetProperty("events").GetArrayLength() == 1000,
            "event log was pruned with undo history");
        var graph = new { schemaVersion = 1, scenes = Array.Empty<object>() };
        string abandoned = Path.Combine(root, "Games", "Scenes-abandoned.pending-" + Guid.NewGuid().ToString("N"));
        string recent = Path.Combine(root, "Games", "Scenes-recent.pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(abandoned);
        Directory.CreateDirectory(recent);
        File.WriteAllText(Path.Combine(abandoned, "incomplete.txt"), "partial copy");
        Directory.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddDays(-8));
        Ok(Call(db, "db.game.copyWithSceneGraph", new { source = "Origin", folder = "Copy", graph }));
        Assert(!Directory.Exists(abandoned), "old safe staging was not recovered");
        Assert(Directory.Exists(recent), "recent staging must remain untouched");
        Assert(File.Exists(Path.Combine(root, "Games", "Origin", "game.db")), "source adventure was touched");
        Assert(!Directory.EnumerateDirectories(Path.Combine(root, "Games"), "Copy.pending-*").Any(),
            "completed copy left a pending staging folder");
        var copied = Call(db, "db.scene.read", new { folder = "Copy", sceneId = "inn" });
        Ok(copied);
        Assert(copied.GetProperty("data").GetProperty("content").GetProperty("images")[0]
            .GetProperty("path").GetString()!.StartsWith("Games/Copy/", StringComparison.Ordinal),
            "copied scene image points at source adventure");
        Assert(File.Exists(Path.Combine(root, "Games", "Copy", "Mekanlar", "inn", "images",
            Path.GetFileName(imagePath))), "scene image missing in copy");
        File.WriteAllBytes(Path.Combine(root, "Games", "Origin", "corrupt.db"), new byte[] { 1, 2, 3 });
        bool copyFailed;
        try
        {
            copyFailed = !Call(db, "db.game.copyWithSceneGraph", new { source = "Origin", folder = "Broken", graph })
                .GetProperty("ok").GetBoolean();
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { copyFailed = true; }
        Assert(copyFailed, "corrupt nested database unexpectedly copied");
        Assert(!Directory.Exists(Path.Combine(root, "Games", "Broken")), "failed copy was published");
        Assert(!Directory.EnumerateDirectories(Path.Combine(root, "Games"), "Broken.pending-*").Any(),
            "failed copy left a pending staging folder");
        return Task.CompletedTask;
    }

    public static Task Files()
    {
        string root = Folder("files");
        var files = new FileApiService(new SandboxPathPolicy(root));
        Ok(Call(files, "fs.mkdir", new { path = "Data" }));
        Ok(Call(files, "fs.writeText", new { path = "Data/ü.txt", content = "ışık 🌙" }));
        Assert(Call(files, "fs.readText", new { path = "Data/ü.txt" }).GetProperty("content").GetString() == "ışık 🌙", "Unicode text");
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "Data", "ü.txt"));
        Assert(!bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "BOM added");
        foreach (object? data in new object?[] { new { text = "</script>'\\\n\u2028" }, new[] { 1, 2 }, false, 0, null })
        {
            Ok(Call(files, "fs.writeJson", new { path = "Data/test.json", data }));
            var read = Call(files, "fs.readJson", new { path = "Data/test.json" });
            Ok(read);
            Assert(read.GetProperty("data").ToString() == Json(data).ToString(), "JSON round trip");
        }
        Ok(Call(files, "fs.writeText", new { path = "invalid.json", content = "{" }));
        Error(Call(files, "fs.readJson", new { path = "invalid.json" }), "invalid_json");
        Ok(Call(files, "fs.writeBinary", new { path = "Data/bytes.bin", base64 = "AAECA/8=" }));
        Assert(File.ReadAllBytes(Path.Combine(root, "Data", "bytes.bin")).SequenceEqual(new byte[] { 0, 1, 2, 3, 255 }), "binary corruption");
        Error(Call(files, "fs.writeBinary", new { path = "Data/no.bin", base64 = "" }), "empty_data");
        Ok(Call(files, "fs.move", new { source = "Data/ü.txt", destination = "Data/new.txt" }));
        Error(Call(files, "fs.move", new { source = "Data/new.txt", destination = "Data/bytes.bin" }), "destination_exists");
        Error(Call(files, "fs.delete", new { path = "Data" }), "directory_delete_disabled");
        Error(Call(files, "fs.deleteTree", new { path = "Data" }), "protected_path");
        Ok(Call(files, "fs.delete", new { path = "Data/new.txt" }));
        Assert(!Call(files, "fs.exists", new { path = "Data/new.txt" }).GetProperty("exists").GetBoolean(), "delete failed");
        Ok(Call(files, "fs.list"));
        Ok(Call(files, "fs.mkdir", new { path = "Disposable/child" }));
        Ok(Call(files, "fs.deleteTree", new { path = "Disposable" }));
        return Task.CompletedTask;
    }

    public static Task Paths()
    {
        var paths = new SandboxPathPolicy(Folder("paths"));
        foreach (string path in new[] { "", "../escape", "..\\paths-sibling\\file", "C:\\outside", "\\\\server\\share", "x.txt:secret", ".", "Data/../../escape", "\\?\\C:\\outside" })
            Assert(paths.Resolve(path) == null, "unsafe path: " + path);
        Assert(paths.Resolve("safe/ü.json") != null, "safe path rejected");
        return Task.CompletedTask;
    }

    public static Task Junctions()
    {
        string root = Folder("junctions");
        string outside = Folder("outside-junctions");
        File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "untouched");
        string junction = Path.Combine(root, "Games");
        // Native Windows junctions need no administrator privilege. Both endpoints are isolated test fixtures.
        string Quote(string value) => "([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "')))";
        string command = "$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path " + Quote(junction) + " -Target " + Quote(outside) + " | Out-Null";
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        using var process = Process.Start(info)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert(process.ExitCode == 0, "junction fixture: " + error);
        try
        {
            var files = new FileApiService(new SandboxPathPolicy(root));
            Error(Call(files, "fs.writeText", new { path = "Games/sentinel.txt", content = "wrong" }), "invalid_path");
            var listed = Call(files, "fs.list");
            Ok(listed);
            Assert(!listed.GetProperty("items").EnumerateArray().Any(item =>
                item.GetProperty("name").GetString() == "Games"),
                "fs.list exposed a junction entry");
            var db = new PortableGameStore(root);
            Error(Call(db, "db.game.ensure", new { folder = "outside" }), "invalid_adventure_folder");
            Assert(File.ReadAllText(Path.Combine(outside, "sentinel.txt")) == "untouched" && !Directory.Exists(Path.Combine(outside, "outside")), "junction escaped");
        }
        finally { Directory.Delete(junction); } // Only unlink this test junction; never recursively traverse its target.
        return Task.CompletedTask;
    }

    public static Task AtomicFailure()
    {
        string folder = Folder("atomic");
        string file = Path.Combine(folder, "state.json");
        AtomicFile.WriteText(file, "old");
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false;
            try { AtomicFile.WriteText(file, "new"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            Assert(failed, "replacement unexpectedly succeeded");
        }
        Assert(File.ReadAllText(file) == "old" && Directory.GetFiles(folder, "*.tmp").Length == 0, "atomic failure lost prior data or leaked temp");
        return Task.CompletedTask;
    }

    public static Task LongPaths()
    {
        // Construct an actual >260-character path independently of the checkout directory length.
        // Each component remains well below the Windows 255-character per-component limit.
        string root = Folder("long-path");
        for (int i = 0; root.Length <= 280; i++)
        {
            string component = $"segment_{i:D2}_" + new string((char)('a' + i % 26), 48);
            root = Path.Combine(root, component);
        }
        Assert(root.Length > 260, "fixture must exceed old Windows path limit");
        Console.WriteLine($"INFO long-path fixture: {root.Length} characters");
        var db = new PortableGameStore(root);
        Ok(Call(db, "db.game.write", new { folder = "Türkçe", key = "map", data = new { correct = true } }));
        Assert(Call(db, "db.game.read", new { folder = "Türkçe", key = "map" }).GetProperty("data").GetProperty("correct").GetBoolean(), "long path read");
        return Task.CompletedTask;
    }
}
