using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Resources;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;

namespace AlekrythaeCore
{
    public class CosmicGate : Window
    {
        private WebView2 _webView;
        private string _jsPath;
        private string _memoryPath; // .alek dosyasının yanındaki .json hafıza dosyası
        private string _rootFolder; // Güvenlik sandbox'ı: .alek dosyasının bulunduğu klasör
        private string _engineDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KozmikData");

        // Desteklenen dosya uzantıları
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
        private static readonly string[] AudioExtensions = { ".mp3", ".m4a", ".wav", ".ogg" };

        public CosmicGate(string jsPath)
        {
            _jsPath = jsPath;
            // HAFIZA: .alek dosyasının yanına aynı isimle .json yaz
            _memoryPath = jsPath + ".json";
            // GÜVENLİK: Root klasör — JS sadece bu klasör ve alt klasörlerinde işlem yapabilir
            string dir = Path.GetDirectoryName(Path.GetFullPath(jsPath)) ?? AppDomain.CurrentDomain.BaseDirectory;
            _rootFolder = dir.EndsWith(Path.DirectorySeparatorChar.ToString()) ? dir : dir + Path.DirectorySeparatorChar;

            // Şase Ayarları
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 5, 10));

            // Uygulama basıldığı anda zınk diye ekrana gelsin diye (Program.cs desteğiyle beraber):
            this.WindowState = WindowState.Normal;
            this.Activate();

            // 1. PENCERE ŞASESİ: Modern, Kenarlıksız ve Transparan
            this.Title = "Kozmik Kapı - " + Path.GetFileName(jsPath);
            this.Width = 1024;
            this.Height = 768;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // 2. ANA LAYOUT (Grid)
            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(45) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 3. ÜST BAR (TITLE BAR) - SİYAH BASKIN YUMUŞAK GRADIENT
            Border titleBar = new Border();
            LinearGradientBrush headerGradient = new LinearGradientBrush();
            headerGradient.StartPoint = new System.Windows.Point(0, 0.5);
            headerGradient.EndPoint = new System.Windows.Point(1, 0.5);

