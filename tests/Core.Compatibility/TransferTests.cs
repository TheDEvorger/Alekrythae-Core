using System.IO.Compression;
using System.Text;
using AlekrythaeCore;
using AlekrythaeCore.Transfer;
using static Program;

internal static class TransferTests
{
    private sealed class Picker(string? export, string? import) : ITransferFilePicker
    {
        public Task<string?> SelectExportPathAsync(string name) => Task.FromResult(export);
        public Task<string?> SelectImportPathAsync() => Task.FromResult(import);
    }

    public static async Task RejectUnsafeArchives()
    {
        string target = Folder("import-adversarial-target");
        string packages = Folder("import-adversarial-packages");

        async Task Reject(string name, string expectedError, Action<ZipArchive> build)
        {
            string file = Path.Combine(packages, name + ".alekdata");
            using (var archive = ZipFile.Open(file, ZipArchiveMode.Create)) build(archive);
            var service = new DataTransferService(target, new Picker(null, file));
            var result = Json(await service.ExecuteAsync("data.import"));
            Assert(!result.GetProperty("ok").GetBoolean(), name + " was accepted");
            string error = result.GetProperty("error").GetString() ?? "";
            Assert(error.Contains(expectedError, StringComparison.OrdinalIgnoreCase), name + ": " + error);
            Assert(!Directory.Exists(Path.Combine(target, "Backups")), name + " wrote a backup before validation");
        }

        static void Add(ZipArchive archive, string path, string content = "test")
        {
            using Stream stream = archive.CreateEntry(path).Open();
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes);
        }

        await Reject("traversal", "güvenli olmayan", archive => Add(archive, "../outside.txt"));
        await Reject("duplicate", "yinelenen", archive =>
        {
            Add(archive, "Data/registry.json");
            Add(archive, "data/REGISTRY.JSON", "overwritten");
        });
        await Reject("device-name", "güvenli olmayan", archive => Add(archive, "Data/CON.txt"));
        await Reject("trailing-dot", "güvenli olmayan", archive => Add(archive, "Games/Adventure./data.json"));
        await Reject("unix-symlink", "bağlantı", archive =>
        {
            ZipArchiveEntry entry = archive.CreateEntry("Data/link");
            entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using Stream stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes("../../outside"));
        });
        await Reject("undeclared-v2-file", "manifest dışı", archive =>
        {
            Add(archive, "Games/unknown.txt");
            Add(archive, "alekrythae-data-manifest.json",
                "{\"format\":\"alekrythae.meggy.data\",\"formatVersion\":2,\"files\":[],\"architectureRevision\":126}");
        });
    }

    public static async Task RoundTrip()
    {
        string source = Folder("export-source");
        string target = Folder("import-target");
        string package = Path.Combine(Folder("packages"), "fixture.alekdata");
        var db = new PortableGameStore(source);
        Ok(Call(db, "db.registry.write", new { data = new { activeId = "fixture", adventures = new[] { new { id = "fixture", name = "Test", folder = "Test", mode = "real_world" } } } }));
        Ok(Call(db, "db.game.ensure", new { folder = "Test", mode = "real_world" }));
        Ok(Call(db, "db.game.write", new { folder = "Test", key = "map", data = new { version = 1, label = "ışık" } }));
        var export = new DataTransferService(source, new Picker(package, null));
        Ok(Json(await export.ExecuteAsync("data.export")));
        Assert(File.Exists(package), "package missing");
        var import = new DataTransferService(target, new Picker(null, package));
        var imported = Json(await import.ExecuteAsync("data.import"));
        Ok(imported);
        Assert(imported.GetProperty("importedAdventures").GetInt32() == 1, "import count");
        var targetDb = new PortableGameStore(target);
        var registry = Call(targetDb, "db.registry.read");
        string folder = registry.GetProperty("data").GetProperty("adventures")[0].GetProperty("folder").GetString()!;
        var map = Call(targetDb, "db.game.read", new { folder, key = "map" });
        Ok(map);
        Assert(map.GetProperty("data").GetProperty("label").GetString() == "ışık", "round trip lost map");
        Assert(Directory.GetFiles(Path.Combine(target, "Backups"), "*.alekdata").Length == 1, "pre-import backup missing");
        var cancel = new DataTransferService(target, new Picker(null, null));
        Assert(Json(await cancel.ExecuteAsync("data.import")).GetProperty("cancelled").GetBoolean(), "import cancellation");
        Assert(Json(await cancel.ExecuteAsync("data.export")).GetProperty("cancelled").GetBoolean(), "export cancellation");
    }
}
