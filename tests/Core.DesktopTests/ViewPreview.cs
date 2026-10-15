using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CoreDesktopTests;

// Captures only this synthetic fixture's embedded view, never the input desktop.
internal sealed class ViewPreview : IDisposable
{
    private readonly Window _window;
    private readonly WebView2 _view;
    private readonly string _fixture;
    private readonly DispatcherTimer _timer;
    private string _completed = "";
    private bool _busy, _disposed;

    internal ViewPreview(Window window, string fixture)
    {
        _window = window;
        _fixture = fixture;
        _view = ((Grid)window.Content).Children.OfType<WebView2>().Single();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += async (_, _) => await Poll();
        _timer.Start();
    }

    private async Task Poll()
    {
        if (_disposed || _busy) return;
        _busy = true;
        string name = "";
        try
        {
            if (_view.CoreWebView2 == null) return;
            string json = await _view.CoreWebView2.ExecuteScriptAsync("window.__nativePreviewRequest || null");
            using var request = JsonDocument.Parse(json);
            if (request.RootElement.ValueKind != JsonValueKind.Object) return;
            name = request.RootElement.GetProperty("name").GetString() ?? "";
            if (name == _completed) return;
            if (!Regex.IsMatch(name, "^[a-z0-9-]{1,60}\\.png$")) throw new InvalidDataException("Unsafe preview filename.");
            int width = request.RootElement.GetProperty("width").GetInt32();
            int height = request.RootElement.GetProperty("height").GetInt32();
            if (width < 320 || width > 1920 || height < 320 || height > 1080)
                throw new InvalidDataException("Preview viewport outside fixture bounds.");
            _window.Width = width;
            _window.Height = height;
            await Task.Delay(350);
            if (_disposed) return;
            string directory = Path.Combine(Path.GetDirectoryName(_fixture)!, "previews");
            Directory.CreateDirectory(directory);
            await using var stream = File.Create(Path.Combine(directory, name));
            await _view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            _completed = name;
            await Reply(new { ok = true, name, width, height });
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            if (!_disposed && name.Length > 0) {
                _completed = name;
                await Reply(new { ok = false, name, error = ex.Message });
            }
        }
        finally { _busy = false; }
    }

    private Task<string> Reply(object response) =>
        _view.CoreWebView2.ExecuteScriptAsync("window.__nativePreviewResult=" + JsonSerializer.Serialize(response));

    public void Dispose() { _disposed = true; _timer.Stop(); }
}
