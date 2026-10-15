using System.Diagnostics;
using System.Text.Json;
using AlekrythaeCore.Bridge;
using AlekrythaeCore.Diagnostics;
using AlekrythaeCore.Storage;
using static Program;

internal static class BridgeTests
{
    public static Task Envelopes()
    {
        var save = BridgeMessage.Parse("{\"op\":\"save\",\"data\":\"{\\\"ü\\\":1}\"}");
        Assert(save.Id == null && save.Data == "{\"ü\":1}", "legacy save");
        string raw = "{\"op\":\"fs.writeJson\",\"id\":\"__alek_1\",\"payload\":{\"data\":{\"value\":42}}}";
        var message = BridgeMessage.Parse(JsonSerializer.Serialize(raw));
        Assert(message.Payload.GetProperty("data").GetProperty("value").GetInt32() == 42, "payload lifetime");
        string injected = BridgeScript.Build(JsonSerializer.Serialize(new { text = "</script><script>alert(1)</script>" }));
        Assert(!injected.Contains("</script>", StringComparison.OrdinalIgnoreCase) && injected.Contains("__alekSave"), "bootstrap escaped data or embedded resource broken");
        foreach (string bad in new[] { "null", "[]", "{}", "{\"op\":1}" })
        {
            bool rejected = false;
            try { BridgeMessage.Parse(bad); } catch (JsonException) { rejected = true; }
            Assert(rejected, "invalid envelope accepted");
        }
        return Task.CompletedTask;
    }

    public static async Task Queue()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<int>();
        var queue = new SerialWorkQueue(ex => throw ex, capacity: 1);
        Assert(queue.TryEnqueue(async () => { start.SetResult(); await release.Task; order.Add(1); }), "first");
        await start.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(queue.TryEnqueue(() => { order.Add(2); return Task.CompletedTask; }), "second");
        Assert(!queue.TryEnqueue(() => Task.CompletedTask), "capacity not enforced");
        Task completion = queue.CompleteAsync();
        Assert(!completion.IsCompleted, "close discarded pending write");
        Assert(!queue.TryEnqueue(() => Task.CompletedTask), "accepted after close");
        release.SetResult();
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(order.SequenceEqual(new[] { 1, 2 }), "FIFO/drain failed");
    }

    public static async Task Budget()
    {
        var errors = new List<Exception>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new SerialWorkQueue(errors.Add, capacity: 4, maxBytes: 10);
        Assert(queue.TryEnqueue(() => release.Task, 8), "budget first");
        Assert(!queue.TryEnqueue(() => Task.CompletedTask, 3), "budget exceeded");
        Assert(queue.TryEnqueue(() => throw new IOException("fixture"), 2), "budget second");
        bool recovered = false;
        Assert(queue.TryEnqueue(() => { recovered = true; return Task.CompletedTask; }), "budget third");
        release.SetResult();
        await queue.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert(recovered && errors.Count == 1, "worker died after error");
    }

    public static async Task Logging()
    {
        string path = Path.Combine(Folder("log"), "core.log");
        var log = new BufferedFileLog(path);
        for (int i = 0; i < 4000; i++) log.Write(new string('x', 10000));
        await log.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert(File.Exists(path) && new FileInfo(path).Length < 4 * 1024 * 1024, "unbounded log");
        Assert(File.ReadAllText(path).Contains("truncated"), "oversize log not truncated");
    }

    public static async Task RootCoordination()
    {
        using var first = new RootStorageLease(Folder("shared"));
        using var second = new RootStorageLease(Folder("shared"));
        using var other = new RootStorageLease(Folder("other"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task one = first.RunAsync(() => release.Task);
        bool ran = false;
        Task two = second.RunAsync(() => { ran = true; return Task.CompletedTask; });
        await other.RunAsync(() => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        Assert(!ran, "same-root storage operations overlapped");
        release.SetResult();
        await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(5));
        Assert(ran, "second window starved");
    }

    public static async Task Responsiveness()
    {
        // Controlled I/O stall: compare caller occupancy, not disk throughput or total app FPS.
        var clock = Stopwatch.StartNew();
        Thread.Sleep(150);
        double synchronousMs = clock.Elapsed.TotalMilliseconds;
        var queue = new SerialWorkQueue(ex => throw ex);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clock.Restart();
        Assert(queue.TryEnqueue(async () => { Thread.Sleep(150); await hold.Task; }), "benchmark enqueue");
        double submissionMs = clock.Elapsed.TotalMilliseconds;
        Assert(!queue.Completion.IsCompleted, "worker was not pending");
        hold.SetResult();
        await queue.CompleteAsync();
        Console.WriteLine($"BENCH controlled 150ms I/O: caller sync={synchronousMs:F2}ms, enqueue={submissionMs:F2}ms (not total latency/FPS)");
    }
}
