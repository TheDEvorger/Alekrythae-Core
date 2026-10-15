using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace CoreDesktopTests;

internal sealed class SessionMetrics : IDisposable
{
    private readonly WebView2 _view;
    private readonly string _fixture;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private readonly List<object> _samples = new();

    internal SessionMetrics(Window window, string fixture)
    {
        _fixture = fixture;
        _view = ((Grid)window.Content).Children.OfType<WebView2>().Single();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
        Sample();
    }

    private void Sample() => _samples.Add(ReadNow());

    internal object ReadNow()
    {
        var processes = new List<object>();
        Add(Environment.ProcessId, "test-host", processes);
        bool browserClosed = false;
        try {
            if (_view.CoreWebView2 != null) {
                foreach (var info in _view.CoreWebView2.Environment.GetProcessInfos())
                    Add(info.ProcessId, info.Kind.ToString(), processes);
            }
        }
        catch (ObjectDisposedException) { browserClosed = true; }
        return new { elapsedMs = _elapsed.ElapsedMilliseconds, browserClosed, processes };
    }

    private static void Add(int id, string kind, List<object> result)
    {
        try {
            using var process = Process.GetProcessById(id);
            process.Refresh();
            result.Add(new { id, kind, workingSetBytes = process.WorkingSet64,
                privateBytes = process.PrivateMemorySize64, cpuMs = process.TotalProcessorTime.TotalMilliseconds });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) {
            result.Add(new { id, kind, unavailable = ex.Message });
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        Sample();
        Program.Write(_fixture, "host-metrics.json", new {
            scope = "Private-desktop fixture host and its WebView2 environment; working sets may share pages",
            foregroundPerformance = false, gpuUtilizationMeasured = false, durationMs = _elapsed.ElapsedMilliseconds, samples = _samples
        });
    }
}
