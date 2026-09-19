using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AlekrythaeCore
{
    internal sealed class ViodCeraBridge : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const int HOTKEY_SELECTION = 0xA701;
        private const int HOTKEY_OCR = 0xA702;

        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_NOACTIVATE = 0x08000000L;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int SW_SHOWNOACTIVATE = 4;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private readonly CosmicGate _host;
        private readonly WebView2 _webView;
        private readonly Action<string, object> _sendResponse;
        private readonly Action<string> _log;
        private HwndSource? _source;
        private IntPtr _hostHwnd;
        private bool _disposed;
        private bool _allowClose;
        private bool _popupVisible;
        private int _popupSequence;
        private Rect _mainBounds;
        private bool _resizable;
        private bool _rememberBounds;
        private bool _boundsLoaded;
        private double _minWidth = 480;
        private double _minHeight = 340;
        private readonly string _windowStatePath;
        private readonly System.Windows.Threading.DispatcherTimer _boundsSaveTimer;

        private string _sourceLanguage = "en";
        private string _targetLanguage = "tr";
        private string _autoA = "en";
        private string _autoB = "tr";
        private bool _autoMode;
        private double _fontSize = 18;

        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        public bool KeepWarm => true;
        public bool AllowClose => _allowClose;

        public ViodCeraBridge(CosmicGate host, WebView2 webView, Action<string, object> sendResponse, Action<string> log)
        {
            _host = host;
            _webView = webView;
            _sendResponse = sendResponse;
            _log = log;

            _windowStatePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "KozmikData",
                "WindowState",
                "ViodCera.bounds.json");

            _boundsSaveTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _boundsSaveTimer.Tick += (_, _) =>
            {
                _boundsSaveTimer.Stop();
                SaveWindowBounds();
            };

            _host.SourceInitialized += OnSourceInitialized;
            _host.Loaded += (_, _) => RememberMainBounds();
            _host.LocationChanged += (_, _) => ScheduleBoundsSave();
            _host.SizeChanged += (_, _) => ScheduleBoundsSave();

            if (new WindowInteropHelper(_host).Handle != IntPtr.Zero)
                AttachNativeHook();
        }

        public void ShowMainWindow()
        {
            if (_disposed) return;
            SetNoActivate(false);
            ApplyResizeChrome(_resizable);
            if (_mainBounds.Width > 0 && _mainBounds.Height > 0)
            {
                _host.WindowState = WindowState.Normal;
                _host.Left = _mainBounds.Left;
                _host.Top = _mainBounds.Top;
                _host.Width = _mainBounds.Width;
                _host.Height = _mainBounds.Height;
            }
            _host.Show();
            _host.Activate();
            _host.Focus();
            _popupVisible = false;
            _ = EmitNativeEventAsync(new { type = "main" });
        }

        public void HideHost()
        {
            if (_disposed) return;
            RememberMainBoundsIfMain();
            _popupVisible = false;
            try { _host.Hide(); } catch { }
        }

        public bool TryHandleApi(string op, string requestId, JsonElement payload)
        {
            switch (op)
            {
                case "viodcera.configure":
                    ApplyConfiguration(payload);
                    _sendResponse(requestId, new { ok = true });
                    return true;

                case "viodcera.translate":
                    _ = HandleTranslateApiAsync(requestId, payload);
                    return true;

                case "viodcera.ocr.begin":
                    _sendResponse(requestId, new { ok = true, started = true });
                    _ = BeginOcrFlowAsync();
                    return true;

                case "viodcera.window.main":
                    ShowMainWindow();
                    _sendResponse(requestId, new { ok = true });
                    return true;

                case "viodcera.window.hide":
                    // Geçici native akışlarda kullanılabilir; kullanıcıdaki ana × bunu çağırmaz.
                    HideHost();
                    _sendResponse(requestId, new { ok = true });
                    return true;

                case "viodcera.window.drag":
                    BeginWindowDrag();
                    _sendResponse(requestId, new { ok = true });
                    return true;

                case "viodcera.window.toggleMaximize":
                    ToggleMaximize();
                    _sendResponse(requestId, new { ok = true, maximized = _host.WindowState == WindowState.Maximized });
                    return true;

                case "viodcera.popup.close":
                    RestoreMainWindowNoActivate();
                    _sendResponse(requestId, new { ok = true });
                    return true;

                case "viodcera.quit":
                    _allowClose = true;
                    _sendResponse(requestId, new { ok = true, closing = true });
                    _host.Dispatcher.BeginInvoke(new Action(() => _host.Close()));
                    return true;

                default:
                    return false;
            }
        }

        private void OnSourceInitialized(object? sender, EventArgs e) => AttachNativeHook();

        private void AttachNativeHook()
        {
            if (_source != null) return;
            _hostHwnd = new WindowInteropHelper(_host).Handle;
            if (_hostHwnd == IntPtr.Zero) return;

            _source = HwndSource.FromHwnd(_hostHwnd);
            _source?.AddHook(WndProc);

            RegisterHotKey(_hostHwnd, HOTKEY_SELECTION, MOD_CONTROL, (uint)KeyInterop.VirtualKeyFromKey(Key.Q));
            RegisterHotKey(_hostHwnd, HOTKEY_OCR, MOD_ALT, (uint)KeyInterop.VirtualKeyFromKey(Key.Q));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY) return IntPtr.Zero;

            int id = wParam.ToInt32();
            if (id == HOTKEY_SELECTION)
            {
                handled = true;

                // Ctrl+Q her zaman yeni bir seçim çevirisi başlatır. Popup açıkken
                // ikinci Ctrl+Q artık "geri dön" değildir; kullanıcı yeni bir metin
                // seçtiyse aynı hotkey o metni yeniden yakalayıp çevirir. Popup'tan
                // çıkış yalnız Esc / × ile yapılır.

                // RegisterHotKey WM_HOTKEY mesajı Ctrl+Q hâlâ fiziksel olarak basılıyken
                // gelebilir. O anda Ctrl+C enjekte etmek bazı uygulamalarda kopyalamayı
                // yutuyordu. Aktif hedef pencereyi şimdi sakla; kopyayı tuşlar bırakılınca yap.
                IntPtr selectionTarget = GetForegroundWindow();
                _ = TranslateCurrentSelectionAsync(selectionTarget);
            }
            else if (id == HOTKEY_OCR)
            {
                handled = true;
                _ = BeginOcrFlowAsync();
            }

            return IntPtr.Zero;
        }

        private void ApplyConfiguration(JsonElement payload)
        {
            if (payload.ValueKind != JsonValueKind.Object) return;

            if (payload.TryGetProperty("sourceLanguage", out var s) && s.ValueKind == JsonValueKind.String)
                _sourceLanguage = NormalizeLanguageCode(s.GetString(), _sourceLanguage);
            if (payload.TryGetProperty("targetLanguage", out var t) && t.ValueKind == JsonValueKind.String)
                _targetLanguage = NormalizeLanguageCode(t.GetString(), _targetLanguage);
            if (payload.TryGetProperty("autoA", out var a) && a.ValueKind == JsonValueKind.String)
                _autoA = NormalizeLanguageCode(a.GetString(), _autoA);
            if (payload.TryGetProperty("autoB", out var b) && b.ValueKind == JsonValueKind.String)
                _autoB = NormalizeLanguageCode(b.GetString(), _autoB);
            if (payload.TryGetProperty("autoMode", out var m) && (m.ValueKind == JsonValueKind.True || m.ValueKind == JsonValueKind.False))
                _autoMode = m.GetBoolean();
            if (payload.TryGetProperty("fontSize", out var f) && f.TryGetDouble(out double fs))
                _fontSize = Math.Clamp(fs, 11, 48);

            if (payload.TryGetProperty("resizable", out var r) &&
                (r.ValueKind == JsonValueKind.True || r.ValueKind == JsonValueKind.False))
                _resizable = r.GetBoolean();

            if (payload.TryGetProperty("rememberBounds", out var rb) &&
                (rb.ValueKind == JsonValueKind.True || rb.ValueKind == JsonValueKind.False))
                _rememberBounds = rb.GetBoolean();

            if (payload.TryGetProperty("minWidth", out var mw) && mw.TryGetDouble(out double minW))
                _minWidth = Math.Clamp(minW, 320, 1600);

            if (payload.TryGetProperty("minHeight", out var mh) && mh.TryGetDouble(out double minH))
                _minHeight = Math.Clamp(minH, 240, 1200);

            ApplyWindowConfiguration();
        }

        private void ApplyWindowConfiguration()
        {
            if (_disposed) return;

            _host.MinWidth = _minWidth;
            _host.MinHeight = _minHeight;

            ApplyResizeChrome(_resizable && !_popupVisible);

            if (_rememberBounds && !_boundsLoaded)
            {
                _boundsLoaded = true;
                LoadWindowBounds();
            }
        }

        private void ApplyResizeChrome(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    _host.ResizeMode = ResizeMode.CanResize;
                    System.Windows.Shell.WindowChrome.SetWindowChrome(
                        _host,
                        new System.Windows.Shell.WindowChrome
                        {
                            CaptionHeight = 0,
                            ResizeBorderThickness = new Thickness(7),
                            GlassFrameThickness = new Thickness(0),
                            CornerRadius = new CornerRadius(0),
                            UseAeroCaptionButtons = false
                        });
                }
                else
                {
                    _host.ResizeMode = ResizeMode.NoResize;
                    System.Windows.Shell.WindowChrome.SetWindowChrome(_host, null);
                }
            }
            catch (Exception ex)
            {
                _log("ViodCera resize chrome: " + ex.Message);
                _host.ResizeMode = enabled ? ResizeMode.CanResize : ResizeMode.NoResize;
            }
        }

        private void BeginWindowDrag()
        {
            if (_disposed || _popupVisible) return;
            if (_host.WindowState == WindowState.Maximized)
                _host.WindowState = WindowState.Normal;

            if (_hostHwnd == IntPtr.Zero)
                _hostHwnd = new WindowInteropHelper(_host).Handle;
            if (_hostHwnd == IntPtr.Zero) return;

            try
            {
                ReleaseCapture();
                SendMessage(_hostHwnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
            }
            catch (Exception ex)
            {
                _log("ViodCera drag: " + ex.Message);
            }
        }

        private void ToggleMaximize()
        {
            if (_disposed || _popupVisible) return;
            RememberMainBoundsIfMain();
            _host.WindowState = _host.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void ScheduleBoundsSave()
        {
            if (!_rememberBounds || _popupVisible || !_host.IsLoaded) return;
            if (_host.WindowState != WindowState.Normal) return;
            RememberMainBounds();
            _boundsSaveTimer.Stop();
            _boundsSaveTimer.Start();
        }

        private void SaveWindowBounds()
        {
            if (!_rememberBounds || _popupVisible || _host.WindowState != WindowState.Normal) return;
            try
            {
                RememberMainBounds();
                if (_mainBounds.Width <= 0 || _mainBounds.Height <= 0) return;
                string? dir = Path.GetDirectoryName(_windowStatePath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(new
                {
                    left = _mainBounds.Left,
                    top = _mainBounds.Top,
                    width = _mainBounds.Width,
                    height = _mainBounds.Height
                });
                File.WriteAllText(_windowStatePath, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                _log("ViodCera save bounds: " + ex.Message);
            }
        }

        private void LoadWindowBounds()
        {
            try
            {
                if (!File.Exists(_windowStatePath)) return;
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(_windowStatePath));
                JsonElement root = doc.RootElement;
                if (!TryReadDouble(root, "left", out double left) ||
                    !TryReadDouble(root, "top", out double top) ||
                    !TryReadDouble(root, "width", out double width) ||
                    !TryReadDouble(root, "height", out double height))
                    return;

                width = Math.Max(_minWidth, width);
                height = Math.Max(_minHeight, height);

                double vLeft = SystemParameters.VirtualScreenLeft;
                double vTop = SystemParameters.VirtualScreenTop;
                double vRight = vLeft + SystemParameters.VirtualScreenWidth;
                double vBottom = vTop + SystemParameters.VirtualScreenHeight;

                // At least a useful slice of the title area must remain reachable.
                bool visible = left < vRight - 80 && top < vBottom - 45 &&
                               left + width > vLeft + 80 && top + height > vTop + 45;
                if (!visible) return;

                _mainBounds = new Rect(left, top, width, height);
                _host.WindowStartupLocation = WindowStartupLocation.Manual;
                _host.Left = left;
                _host.Top = top;
                _host.Width = width;
                _host.Height = height;
            }
            catch (Exception ex)
            {
                _log("ViodCera load bounds: " + ex.Message);
            }
        }

        private static bool TryReadDouble(JsonElement root, string name, out double value)
        {
            value = 0;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty(name, out JsonElement el) &&
                   el.TryGetDouble(out value);
        }

        private async Task HandleTranslateApiAsync(string requestId, JsonElement payload)
        {
            try
            {
                string text = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("text", out var textEl)
                    ? (textEl.GetString() ?? string.Empty)
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(text))
                {
                    _sendResponse(requestId, new { ok = true, text = "", detectedSource = "", targetLanguage = _targetLanguage });
                    return;
                }

                TranslationResult result = await TranslateWithCurrentModeAsync(text).ConfigureAwait(true);
                _sendResponse(requestId, new
                {
                    ok = true,
                    text = result.Text,
                    detectedSource = result.DetectedSource,
                    targetLanguage = result.TargetLanguage
                });
            }
            catch (Exception ex)
            {
                _log("ViodCera translate API: " + ex);
                _sendResponse(requestId, new { ok = false, error = "translation_failed", message = ex.Message });
            }
        }

        private async Task TranslateCurrentSelectionAsync(IntPtr targetWindow)
        {
            try
            {
                string text = await CaptureSelectedTextAsync(targetWindow);
                if (string.IsNullOrWhiteSpace(text))
                {
                    await ShowStatusPopupAsync("No text selected.");
                    return;
                }

                TranslationResult result = await TranslateWithCurrentModeAsync(text);
                Rectangle bounds = GetSelectionPopupBounds();
                await ShowTranslationPopupAsync(bounds, text, result.Text, result.DetectedSource, result.TargetLanguage, "selection");
            }
            catch (Exception ex)
            {
                _log("ViodCera selection flow: " + ex);
                await ShowStatusPopupAsync("Translation could not be completed.");
            }
        }

        private async Task BeginOcrFlowAsync()
        {
            try
            {
                HideHost();
                var overlay = new ViodCeraSelectionOverlay();
                Rectangle? selected = await overlay.SelectAsync();
                if (selected == null || selected.Value.Width < 2 || selected.Value.Height < 2)
                {
                    RestoreMainWindowNoActivate();
                    return;
                }

                Rectangle region = selected.Value;
                string text = await RecognizeRegionAsync(region);
                if (string.IsNullOrWhiteSpace(text))
                {
                    await ShowTranslationPopupAsync(region, "", "No text detected in this area.", "", "", "ocr");
                    return;
                }

                TranslationResult result = await TranslateWithCurrentModeAsync(text);
                await ShowTranslationPopupAsync(region, text, result.Text, result.DetectedSource, result.TargetLanguage, "ocr");
            }
            catch (Exception ex)
            {
                _log("ViodCera OCR flow: " + ex);
                await ShowStatusPopupAsync("OCR could not be completed.");
            }
        }

        private async Task<string> CaptureSelectedTextAsync(IntPtr targetWindow)
        {
            // WM_HOTKEY, Q/Ctrl key-up olaylarından önce gelebilir. Tarayıcılar ve bazı
            // oyun/uygulamalar Ctrl hâlâ basılıyken enjekte edilen Ctrl+C'yi güvenilir
            // biçimde işlemiyor. Önce hotkey'in fiziksel olarak bırakılmasını bekliyoruz.
            bool released = false;
            for (int i = 0; i < 100; i++)
            {
                bool ctrlDown = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool qDown = (GetAsyncKeyState(0x51) & 0x8000) != 0;
                if (!ctrlDown && !qDown)
                {
                    released = true;
                    break;
                }
                await Task.Delay(15);
            }

            // Fiziksel Ctrl/Q hâlâ basılıysa sentetik Ctrl+C göndermek klavye durumunu
            // bozabilir ve hedef uygulamanın kopyayı yutmasına yol açabilir. Bu durumda
            // sessizce başarısız ol; kullanıcı tuşları bıraktığında tekrar Ctrl+Q yapabilir.
            if (!released)
                return string.Empty;

            // ViodCera hiçbir zaman foreground'u kendi üstüne almamalı. Hotkey sırasında
            // hedef pencere değişmişse seçimin bulunduğu pencereyi yeniden öne getir.
            if (targetWindow != IntPtr.Zero && GetForegroundWindow() != targetWindow)
            {
                try { SetForegroundWindow(targetWindow); } catch { }
                await Task.Delay(35);
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint before = GetClipboardSequenceNumber();
                SendCtrlC();

                // Chromium/Office gibi uygulamalar clipboard'u asenkron güncelleyebilir.
                // Sabit 90 ms yerine gerçek clipboard değişimini bekle.
                for (int i = 0; i < 40; i++)
                {
                    await Task.Delay(20);
                    uint now = GetClipboardSequenceNumber();
                    if (now == before) continue;

                    try
                    {
                        if (System.Windows.Forms.Clipboard.ContainsText())
                        {
                            string captured = System.Windows.Forms.Clipboard.GetText();
                            if (!string.IsNullOrWhiteSpace(captured))
                                return captured.Trim();
                        }
                    }
                    catch
                    {
                        // Clipboard başka süreç tarafından kısa süreli kilitlenmiş olabilir.
                    }
                }

                // Bazı uygulamalarda clipboard sequence bildirimi gecikebilir.
                // Son bir doğrudan metin okuması yap; bu yalnız Ctrl+C denemesinden sonra
                // çalıştığı için normal akışın yerini almaz.
                try
                {
                    if (System.Windows.Forms.Clipboard.ContainsText())
                    {
                        string captured = System.Windows.Forms.Clipboard.GetText();
                        if (!string.IsNullOrWhiteSpace(captured))
                            return captured.Trim();
                    }
                }
                catch { }

                await Task.Delay(60);
            }

            return string.Empty;
        }

        private async Task<string> RecognizeRegionAsync(Rectangle region)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "viodcera_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (var bmp = new Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
                    bmp.Save(tempPath, ImageFormat.Png);
                }

                StorageFile file = await StorageFile.GetFileFromPathAsync(tempPath);
                using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                var preferredCodes = _autoMode
                    ? new[] { _autoA, _autoB }
                    : new[] { _sourceLanguage };

                var candidates = new List<string>();
                foreach (string code in preferredCodes.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(code) || code == "auto") continue;
                    try
                    {
                        OcrEngine? engine = OcrEngine.TryCreateFromLanguage(new Language(ToBcp47(code)));
                        if (engine == null) continue;
                        OcrResult result = await engine.RecognizeAsync(softwareBitmap);
                        if (!string.IsNullOrWhiteSpace(result.Text)) candidates.Add(result.Text.Trim());
                    }
                    catch { }
                }

                if (candidates.Count == 0)
                {
                    OcrEngine? fallback = OcrEngine.TryCreateFromUserProfileLanguages();
                    if (fallback != null)
                    {
                        OcrResult result = await fallback.RecognizeAsync(softwareBitmap);
                        if (!string.IsNullOrWhiteSpace(result.Text)) candidates.Add(result.Text.Trim());
                    }
                }

                return candidates
                    .OrderByDescending(ScoreOcrText)
                    .FirstOrDefault() ?? string.Empty;
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private async Task<TranslationResult> TranslateWithCurrentModeAsync(string text)
        {
            if (_autoMode)
            {
                TranslationResult first = await TranslateAsync(text, "auto", _autoB);
                if (LanguageMatches(first.DetectedSource, _autoB))
                    return await TranslateAsync(text, "auto", _autoA);
                return first;
            }

            return await TranslateAsync(text, _sourceLanguage, _targetLanguage);
        }

        private static async Task<TranslationResult> TranslateAsync(string text, string source, string target)
        {
            source = string.IsNullOrWhiteSpace(source) ? "auto" : source;
            target = string.IsNullOrWhiteSpace(target) ? "en" : target;

            // Google'ın istemci anahtarı gerektirmeyen web çeviri uç noktası query
            // parametreleriyle çalışır. v0.0.1 bunları POST body'ye koyduğu için
            // bazı makinelerde 400/boş sonuç dönüyordu. Aynı sağlayıcıyı koruyup
            // doğru GET biçimine geçiriyoruz.
            string url = "https://translate.googleapis.com/translate_a/single" +
                         "?client=gtx" +
                         "&dt=t" +
                         "&sl=" + Uri.EscapeDataString(source) +
                         "&tl=" + Uri.EscapeDataString(target) +
                         "&q=" + Uri.EscapeDataString(text);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 ViodCera/0.0.3");
            request.Headers.Accept.ParseAdd("application/json,text/plain,*/*");

            using HttpResponseMessage response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            var sb = new StringBuilder();

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement segment in root[0].EnumerateArray())
                {
                    if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                        sb.Append(segment[0].GetString());
                }
            }

            if (sb.Length == 0)
                throw new InvalidOperationException("Translation provider returned an empty result.");

            string detected = "";
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
                detected = root[2].GetString() ?? "";

            return new TranslationResult(sb.ToString().Trim(), detected, target);
        }

        private async Task ShowTranslationPopupAsync(Rectangle bounds, string sourceText, string translatedText, string detectedSource, string targetLanguage, string origin)
        {
            if (_disposed) return;

            RememberMainBoundsIfMain();
            SetNoActivate(true);
            _popupVisible = true;
            ApplyResizeChrome(false);
            _popupSequence++;

            if (_host.Visibility != Visibility.Visible)
                _host.Show();

            ShowWindow(_hostHwnd, SW_SHOWNOACTIVATE);
            SetWindowPos(_hostHwnd, HWND_TOPMOST, bounds.Left, bounds.Top, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), SWP_NOACTIVATE | SWP_SHOWWINDOW);

            await Task.Delay(30);
            await EmitNativeEventAsync(new
            {
                type = "popup",
                origin,
                sourceText,
                translatedText,
                detectedSource,
                targetLanguage,
                fontSize = _fontSize
            });
        }

        private async Task ShowStatusPopupAsync(string message)
        {
            Rectangle bounds = GetPopupBoundsNearCursor(270, 74);
            await ShowTranslationPopupAsync(bounds, "", message, "", "", "status");

            // Durum uyarıları (ör. "No text selected") ana pencereyi compact
            // boyutta kilitlememeli. Kısa süre görünür, sonra otomatik olarak
            // ana pencere ölçülerine geri döner. Yeni bir popup açılmışsa eski
            // timer ona dokunmaz.
            int sequence = _popupSequence;
            await Task.Delay(1400);
            if (!_disposed && _popupVisible && sequence == _popupSequence)
                RestoreMainWindowNoActivate();
        }

        private Rectangle GetSelectionPopupBounds() => GetPopupBoundsNearCursor(420, 160);

        private Rectangle GetPopupBoundsNearCursor(int width, int height)
        {
            GetCursorPos(out POINT pt);
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
            Rectangle wa = screen.WorkingArea;
            int x = Math.Min(pt.X + 18, wa.Right - width);
            int y = Math.Min(pt.Y + 18, wa.Bottom - height);
            x = Math.Max(wa.Left, x);
            y = Math.Max(wa.Top, y);
            return new Rectangle(x, y, width, height);
        }

        private async Task EmitNativeEventAsync(object payload)
        {
            if (_webView.CoreWebView2 == null) return;
            string json = JsonSerializer.Serialize(payload).Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync($"window.__viodceraNativeEvent && window.__viodceraNativeEvent({json});");
            }
            catch (Exception ex)
            {
                _log("ViodCera event dispatch: " + ex.Message);
            }
        }

        private void RestoreMainWindowNoActivate()
        {
            if (_disposed) return;

            _popupVisible = false;
            _popupSequence++;
            SetNoActivate(false);
            ApplyResizeChrome(_resizable);

            if (_mainBounds.Width <= 0 || _mainBounds.Height <= 0)
                _mainBounds = new Rect(_host.Left, _host.Top, 560, 430);

            _host.WindowState = WindowState.Normal;
            _host.Left = _mainBounds.Left;
            _host.Top = _mainBounds.Top;
            _host.Width = _mainBounds.Width;
            _host.Height = _mainBounds.Height;

            if (_host.Visibility != Visibility.Visible)
                _host.Show();

            if (_hostHwnd == IntPtr.Zero)
                _hostHwnd = new WindowInteropHelper(_host).Handle;

            if (_hostHwnd != IntPtr.Zero)
            {
                ShowWindow(_hostHwnd, SW_SHOWNOACTIVATE);
                SetWindowPos(
                    _hostHwnd,
                    HWND_NOTOPMOST,
                    (int)Math.Round(_mainBounds.Left),
                    (int)Math.Round(_mainBounds.Top),
                    Math.Max(1, (int)Math.Round(_mainBounds.Width)),
                    Math.Max(1, (int)Math.Round(_mainBounds.Height)),
                    SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }

            _ = EmitNativeEventAsync(new { type = "main" });
        }

        private void RememberMainBounds()
        {
            if (_popupVisible) return;
            if (_host.WindowState != WindowState.Normal) return;
            _mainBounds = new Rect(_host.Left, _host.Top, _host.Width, _host.Height);
        }

        private void RememberMainBoundsIfMain()
        {
            if (!_popupVisible) RememberMainBounds();
        }

        private void SetNoActivate(bool enabled)
        {
            if (_hostHwnd == IntPtr.Zero)
                _hostHwnd = new WindowInteropHelper(_host).Handle;
            if (_hostHwnd == IntPtr.Zero) return;

            long style = GetWindowLongPtr(_hostHwnd, GWL_EXSTYLE).ToInt64();
            if (enabled) style |= WS_EX_NOACTIVATE;
            else style &= ~WS_EX_NOACTIVATE;
            SetWindowLongPtr(_hostHwnd, GWL_EXSTYLE, new IntPtr(style));
        }

        private static int ScoreOcrText(string text)
        {
            int letters = text.Count(char.IsLetterOrDigit);
            int printable = text.Count(c => !char.IsControl(c));
            return letters * 4 + printable;
        }

        private static bool LanguageMatches(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            string aa = a.Split('-')[0];
            string bb = b.Split('-')[0];
            return string.Equals(aa, bb, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLanguageCode(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            return value.Trim().ToLowerInvariant();
        }

        private static string ToBcp47(string code)
        {
            return code switch
            {
                "zh-cn" => "zh-Hans",
                "zh-tw" => "zh-Hant",
                _ => code
            };
        }

        private static void SendCtrlC()
        {
            INPUT[] inputs = new INPUT[4];
            inputs[0] = INPUT.Keyboard(0x11, 0);
            inputs[1] = INPUT.Keyboard(0x43, 0);
            inputs[2] = INPUT.Keyboard(0x43, 0x0002);
            inputs[3] = INPUT.Keyboard(0x11, 0x0002);
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        public void Dispose()
        {
            if (_disposed) return;
            SaveWindowBounds();
            _boundsSaveTimer.Stop();
            _disposed = true;
            _allowClose = true;

            try { _host.SourceInitialized -= OnSourceInitialized; } catch { }
            try { if (_source != null) _source.RemoveHook(WndProc); } catch { }
            try { if (_hostHwnd != IntPtr.Zero) UnregisterHotKey(_hostHwnd, HOTKEY_SELECTION); } catch { }
            try { if (_hostHwnd != IntPtr.Zero) UnregisterHotKey(_hostHwnd, HOTKEY_OCR); } catch { }
            _source = null;
        }

        private readonly record struct TranslationResult(string Text, string DetectedSource, string TargetLanguage);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION U;
            public static INPUT Keyboard(ushort vk, uint flags) => new INPUT
            {
                type = 1,
                U = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
            };
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)] private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)] private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : SetWindowLong32(hWnd, nIndex, value);

        private sealed class ViodCeraSelectionOverlay : Window
        {
            private const int WM_HOTKEY = 0x0312;
            private const int ID_SPACE = 0xB101;
            private const int ID_ENTER = 0xB102;
            private const int ID_ESCAPE = 0xB103;
            private const int GWL_EXSTYLE = -20;
            private const long WS_EX_NOACTIVATE = 0x08000000L;
            private const long WS_EX_TOOLWINDOW = 0x00000080L;
            private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
            private const uint SWP_NOACTIVATE = 0x0010;
            private const uint SWP_SHOWWINDOW = 0x0040;
            private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

            private readonly Canvas _canvas;
            private readonly System.Windows.Shapes.Rectangle _frame;
            private readonly TextBlock _hint;
            private readonly TextBlock[] _corners;
            private HwndSource? _source;
            private IntPtr _hwnd;
            private System.Windows.Point _start;
            private System.Windows.Point _current;
            private bool _down;
            private bool _dragged;
            private System.Drawing.Rectangle? _selectedPixels;
            private TaskCompletionSource<System.Drawing.Rectangle?>? _tcs;

            public ViodCeraSelectionOverlay()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                ResizeMode = ResizeMode.NoResize;
                ShowInTaskbar = false;
                Topmost = true;
                Background = System.Windows.Media.Brushes.Transparent;
                Left = SystemParameters.VirtualScreenLeft;
                Top = SystemParameters.VirtualScreenTop;
                Width = SystemParameters.VirtualScreenWidth;
                Height = SystemParameters.VirtualScreenHeight;

                _canvas = new Canvas
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0)),
                    Cursor = Cursors.Cross
                };

                _frame = new System.Windows.Shapes.Rectangle
                {
                    Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(146, 188, 230)),
                    StrokeThickness = 1.4,
                    Fill = System.Windows.Media.Brushes.Transparent,
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                };
                _canvas.Children.Add(_frame);

                _hint = new TextBlock
                {
                    Text = "SPACE  ◇  TRANSLATE    ENTER  ◇  OK    ESC  ◇  CANCEL",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(222, 231, 240)),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(228, 18, 23, 31)),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    FontSize = 11,
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                };
                _canvas.Children.Add(_hint);

                _corners = Enumerable.Range(0, 4).Select(_ => new TextBlock
                {
                    Text = "◇",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(165, 205, 240)),
                    FontSize = 15,
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                }).ToArray();
                foreach (var c in _corners) _canvas.Children.Add(c);

                Content = _canvas;
                _canvas.MouseLeftButtonDown += OnMouseDown;
                _canvas.MouseMove += OnMouseMove;
                _canvas.MouseLeftButtonUp += OnMouseUp;
                SourceInitialized += OnSourceInitialized;
                Closed += (_, _) => CleanupNative();
            }

            public Task<System.Drawing.Rectangle?> SelectAsync()
            {
                _tcs = new TaskCompletionSource<System.Drawing.Rectangle?>();
                Show();
                return _tcs.Task;
            }

            private void OnSourceInitialized(object? sender, EventArgs e)
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                _source = HwndSource.FromHwnd(_hwnd);
                _source?.AddHook(WndProc);

                long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
                ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
                try { SetWindowDisplayAffinity(_hwnd, WDA_EXCLUDEFROMCAPTURE); } catch { }

                RegisterHotKey(_hwnd, ID_SPACE, 0, 0x20);
                RegisterHotKey(_hwnd, ID_ENTER, 0, 0x0D);
                RegisterHotKey(_hwnd, ID_ESCAPE, 0, 0x1B);
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }

            private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (msg != WM_HOTKEY) return IntPtr.Zero;
                int id = wParam.ToInt32();
                if (id == ID_ESCAPE)
                {
                    handled = true;
                    Complete(null);
                }
                else if (id == ID_SPACE || id == ID_ENTER)
                {
                    handled = true;
                    Complete(_selectedPixels);
                }
                return IntPtr.Zero;
            }

            private void OnMouseDown(object sender, MouseButtonEventArgs e)
            {
                _down = true;
                _dragged = false;
                _start = e.GetPosition(_canvas);
                _current = _start;
                _canvas.CaptureMouse();
            }

            private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
            {
                if (!_down) return;
                _current = e.GetPosition(_canvas);
                if (Math.Abs(_current.X - _start.X) > 4 || Math.Abs(_current.Y - _start.Y) > 4)
                    _dragged = true;
                if (_dragged) UpdateSelectionFromDip(_start, _current);
            }

            private void OnMouseUp(object sender, MouseButtonEventArgs e)
            {
                if (!_down) return;
                _down = false;
                _canvas.ReleaseMouseCapture();
                _current = e.GetPosition(_canvas);

                if (_dragged)
                {
                    UpdateSelectionFromDip(_start, _current);
                }
                else
                {
                    System.Windows.Point screenDip = PointToScreen(_current);
                    var screenPoint = new System.Drawing.Point((int)Math.Round(screenDip.X), (int)Math.Round(screenDip.Y));
                    System.Drawing.Rectangle? winRect = FindUnderlyingWindowRect(screenPoint, _hwnd);
                    if (winRect != null) SetPixelSelection(winRect.Value);
                }
            }

            private void UpdateSelectionFromDip(System.Windows.Point a, System.Windows.Point b)
            {
                System.Windows.Point pa = PointToScreen(a);
                System.Windows.Point pb = PointToScreen(b);
                int left = (int)Math.Round(Math.Min(pa.X, pb.X));
                int top = (int)Math.Round(Math.Min(pa.Y, pb.Y));
                int right = (int)Math.Round(Math.Max(pa.X, pb.X));
                int bottom = (int)Math.Round(Math.Max(pa.Y, pb.Y));
                if (right - left < 2 || bottom - top < 2) return;
                SetPixelSelection(new System.Drawing.Rectangle(left, top, right - left, bottom - top));
            }

            private void SetPixelSelection(System.Drawing.Rectangle px)
            {
                _selectedPixels = px;

                System.Windows.Point tl = PointFromScreen(new System.Windows.Point(px.Left, px.Top));
                System.Windows.Point br = PointFromScreen(new System.Windows.Point(px.Right, px.Bottom));
                double left = Math.Min(tl.X, br.X);
                double top = Math.Min(tl.Y, br.Y);
                double width = Math.Max(1, Math.Abs(br.X - tl.X));
                double height = Math.Max(1, Math.Abs(br.Y - tl.Y));

                Canvas.SetLeft(_frame, left);
                Canvas.SetTop(_frame, top);
                _frame.Width = width;
                _frame.Height = height;
                _frame.Visibility = Visibility.Visible;

                var positions = new[]
                {
                    new System.Windows.Point(left - 7, top - 12),
                    new System.Windows.Point(left + width - 7, top - 12),
                    new System.Windows.Point(left - 7, top + height - 9),
                    new System.Windows.Point(left + width - 7, top + height - 9)
                };
                for (int i = 0; i < _corners.Length; i++)
                {
                    Canvas.SetLeft(_corners[i], positions[i].X);
                    Canvas.SetTop(_corners[i], positions[i].Y);
                    _corners[i].Visibility = Visibility.Visible;
                }

                _hint.Visibility = Visibility.Visible;
                _hint.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double hintLeft = Math.Max(4, Math.Min(ActualWidth - _hint.DesiredSize.Width - 4, left + width - _hint.DesiredSize.Width));
                double hintTop = top + height + 8;
                if (hintTop + _hint.DesiredSize.Height > ActualHeight)
                    hintTop = Math.Max(4, top - _hint.DesiredSize.Height - 8);
                Canvas.SetLeft(_hint, hintLeft);
                Canvas.SetTop(_hint, hintTop);
            }

            private void Complete(System.Drawing.Rectangle? result)
            {
                if (_tcs == null) return;
                var tcs = _tcs;
                _tcs = null;
                CleanupNative();
                try { Close(); } catch { }
                tcs.TrySetResult(result);
            }

            private void CleanupNative()
            {
                try { if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, ID_SPACE); } catch { }
                try { if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, ID_ENTER); } catch { }
                try { if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, ID_ESCAPE); } catch { }
                try { if (_source != null) _source.RemoveHook(WndProc); } catch { }
                _source = null;
            }

            private static System.Drawing.Rectangle? FindUnderlyingWindowRect(System.Drawing.Point point, IntPtr overlay)
            {
                System.Drawing.Rectangle? found = null;
                EnumWindows((hwnd, lParam) =>
                {
                    if (hwnd == overlay || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
                    if (!GetWindowRect(hwnd, out RECT r)) return true;
                    var rect = System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                    if (rect.Width < 20 || rect.Height < 20) return true;
                    if (!rect.Contains(point)) return true;
                    found = rect;
                    return false;
                }, IntPtr.Zero);
                return found;
            }

            [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
            private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

            [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
            [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
            [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
            [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
            [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
            [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
            [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
            [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
            [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
            [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)] private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
            [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
            [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)] private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
            private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
            private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : SetWindowLong32(hWnd, nIndex, value);
        }
    }
}