            // 1. Durak: Saf derin siyah (Barın yarısına kadar %50 baskın)
            headerGradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(10, 10, 12), 0.0));
            headerGradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(10, 10, 12), 0.5));

            // 2. Durak: Yumuşak geçiş bölgesi
            headerGradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(25, 40, 70), 0.8));

            // 3. Durak: Hedef siber mavi
            headerGradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(58, 123, 213), 1.0));

            titleBar.Background = headerGradient;
            Grid.SetRow(titleBar, 0);

            // Sürükleme Özelliği
            titleBar.MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) this.DragMove(); };

            Grid titleContent = new Grid();
            titleBar.Child = titleContent;

            // ==========================================
            // İŞTE BURASI DÜZELDİ: KALİGRAFİ (Sol Üst)
            // ==========================================
            try
            {
                System.Windows.Controls.Image logo = new System.Windows.Controls.Image
                {
                    // Artık dosya aramak yok, direkt uygulamanın kalbinden (pack://) çekiyoruz
                    Source = new BitmapImage(new Uri("pack://application:,,,/Resources/Alekrythae.png", UriKind.RelativeOrAbsolute)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Margin = new Thickness(15, 0, 0, 0),
                    Height = 25,
                    Stretch = Stretch.Uniform
                };
                titleContent.Children.Add(logo);
            }
            catch { /* Hata olsa da programı patlatma */ }
            // ==========================================

            // SÜRÜKLEME VE ÇİFT TIKLAMA PROTOKOLÜ
            titleBar.MouseLeftButtonDown += (s, e) => {
                if (e.ClickCount == 2)
                {
                    this.WindowState = this.WindowState == WindowState.Maximized
                        ? WindowState.Normal : WindowState.Maximized;
                }
                else if (e.LeftButton == MouseButtonState.Pressed)
                {
                    this.DragMove();
                }
            };

            // F11 TAM EKRAN KISAYOLU
            this.KeyDown += (s, e) => {
                if (e.Key == System.Windows.Input.Key.F11)
                {
                    if (this.WindowState == WindowState.Maximized && titleBar.Visibility == Visibility.Collapsed)
                    {
                        this.WindowState = WindowState.Normal;
                        titleBar.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        this.WindowState = WindowState.Maximized;
                        titleBar.Visibility = Visibility.Collapsed;
                    }
                }
            };

            // KAPATMA BUTONU (Sağ Üst)
            System.Windows.Controls.Button closeBtn = new System.Windows.Controls.Button
            {
                Content = "✕",
                Width = 45,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 18,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            closeBtn.Click += (s, e) => this.Close();
            closeBtn.MouseEnter += (s, e) => closeBtn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 50, 50));
            closeBtn.MouseLeave += (s, e) => closeBtn.Background = System.Windows.Media.Brushes.Transparent;

            titleContent.Children.Add(closeBtn);

            // 4. WEB İÇERİK ALANI
            _webView = new WebView2
            {
                Visibility = Visibility.Visible,
                DefaultBackgroundColor = System.Drawing.Color.Transparent
            };
            Grid.SetRow(_webView, 1);

            mainGrid.Children.Add(titleBar);
            mainGrid.Children.Add(_webView);
            this.Content = mainGrid;

            this.Loaded += async (s, e) => {
                try
                {
                    var env = await CoreWebView2Environment.CreateAsync(null, _engineDataFolder);
                    await _webView.EnsureCoreWebView2Async(env);

                    // ====== VIRTUAL HOST MAPPING ======
                    string? alekFolder = Path.GetDirectoryName(Path.GetFullPath(_jsPath));
                    if (!string.IsNullOrEmpty(alekFolder))
                    {
                        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                            "alek-assets.local", alekFolder,
                            CoreWebView2HostResourceAccessKind.Allow);
                    }

                    // Resources klasörü için ayrı virtual host (Bluemoon.png vs.)
                    string resourcesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
                    if (Directory.Exists(resourcesFolder))
                    {
                        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                            "alek-engine.local", resourcesFolder,
                            CoreWebView2HostResourceAccessKind.Allow);
                    }
                    // ==================================

                    // ====== JS <-> C# KÖPRÜSÜ KURULUMU ======
                    _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                    // ========================================

                    FireUpJs();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Çekirdek Hatası: {ex.Message}");
                }
            };
        }

        // ============================================================
        // GÜVENLİ PATH ÇÖZÜCÜ
        // Relative path'i alır, root klasörle birleştirir, sandbox dışına
        // çıkış denemelerini engeller. Güvensizse null döner.
        // ============================================================
        private string? ResolveSafePath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;

            // Absolute path, farklı disk veya UNC engelle
            if (Path.IsPathRooted(relativePath)) return null;

            try
            {
                string combined = Path.GetFullPath(Path.Combine(_rootFolder, relativePath));

                // Root dışına çıkış kontrolü (trailing separator sayesinde prefix collision önlenir)
                if (!combined.StartsWith(_rootFolder, StringComparison.OrdinalIgnoreCase))
                    return null;

                return combined;
            }
            catch
            {
                return null; // Geçersiz karakterler vs.
            }
        }

        // ============================================================
        // C# → JS RESPONSE GÖNDERİCİ
        // ============================================================
        private void SendResponse(string requestId, object result)
        {
            try
            {
                string json = JsonSerializer.Serialize(result);
                string idEscaped = JsonSerializer.Serialize(requestId);
                string script = $"window.__alekDispatch({idEscaped},{json})";
                _webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Response hatası: " + ex.Message);
            }
        }

        // ============================================================
        // JS -> C# KÖPRÜSÜ
        // Eski mesajlar (id'siz): { "op":"save", "data":"..." } → geriye uyumlu
        // Yeni mesajlar (id'li):  { "op":"fs.readText", "id":"req_1", "payload":{...} }
        // ============================================================
        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.TryGetWebMessageAsString();
                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("op", out var opEl)) return;
                string op = opEl.GetString() ?? "";

                // Yeni API sistemi: id varsa Promise tabanlı response
                if (doc.RootElement.TryGetProperty("id", out var idEl))
                {
                    string reqId = idEl.GetString() ?? "";
                    JsonElement payload = doc.RootElement.TryGetProperty("payload", out var pEl)
                        ? pEl : default;
                    HandleApiRequest(op, reqId, payload);
                    return;
                }

                // ====== ESKİ SİSTEM (GERİYE UYUMLU) ======
                if (op == "save")
                {
                    string data = doc.RootElement.TryGetProperty("data", out var dEl)
                        ? (dEl.GetString() ?? "{}") : "{}";
                    // Atomik yazma: önce .tmp'a, sonra rename
                    string tmp = _memoryPath + ".tmp";
                    File.WriteAllText(tmp, data);
                    if (File.Exists(_memoryPath)) File.Delete(_memoryPath);
                    File.Move(tmp, _memoryPath);
                }
                else if (op == "reveal")
                {
                    // Hafıza dosyasını Windows Gezgini'nde aç ve seç
                    if (File.Exists(_memoryPath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_memoryPath}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Köprü hatası: " + ex.Message);
            }
        }

        // ============================================================
        // YENİ API İSTEK İŞLEYİCİ — 9 OPERASYON
        // ============================================================
        private void HandleApiRequest(string op, string reqId, JsonElement payload)
        {
            try
            {
                string relPath = "";
                if (payload.ValueKind == JsonValueKind.Object &&
                    payload.TryGetProperty("path", out var pathEl))
                {
                    relPath = pathEl.GetString() ?? "";
                }

                switch (op)
                {
                    // ─── fs.list ───
                    case "fs.list":
                    {
                        string? safePath = string.IsNullOrEmpty(relPath) ? _rootFolder.TrimEnd(Path.DirectorySeparatorChar) : ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }
                        if (!Directory.Exists(safePath)) { SendResponse(reqId, new { ok = false, error = "not_found" }); return; }

                        var items = new List<object>();
                        foreach (var d in Directory.GetDirectories(safePath))
                            items.Add(new { name = Path.GetFileName(d), type = "directory" });
                        foreach (var f in Directory.GetFiles(safePath))
                            items.Add(new { name = Path.GetFileName(f), type = "file", size = new FileInfo(f).Length });

                        SendResponse(reqId, new { ok = true, items });
                        return;
                    }

                    // ─── fs.readText ───
                    case "fs.readText":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }
                        if (!File.Exists(safePath)) { SendResponse(reqId, new { ok = false, error = "not_found" }); return; }

                        string content = File.ReadAllText(safePath, System.Text.Encoding.UTF8);
                        SendResponse(reqId, new { ok = true, content });
                        return;
                    }

                    // ─── fs.writeText ───
                    case "fs.writeText":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        string content = "";
                        if (payload.ValueKind == JsonValueKind.Object &&
                            payload.TryGetProperty("content", out var cEl))
                            content = cEl.GetString() ?? "";

                        string? dir = Path.GetDirectoryName(safePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        File.WriteAllText(safePath, content, System.Text.Encoding.UTF8);
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── fs.readJson ───
                    case "fs.readJson":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }
                        if (!File.Exists(safePath)) { SendResponse(reqId, new { ok = false, error = "not_found" }); return; }

                        string raw = File.ReadAllText(safePath, System.Text.Encoding.UTF8);
                        try
                        {
                            using var jsonDoc = JsonDocument.Parse(raw);
                            // Geçerli JSON — ham metni data olarak gönder (JS tarafında parse edilecek)
                            string jsonStr = JsonSerializer.Serialize(new { ok = true });
                            // data alanını ham JSON olarak ekle
                            jsonStr = jsonStr.TrimEnd('}') + ",\"data\":" + raw + "}";
                            string idEsc = JsonSerializer.Serialize(reqId);
                            _webView.CoreWebView2.ExecuteScriptAsync($"window.__alekDispatch({idEsc},{jsonStr})");
                        }
                        catch (JsonException)
                        {
                            SendResponse(reqId, new { ok = false, error = "invalid_json" });
                        }
                        return;
                    }

                    // ─── fs.writeJson ───
                    case "fs.writeJson":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        string jsonContent = "{}";
                        if (payload.ValueKind == JsonValueKind.Object &&
                            payload.TryGetProperty("data", out var dataEl))
                        {
                            jsonContent = JsonSerializer.Serialize(dataEl, new JsonSerializerOptions { WriteIndented = true });
                        }

                        string? dir = Path.GetDirectoryName(safePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        File.WriteAllText(safePath, jsonContent, System.Text.Encoding.UTF8);
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── fs.exists ───
                    case "fs.exists":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        if (File.Exists(safePath))
                            SendResponse(reqId, new { ok = true, exists = true, type = "file" });
                        else if (Directory.Exists(safePath))
                            SendResponse(reqId, new { ok = true, exists = true, type = "directory" });
                        else
                            SendResponse(reqId, new { ok = true, exists = false });
                        return;
                    }

                    // ─── fs.mkdir ───
                    case "fs.mkdir":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        Directory.CreateDirectory(safePath);
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── fs.delete ───
                    case "fs.delete":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        if (Directory.Exists(safePath))
                        {
                            // Güvenlik: klasör silme kapalı
                            SendResponse(reqId, new { ok = false, error = "directory_delete_disabled" });
                            return;
                        }
                        if (!File.Exists(safePath))
                        {
                            SendResponse(reqId, new { ok = false, error = "not_found" });
                            return;
                        }

                        File.Delete(safePath);
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── fs.writeBinary ───
                    case "fs.writeBinary":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        string base64 = "";
                        if (payload.ValueKind == JsonValueKind.Object &&
                            payload.TryGetProperty("base64", out var b64El))
                            base64 = b64El.GetString() ?? "";

                        if (string.IsNullOrEmpty(base64)) { SendResponse(reqId, new { ok = false, error = "empty_data" }); return; }

                        string? dir = Path.GetDirectoryName(safePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        byte[] bytes = Convert.FromBase64String(base64);
                        File.WriteAllBytes(safePath, bytes);
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── alek.load ───
                    case "alek.load":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null || !safePath.EndsWith(".alek", StringComparison.OrdinalIgnoreCase))
                        {
                            SendResponse(reqId, new { ok = false, error = "invalid_path" });
                            return;
                        }
                        if (!File.Exists(safePath))
                        {
                            SendResponse(reqId, new { ok = false, error = "not_found" });
                            return;
                        }

                        // Hedef .alek dosyasına geç
                        _jsPath = safePath;
                        _memoryPath = safePath + ".json";
                        // _rootFolder DEĞİŞMEZ — aynı sandbox içinde kalır

                        // Sayfayı yeniden yükle
                        Dispatcher.Invoke(() => FireUpJs());
                        // Response gönderilmez — sayfa yeniden yüklenecek
                        return;
                    }

                    default:
                        SendResponse(reqId, new { ok = false, error = "unknown_op" });
                        return;
                }
            }
            catch (Exception ex)
            {
                SendResponse(reqId, new { ok = false, error = ex.Message });
            }
        }

        // ============================================================
        // BLUEMOON.PNG → VIRTUAL HOST URL
        // ============================================================
        private string GetBluemoonUrl()
        {
            // Resources klasöründen virtual host üzerinden sun
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Bluemoon.png");
            if (File.Exists(localPath))
            {
                return "https://alek-engine.local/Bluemoon.png";
            }
            // Fallback: base64 data URL
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/Bluemoon.png", UriKind.Absolute);
                StreamResourceInfo sri = System.Windows.Application.GetResourceStream(uri);
                if (sri != null)
                {
                    using var ms = new MemoryStream();
                    sri.Stream.CopyTo(ms);
                    string b64 = Convert.ToBase64String(ms.ToArray());
                    return "data:image/png;base64," + b64;
                }
            }
            catch { }
            return "";
        }

        // ============================================================
        // RESİM KEŞFİ VE NUMERİC SIRALAMA
        // ============================================================
        private List<string> DiscoverSlides(string alekFolder)
        {
            // Sunum Resim klasörüne bak, yoksa .alek klasörünü kullan
            string imageFolder = Path.Combine(alekFolder, "Sunum Resim");
            if (!Directory.Exists(imageFolder))
            {
                imageFolder = alekFolder;
            }

            var files = Directory.GetFiles(imageFolder)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            // Numeric sort: dosya adındaki ilk sayıyı yakala
            var regex = new Regex(@"\d+");
            files.Sort((a, b) =>
            {
                string nameA = Path.GetFileName(a);
                string nameB = Path.GetFileName(b);
                var matchA = regex.Match(nameA);
                var matchB = regex.Match(nameB);

                long numA = matchA.Success ? long.Parse(matchA.Value) : long.MaxValue;
                long numB = matchB.Success ? long.Parse(matchB.Value) : long.MaxValue;

                int cmp = numA.CompareTo(numB);
                if (cmp != 0) return cmp;

                // Aynı ilk sayıda ikincil sıralama: dosya adı alfabetik
                return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
            });

            return files;
        }

        // ============================================================
        // MÜZİK KEŞFİ
        // ============================================================
        private string? DiscoverAudio(string alekFolder)
        {
            // Önce .alek dosyasının bulunduğu klasörde ara
            var audioFile = FindFirstAudio(alekFolder);
            if (audioFile != null) return audioFile;

            // Yoksa Sunum Resim klasöründe ara
            string imageFolder = Path.Combine(alekFolder, "Sunum Resim");
            if (Directory.Exists(imageFolder))
            {
                audioFile = FindFirstAudio(imageFolder);
            }
            return audioFile;
        }

        private string? FindFirstAudio(string folder)
        {
            try
            {
                return Directory.GetFiles(folder)
                    .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        // ============================================================
        // DOSYA YOLUNU VIRTUAL HOST URL'İNE ÇEVİR
        // ============================================================
        private string ToVirtualUrl(string filePath, string alekFolder)
        {
            // alekFolder'a göre göreli yolu al
            string relativePath = Path.GetRelativePath(alekFolder, filePath);
            // Windows path separator'ı URL separator'a çevir
            string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Her segmenti ayrı ayrı encode et (boşluklar, Türkçe karakterler vs.)
            string encodedPath = string.Join("/", segments.Select(seg => Uri.EscapeDataString(seg)));
            return "https://alek-assets.local/" + encodedPath;
        }

        // ============================================================
        // ANA MOTOR: JS ATEŞLEME
        // ============================================================
        private void FireUpJs()
        {
            if (!File.Exists(_jsPath)) return;
            string jsContent = File.ReadAllText(_jsPath);
            string alekFolder = Path.GetDirectoryName(Path.GetFullPath(_jsPath)) ?? "";

            // ====== HAFIZAYI DİSKTEN OKU (varsa) ======
            string memoryJson = "null";
            try
            {
                if (File.Exists(_memoryPath))
                {
                    string raw = File.ReadAllText(_memoryPath);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        // Geçerli JSON mu kontrol et; geçerliyse aynen ver, değilse null
                        using var _ = JsonDocument.Parse(raw);
                        memoryJson = raw;
                    }
                }
            }
            catch { memoryJson = "null"; }

            // Ham JSON metnini JS tek-tırnaklı string literal'i içinde güvenle gömmek için kaçır.
            // Sıra önemli: önce ters slash, sonra diğerleri.
            string memoryJsForLiteral = (memoryJson ?? "null")
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\u2028", "\\u2028") // U+2028 LINE SEPARATOR — JS literal'inde kabul edilmez
                .Replace("\u2029", "\\u2029") // U+2029 PARAGRAPH SEPARATOR
                .Replace("</", "<\\/");        // erken </script> kapanışı koruması

            // ====== ASSET KEŞFİ ======
            string moonDataUrl = GetBluemoonUrl();

            // Resim keşfi
            List<string> slideFiles = DiscoverSlides(alekFolder);
            var slideUrls = slideFiles.Select(f => ToVirtualUrl(f, alekFolder)).ToList();
            string slidesArrayJs = "[" + string.Join(",", slideUrls.Select(u => "\"" + EscapeJsString(u) + "\"")) + "]";

            // Müzik keşfi
            string? audioFile = DiscoverAudio(alekFolder);
            string audioJs;
            string audioNameJs;
            if (audioFile != null)
            {
                string audioUrl = ToVirtualUrl(audioFile, alekFolder);
                audioJs = "\"" + EscapeJsString(audioUrl) + "\"";
                audioNameJs = "\"" + EscapeJsString(Path.GetFileName(audioFile)) + "\"";
            }
            else
            {
                audioJs = "null";
                audioNameJs = "null";
            }

            string assetsInjector = $@"
                window.__alekAssets = {{
                    moon: ""{EscapeJsString(moonDataUrl)}"",
                    audio: {audioJs},
                    audioName: {audioNameJs},
                    slides: {slidesArrayJs},
                    slideCount: {slideUrls.Count}
                }};
            ";

            string injector = @"
                // ====== ALEKRYTHAE KÖPRÜSÜ (JS<->C#) ======
                window.__alekStorage = (function() {
                    try { return JSON.parse('" + memoryJsForLiteral + @"'); } catch(e) { return null; }
                })();
                window.__alekSave = function(jsonString) {
                    try {
                        if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                            window.chrome.webview.postMessage({ op: 'save', data: jsonString });
                            return true;
                        }
                    } catch(e) { console.error('alekSave köprü hatası:', e); }
                    return false;
                };
                window.__alekReveal = function() {
                    try {
                        if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                            window.chrome.webview.postMessage({ op: 'reveal' });
                        }
                    } catch(e) {}
                };
                // ==========================================

                // ====== YENİ DOSYA SİSTEMİ KÖPRÜSÜ (Promise tabanlı) ======
                window.__alekPending = {};
                window.__alekReqId = 0;

                window.__alekDispatch = function(id, result) {
                    if (window.__alekPending[id]) {
                        window.__alekPending[id](result);
                        delete window.__alekPending[id];
                    }
                };

                window.__alekAPI = function(op, payload) {
                    return new Promise(function(resolve) {
                        var id = '__alek_' + (++window.__alekReqId);
                        window.__alekPending[id] = resolve;
                        try {
                            window.chrome.webview.postMessage(JSON.stringify({ op: op, id: id, payload: payload || {} }));
                        } catch(e) {
                            delete window.__alekPending[id];
                            resolve({ ok: false, error: 'bridge_unavailable' });
                        }
                    });
                };

                // Yardımcı fonksiyonlar
                window.__alekReadText = function(p) { return window.__alekAPI('fs.readText', { path: p }); };
                window.__alekWriteText = function(p, c) { return window.__alekAPI('fs.writeText', { path: p, content: c }); };
                window.__alekReadJson = function(p) { return window.__alekAPI('fs.readJson', { path: p }); };
                window.__alekWriteJson = function(p, d) { return window.__alekAPI('fs.writeJson', { path: p, data: d }); };
                window.__alekList = function(p) { return window.__alekAPI('fs.list', { path: p || '' }); };
                window.__alekExists = function(p) { return window.__alekAPI('fs.exists', { path: p }); };
                window.__alekMkdir = function(p) { return window.__alekAPI('fs.mkdir', { path: p }); };
                window.__alekDelete = function(p) { return window.__alekAPI('fs.delete', { path: p }); };
                window.__alekWriteBinary = function(p, b64) { return window.__alekAPI('fs.writeBinary', { path: p, base64: b64 }); };
                window.__alekLoad = function(p) { return window.__alekAPI('alek.load', { path: p }); };
                // ===========================================================

                const style = document.createElement('style');
                style.innerHTML = '.alek-msg { position:fixed; top:20px; left:50%; transform:translateX(-50%); background:rgba(5,10,20,0.95); border:1px solid #3A7BD5; color:#E0E0E0; padding:15px 30px; border-radius:8px; box-shadow: 0 0 20px rgba(58,123,213,0.5); font-family:Consolas; z-index:9999; }';
                document.head.appendChild(style);
                // Not: window.Notice'i .alek dosyası kendi shim'inde override edecek;
                // burada sadece fallback olarak duruyor (script yüklenmezse diye).
                if (typeof window.Notice !== 'function') {
                    window.alert = window.Notice = function(msg) {
                        const b = document.createElement('div'); b.className='alek-msg'; b.innerText=msg; document.body.appendChild(b);
                        setTimeout(() => b.remove(), 4000);
                    };
                }
            ";

            string html = $"<html><body style='background:transparent; color:white; font-family:sans-serif; margin:0; padding:20px;'><script>{injector}</script><script>{assetsInjector}</script><script type='module'>{jsContent}</script></body></html>";
            _webView.NavigateToString(html);
        }

        // ============================================================
        // JS STRING ESCAPE HELPER
        // ============================================================
        private static string EscapeJsString(string s)
        {
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\u2028", "\\u2028")
                .Replace("\u2029", "\\u2029")
                .Replace("</", "<\\/");
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try { _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived; } catch { }
            _webView.Dispose();
        }
    }
}