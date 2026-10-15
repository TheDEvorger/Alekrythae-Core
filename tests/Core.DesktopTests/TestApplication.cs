using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AlekrythaeCore;

namespace CoreDesktopTests;

internal static class TestApplication
{
    internal static int Run(string expectedDesktop, string fixture)
    {
        string actual = NativeDesktop.CurrentThreadName();
        if (!expectedDesktop.StartsWith(NativeDesktop.Prefix, StringComparison.Ordinal) || actual != expectedDesktop)
            throw new InvalidOperationException("Refusing to create a test window outside its private desktop.");
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        int result = 0;
        app.DispatcherUnhandledException += (_, error) =>
        {
            Program.Write(fixture, "host-error.json", new { error = error.Exception.ToString() });
            result = 1;
            error.Handled = true;
            app.Shutdown(1);
        };
        // Reuse the production window/bridge without Program.Main: no shared mutex,
        // pipe server, tray, file association registration or normal-app activation.
        var gate = new CosmicGate(fixture) { ShowActivated = false, ShowInTaskbar = false };
        gate.SourceInitialized += (_, _) =>
        {
            uint thread = WindowNative.GetWindowThreadProcessId(new WindowInteropHelper(gate).Handle, out _);
            string windowDesktop = WindowNative.DesktopName(thread);
            if (windowDesktop != expectedDesktop) throw new InvalidOperationException("Window escaped private desktop.");
            Program.Write(fixture, "host-window.json", new { desktop = windowDesktop, actual, expectedDesktop });
        };
        using var previews = new ViewPreview(gate, fixture);
        using var metrics = new SessionMetrics(gate, fixture);
        using var requests = new MetricsRequest(gate, metrics);
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(FixtureOptions.TimeoutSeconds(fixture)) };
        timeout.Tick += (_, _) =>
        {
            Program.Write(fixture, "host-error.json", new { error = "WebView2 fixture exceeded its configured timeout." });
            result = 124;
            app.Shutdown(124);
        };
        timeout.Start();
        app.Run(gate);
        timeout.Stop();
        return result;
    }
}
