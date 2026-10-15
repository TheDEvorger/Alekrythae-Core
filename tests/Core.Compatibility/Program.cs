using System.Diagnostics;
using System.Text.Json;
using AlekrythaeCore;
using AlekrythaeCore.Bridge;
using AlekrythaeCore.Storage;

internal static class Program
{
    private static int _passed;
    internal static readonly string TestRoot = Path.Combine(FindCore(), "test-output");
    internal static readonly string Workspace = Path.Combine(TestRoot, Guid.NewGuid().ToString("N")[..8]);

    private static string FindCore()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Alekrythae.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Run this test build inside the Core repository.");
    }

    static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = new System.Text.UTF8Encoding(false);
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Directory.CreateDirectory(Workspace);
        try
        {
            if (args.FirstOrDefault() == "--bridge") return await ServeBridge(args[1]);
            await Case("legacy object and string bridge envelopes", BridgeTests.Envelopes);
            await Case("queue FIFO, bounded capacity and graceful drain", BridgeTests.Queue);
            await Case("queue byte budget and exception recovery", BridgeTests.Budget);
            await Case("windows sharing a root serialize storage without blocking other roots", BridgeTests.RootCoordination);
            await Case("R9 database responses and history equivalence", StorageTests.LegacyDatabase);
            await Case("scene SQLite revisions, image import and copy", StorageTests.SceneArt);
            await Case("file API contract, Unicode, JSON and binary", StorageTests.Files);
            await Case("sandbox traversal and device paths", StorageTests.Paths);
            await Case("existing junctions cannot redirect SQLite or file APIs", StorageTests.Junctions);
            await Case("SQLite database paths longer than 260 characters", StorageTests.LongPaths);
            await Case("atomic write failure retains prior content", StorageTests.AtomicFailure);
            await Case("portable export/import round trip and picker cancellation", TransferTests.RoundTrip);
            await Case("unsafe ZIP paths, duplicates, links and undeclared payloads", TransferTests.RejectUnsafeArchives);
            await Case("bounded diagnostic logging completes after drain", BridgeTests.Logging);
            await Case("storage worker responsiveness benchmark", BridgeTests.Responsiveness);
            Console.WriteLine($"PASS {_passed}/{_passed}; workspace={Workspace}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static async Task Case(string name, Func<Task> test)
    {
        await test();
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    internal static string Folder(string name) { string p = Path.Combine(Workspace, name); Directory.CreateDirectory(p); return p; }
    internal static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static JsonElement Call(IStorageApiService service, string op, object? payload = null)
    {
        try
        {
            Assert(service.TryHandleApi(op, Json(payload ?? new { }), out var result), "unsupported: " + op);
            return Json(result);
        }
        catch (IOException ex) { return Json(new { ok = false, error = ex.Message }); }
    }
    internal static void Ok(JsonElement result) => Assert(result.GetProperty("ok").GetBoolean(), result.ToString());
    internal static void Error(JsonElement result, string error) => Assert(!result.GetProperty("ok").GetBoolean() && result.GetProperty("error").GetString() == error, result.ToString());

    private static async Task<int> ServeBridge(string root)
    {
        root = Path.GetFullPath(root);
        // This test endpoint may only mutate its own isolated build output fixtures.
        string allowed = Path.GetFullPath(TestRoot) + Path.DirectorySeparatorChar;
        Assert(root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase), "test root outside test-workspaces");
        Directory.CreateDirectory(root);
        IStorageApiService[] services = { new PortableGameStore(root), new FileApiService(new SandboxPathPolicy(root)) };
        while (await Console.In.ReadLineAsync() is string line)
        {
            try
            {
                BridgeMessage message = BridgeMessage.Parse(line);
                if (message.Operation == "save")
                {
                    AtomicFile.WriteText(Path.Combine(root, "fixture.alek.json"), message.Data ?? "{}");
                    continue;
                }
                if (message.Operation == "core.log") continue;
                object result = new { ok = false, error = "unknown_op" };
                foreach (var service in services) if (service.TryHandleApi(message.Operation, message.Payload, out result)) break;
                Console.WriteLine(JsonSerializer.Serialize(new { id = message.Id, result }));
            }
            catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new { error = ex.Message })); }
        }
        return 0;
    }
}
