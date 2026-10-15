using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace CoreDesktopTests;

// Own-fixture process counters at explicit benchmark boundaries, never user processes.
internal sealed class MetricsRequest : IDisposable
{
    private readonly WebView2 _view;
    private readonly SessionMetrics _metrics;
    private readonly DispatcherTimer _timer;
    private string _completed = "";
    private bool _busy, _disposed;

    internal MetricsRequest(Window window, SessionMetrics metrics)
    {
        _view = ((Grid)window.Content).Children.OfType<WebView2>().Single();
        _metrics = metrics;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += async (_, _) => await Poll();
        _timer.Start();
    }

    private async Task Poll()
    {
        if (_disposed || _busy) return;
        _busy = true;
        try {
            if (_view.CoreWebView2 == null) return;
            string json = await _view.CoreWebView2.ExecuteScriptAsync("window.__nativeMetricsRequest || null");
            using var request = JsonDocument.Parse(json);
            if (request.RootElement.ValueKind != JsonValueKind.Object) return;
            string name = request.RootElement.GetProperty("name").GetString() ?? "";
            if (name == _completed || !Regex.IsMatch(name, "^[a-z0-9-]{1,80}$")) return;
            _completed = name;
            var reply = new { ok = true, name, sample = _metrics.ReadNow() };
            await _view.CoreWebView2.ExecuteScriptAsync("window.__nativeMetricsResult=" + JsonSerializer.Serialize(reply));
        }
        catch (ObjectDisposedException) { }
        finally { _busy = false; }
    }

    public void Dispose() { _disposed = true; _timer.Stop(); }
}
